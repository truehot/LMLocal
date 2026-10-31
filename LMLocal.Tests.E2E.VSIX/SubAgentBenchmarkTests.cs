using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Tests.E2E.VSIX
{
    /// <summary>
    /// Manual end-to-end benchmark for the built-in SubAgents: real model (LM Studio),
    /// real VS Experimental instance, real tools. One VS instance for the whole run.
    /// </summary>
    [TestClass, TestCategory("SubAgentBenchmark")]
    public class SubAgentBenchmarkTests
    {
        private const string PipeName = "LMLocal.Ipc";

        private const string Explorer = "code_explorer_subagent";
        private const string Reader = "code_reader_subagent";
        private const string Analyzer = "symbol_analyzer_subagent";
        private const string Reviewer = "reviewer_subagent";
        private const string Editor = "edit_subagent";

        public TestContext TestContext { get; set; }

        // ---------------------------------------------------------------------
        // Data model
        // ---------------------------------------------------------------------

        private sealed class Scenario
        {
            public string Id;
            public string AgentId;
            public string Prompt;
            public string[] MustContain = new string[0];

            // Alternative evidence groups (OR-sets). Each inner array is a group;
            public string[][] MustContainAny = new string[0][];

            // Ordered evidence: each marker must appear in the answer AND its first occurrence must come after the first occurrence of the previous marker.
            public string[] MustContainInOrder = new string[0];
            // Hard negative evidence: each string MUST NOT appear anywhere in the answer.
            public string[] MustNotContain = new string[0];
            // Exactly-once evidence: each marker must appear exactly once in the final answer.
            // 0 occurrences = missing; 2+ = the same content arrived twice (printed in the reply
            // text AND expanded from a ref marker by the runner, or via overlapping markers).
            public string[] MustAppearExactlyOnce = new string[0];
            public bool ExpectNotFound;
            public bool CheckRefusal = true;

            // ---- edit_subagent fixture (Temp\AgentProbe under the solution root) ----
            // True => both seed files are (re)created before the run and verified on disk after it.
            public bool WithFixture;

            // Exact expected content after the run. null => the file must still equal its seed.
            public string ExpectedProbe;
            public string ExpectedNotes;

            // Folder inventory right after the reset; files that appear later were created by the agent.
            public HashSet<string> FixtureFilesBefore;

            // The scenario legitimately names a path that must not exist (a missing file).
            public bool SkipInventedPathCheck;

            // Soft evidence: same semantics as MustContain / MustContainAny / MustNotContain, but a
            // hit produces a non-hard violation (PARTIAL) instead of FAIL. Used for report quality,
            // so a correct edit on disk is never masked by a cosmetic reporting defect.
            public string[] SoftMustContain = new string[0];
            public string[][] SoftMustContainAny = new string[0][];
            public string[] SoftMustNotContain = new string[0];

            public TimeSpan ReadTimeout = TimeSpan.FromMinutes(5);
        }

        private sealed class Violation
        {
            public string Text;
            public bool Hard;
            public Violation(string text, bool hard)
            {
                Text = text;
                Hard = hard;
            }
        }

        private sealed class BenchmarkResult
        {
            public string Id;
            public string AgentId;
            public string Prompt;
            public string Verdict;
            public string Content;
            public List<string> ToolsUsed = new List<string>();
            public int Rounds;
            public double DurationMs;
            public int? PromptTokens;
            public int? CompletionTokens;
            public int? TotalTokens;
            public double TokensPerSecond;
            public List<string> Violations = new List<string>();
        }

        // Mirror of SubAgentsRunResponse serialized over the pipe.
        private sealed class RunResponseDto
        {
            public bool Success { get; set; }
            public string Content { get; set; }
            public string Error { get; set; }
            public string Model { get; set; }
            public string RunId { get; set; }
            public long DurationMs { get; set; }
            public int? PromptTokens { get; set; }
            public int? CompletionTokens { get; set; }
            public int? TotalTokens { get; set; }
            public double TokensPerSecond { get; set; }
            public int Rounds { get; set; }
            public List<string> ToolsUsed { get; set; }
            public int ExpandedRefCount { get; set; }
            public int UnresolvedRefCount { get; set; }
        }

        // ---------------------------------------------------------------------
        // Scenario builders
        // ---------------------------------------------------------------------

        private static Scenario Sc(string id, string agentId, string prompt, params string[] mustContain)
        {
            return new Scenario
            {
                Id = id,
                AgentId = agentId,
                Prompt = prompt,
                MustContain = mustContain ?? new string[0]
            };
        }

        private static Scenario NotFoundSc(string id, string agentId, string prompt, params string[] mustContain)
        {
            var s = Sc(id, agentId, prompt, mustContain);
            s.ExpectNotFound = true;
            return s;
        }


        /// <summary>
        /// Verbatim-file reads can be very long, so we disable the refusal check for them. The agent is expected to return the file content verbatim, not a refusal.
        /// </summary>
        private static Scenario ScVerbatim(string id, string agentId, string prompt, params string[] mustContain)
        {
            var s = Sc(id, agentId, prompt, mustContain);
            s.CheckRefusal = false;
            return s;
        }


        // ---------------------------------------------------------------------
        // edit_subagent fixture (see Docs\edit-subagent-prompt-testing-plan.md)
        // Temp\AgentProbe sits at the solution root, outside every project directory, so a broken
        // edit never compiles and never breaks the build. The source of truth is the file on disk;
        // the answer text is judged separately, because a report about an edit can be wrong even
        // when the edit itself is correct.
        // ---------------------------------------------------------------------

        private const string FixtureFolder = @"Temp\AgentProbe";
        private const string ProbePath = @"Temp\AgentProbe\Probe.cs";
        private const string NotesPath = @"Temp\AgentProbe\notes.txt";

        // 19 lines. Line 7 = MaxRetries, 11 = return a + b;, 14-17 = the Describe block.
        private const string ProbeSeed = @"using System;

namespace AgentProbe
{
    internal static class Probe
    {
        public const int MaxRetries = 3;

        public static int Add(int a, int b)
        {
            return a + b;
        }

        public static string Describe(int value)
        {
            return ""value="" + value;
        }
    }
}";

        // 6 lines, no C# syntax: a plain-text target must not get C#-only syntax checks.
        private const string NotesSeed = @"line one - alpha
line two - bravo
line three - charlie
line four - delta
line five - echo
line six - foxtrot";

        private static Scenario ScEdit(string id, string prompt)
        {
            return new Scenario
            {
                Id = id,
                AgentId = Editor,
                Prompt = prompt,
                WithFixture = true
            };
        }

        /// <summary>
        /// Builds an expected file content from a seed by exact replacement. Throws when the seed
        /// does not contain the text, so a typo in an expectation fails loudly instead of silently
        /// asserting the unchanged seed.
        /// </summary>
        private static string PatchText(string text, string oldText, string newText)
        {
            // A verbatim literal that spans source lines carries the source file's own line endings
            // (CRLF in this repository), so a pattern written with '\n' would never match it. Both
            // sides are compared in LF form; CheckFixtureFile normalizes again before comparing.
            var normalized = NormalizeLineEndings(text);
            var pattern = NormalizeLineEndings(oldText);

            if (normalized.IndexOf(pattern, StringComparison.Ordinal) < 0)
            {
                // Authoring error in an expectation, not a model failure: aborting the run is
                // intentional, because falling back to the untouched seed would turn the scenario
                // into a weaker test that still reports PASS.
                throw new InvalidOperationException(
                    "Fixture expectation refers to text the seed does not contain (fix the expectation, not the seed): "
                    + oldText.Replace("\r\n", "\\r\\n").Replace("\n", "\\n"));
            }

            return normalized.Replace(pattern, NormalizeLineEndings(newText));
        }

        // ---------------------------------------------------------------------
        // Scenario list (prompts and expectations are in English)
        // ---------------------------------------------------------------------

        private static List<Scenario> BuildScenarios()
        {
            var list = new List<Scenario>();
            var guid = Guid.NewGuid().ToString("N").Substring(0, 12);
            //----A.1 code_explorer_subagent: discovery(10)------------------ -
            list.Add(Sc("A1.1", Explorer,
                "Search for the exact identifier ToolCallRecord. Report yes if the returned result includes StreamCompletionResult.cs at line 114.",
                "yes"));

            list.Add(Sc("A1.2", Explorer,
                "Search for the exact identifier GetAsync in the solution. Report yes if the returned results include GetAsync and report the returned total count.",
                "GetAsync"));

            list.Add(Sc("A1.3", Explorer,
                "Find the file named ChatLogSerializer.cs. Report yes if the returned path includes Persistence.",
                "yes"));

            list.Add(Sc("A1.4", Explorer,
                "Search for the exact XML text PackageReference Include=\"Microsoft.CodeAnalysis\". Report yes if the returned result is a PackageReference to Microsoft.CodeAnalysis.",
                "yes"));

            list.Add(Sc("A1.5", Explorer,
                "Find the file named StreamProcessor.cs. Report yes if the returned file name is StreamProcessor.cs.",
                "yes"));

            list.Add(Sc("A1.6", Explorer,
                "Search for the exact identifier ConsolidateLastExchangeAsync with project filter LMLocal. Report yes if the returned results belong to project LMLocal.",
                "yes"));

            list.Add(Sc("A1.7", Explorer,
                $"Search for the literal text bench-{guid}-{guid}. Report yes if the search tool returned zero matches.",
                "yes"));

            list.Add(Sc("A1.8", Explorer,
                "Search for the exact text using Newtonsoft. Retrieve all returned pages. Report yes if the final result contains the complete returned result list and the reported total count.",
                "yes"));

            list.Add(Sc("A1.9", Explorer,
                "Get the solution overview and report yes if returned project list doen't contain LMLocal.Web.",
                "yes"));

            list.Add(Sc("A1.10", Explorer,
                "Search for the exact identifier ToolCallRecord and retrieve all returned pages. Report yes if the returned results include StreamCompletionResult.cs at line 114.",
                "yes"));

            // ---- A.2 code_reader_subagent: deterministic reading (9) ----------
            list.Add(Sc("A2.1", Reader,
                "Read the entire file LMLocal/Infrastructure/Persistence/ChatLogSerializer.cs and return it verbatim.",
                "MaxPromptLength"));

            list.Add(ScVerbatim("A2.2", Reader,
                "Read lines 1-12 of LMLocal\\GettingStarted.txt and return them verbatim.",
                "LM Local", "Visual Studio"));

            list.Add(Sc("A2.3", Reader,
                "Find the file named exactly StreamProcessor.cs and read it (there is also a StreamProcessorFactory.cs in the same folder - do not pick the factory).",
                "ToolCallRecord", "LlmSseParser"));

            list.Add(ScVerbatim("A2.4", Reader,
                "Read these three files and return them verbatim: LMLocal\\Infrastructure\\Persistence\\ChatLogSerializer.cs, LMLocal\\Infrastructure\\Persistence\\ChatPersistenceService.cs, LMLocal\\Core\\Models\\StreamChunk.cs.",
                "MaxPromptLength", "SaveLastMessageAsync", "ChunkKind"));

            list.Add(ScVerbatim("A2.5", Reader,
                "Read these 4 files and return them verbatim: ChatLogSerializer.cs, StreamCompletionResult.cs, StreamProcessor.cs, ChatHistoryManager.cs.",
                "ChatLogSerializer", "ToolCallRecord", "ProcessStreamAsync", "ConsolidateLastExchangeAsync"));

            list.Add(Sc("A2.6", Reader,
                "Read LMLocal/Infrastructure/Persistence/NoSuchSerializer.cs and return yes if not found.",
                "yes"));

            list.Add(Sc("A2.7", Reader,
                "Read LMLocal/Infrastructure/Persistence/ChatLogSerializer.cs. If the entire file is not returned, clearly state that it is partial and identify the missing portion.",
                "MaxPromptLength"));

            var a208 = ScVerbatim("A2.8", Reader,
                "Read the file at exact path LMLocal.Tests.E2E.VSIX\\Fixtures\\reader-truth-fixture.md and return it verbatim.",
                "## 1. First Section", "## Appendix A. References", "## 2. Second Section", "Tail-Anchor-Open");
            a208.MustAppearExactlyOnce = new[] { "Seed-One-Anchor", "Ref-Anchor-Alpha", "Tail-Anchor-Open" };
            list.Add(a208);

            var a209 = Sc("A2.9", Reader,
                "Read the file at exact path LMLocal.Tests.E2E.VSIX\\Fixtures\\reader-truth-fixture.md. Verify whether these headings appear in this exact order: \"## 1. First Section\", \"## 2. Second Section\", \"## 3. Third Section\", \"## Appendix A. References\". Use the actual file contents and line positions. Do not infer or assume any heading or position that is not present in the retrieved content.",
                "## Appendix A. References");
            a209.MustContainInOrder = new[] { "## 1. First Section", "## Appendix A. References", "## 2. Second Section", "## 3. Third Section" };
            list.Add(a209);

            list.Add(Sc("A2.10", Reader,
                "Read the entire file LMLocal.Tests.E2E.VSIX\\Fixtures\\reader-large-fixture.md and return it verbatim. Continue reading until the entire file has been retrieved; do not stop while has_more_results is true.",
                "Reader-Start", "Reader-End"));

            var a211 = ScVerbatim("A2.11", Reader,
                "In LMLocal\\Infrastructure\\Security\\TestConnectionErrorClassifier.cs return VERBATIM only the single line that contains ExceptionFormatter.Format. Return no other line of the file and do not mark the file as missing.",
                "return ExceptionFormatter.Format(ex);");
            a211.MustNotContain = new[] { "Missing files" };
            list.Add(a211);

            var a212 = ScVerbatim("A2.12", Reader,
                "In LMLocal\\Application\\ModelsList\\ModelsListService.cs return VERBATIM only the two lines that contain ExceptionFormatter.Format. Return no other line of the file and do not mark the file as missing.",
                "var errorResponse = new UnifiedListModelsResponse { Error = ExceptionFormatter.Format(ex) };",
                "return new UnifiedListModelsResponse { Error = ExceptionFormatter.Format(ex) };");
            a212.MustNotContain = new[] { "Missing files" };
            list.Add(a212);

            var a213 = ScVerbatim("A2.13", Reader,
                "Read the file at exact path LMLocal.Tests.E2E.VSIX\\Fixtures\\reader-ranges-fixture.md and return ONLY lines 11-15 and lines 30-39 VERBATIM, with each line carrying its actual 1-based line number. Do not include any other line of the file, do not substitute different ranges, and do not mark the file as missing.",
                "RANGE-ANCHOR-A1-OPEN", "RANGE-ANCHOR-A1-CLOSE", "RANGE-ANCHOR-A3-CLOSE", "RANGE-ANCHOR-APP-OPEN");
            a213.MustContainInOrder = new[] { "RANGE-ANCHOR-A1-OPEN", "RANGE-ANCHOR-A1-CLOSE", "RANGE-ANCHOR-A3-CLOSE", "RANGE-ANCHOR-APP-OPEN" };
            a213.MustNotContain = new[] { "RANGE-ANCHOR-A2-OPEN", "RANGE-ANCHOR-A2-CLOSE", "RANGE-ANCHOR-A3-OPEN", "RANGE-ANCHOR-APP-CLOSE", "RANGE-ANCHOR-TAIL-OPEN", "RANGE-ANCHOR-A1-OPEN-COPY", "RANGE-ANCHOR-A1-CLOSE-COPY", "# End of fixture" };
            list.Add(a213);

            var a214 = ScVerbatim("A2.14", Reader,
                "Read the file at exact path LMLocal.Tests.E2E.VSIX\\Fixtures\\reader-symbols-fixture.md and return it verbatim, with each line carrying its 1-based line number. Preserve every character exactly — multiple spaces, tabs, single and double quotes, and every bracket. Do not normalize, drop, or rebalance any of them.",
                "SYM-SPACE-MID: two  spaces  between  words",
                "SYM-QUOTE-NESTED-SINGLE: 'outer \"inner\" value'",
                "SYM-QUOTE-AT-END: trailing quote\"",
                "SYM-PAREN-OPEN: function(",
                "SYM-PAREN-BALANCED: ((a, b), [c, d], {e: f})",
                "SYM-BRACKET-MIXED: {[(\"a\", {\"b\": [1,2]})]}",
                "SYM-EMPTY-AFTER: this line follows the empty line above",
                "SYM-END-TAIL: last line anchor");

            a214.MustNotContain = new[]
            {
                "SYM-EMPTY-UNWANTED",
                "//SECTION-SKIP",
                "///"
            };
            list.Add(a214);

            //----A.3 symbol_analyzer_subagent: symbols & references(9)------ -
            list.Add(Sc("A3.1", Analyzer,
                "Find the C# symbol ToolCallRecord and report its declaration file and line.",
                "StreamCompletionResult.cs", "114"));

            list.Add(Sc("A3.2", Analyzer,
                "Find all references to ChatLogSerializer. Report file:line coordinates.",
                "ChatLogSerializer", "ChatPersistenceService.cs"));

            list.Add(Sc("A3.3", Analyzer,
                "Inspect the type StreamCompletionResult and list its public members with file:line locations.",
                "ContentResponse", "ToolCalls"));

            list.Add(Sc("A3.4", Analyzer,
                "Find the JavaScript symbol lmInit and report its file and line. Use the semantic JavaScript tool.",
                "app.js"));

            list.Add(NotFoundSc("A3.5", Analyzer,
                "Find the symbol GeminiThoughtSignaturePolicyEnforcer.",
                "GeminiThoughtSignaturePolicyEnforcer"));

            list.Add(Sc("A3.6", Analyzer,
                "Find the symbol ToolCallRecord, then read its declaration context.",
                "ToolCallRecord", "StreamCompletionResult.cs"));

            var a37 = Sc("A3.7", Analyzer,
                "Find all references to the ToolCalls property of the type ChatMessage (declared in LMLocal/Core/Models/ChatMessage.cs). Ignore the same-named ToolCalls property on other types such as Message and StreamCompletionResult. Retrieve ALL pages of references and state the total reference count.",
                "ToolCalls", "ChatMessage.cs");
            a37.MustContainAny = new[] { new[] { "ApiRequestBuilder.cs", "ApiRequestBuilder" } };
            list.Add(a37);

            var a38 = Sc("A3.8", Analyzer,
                "Inspect ConsolidateLastExchangeAsync and determine whether ToolCalls is copied by reference or reconstructed. Read the actual source before answering.",
                "ConsolidateLastExchangeAsync", "ToolCalls");
            a38.MustContainAny = new[] { new[] { "reference", "same instance", "reused", "not reconstructed" } };
            list.Add(a38);

            list.Add(Sc("A3.9", Analyzer,
                "Find the C# method AddAssistantMessage in LMLocal/Application/Chat/ChatHistoryManager.cs. It has an interface declaration and two concrete overloads. Select the concrete implementation overload that accepts IReadOnlyList<ToolCallRecord> and return its declaration line together with its full implementation body.",
                "AddAssistantMessage", "toolCallObjects", "normalizedArguments"));

            // ---- A.4 reviewer_subagent: structured status review (2) ----
            var a41 = Sc("A4.1", Reviewer,
                "Review the following completed deliverable and produce your structured review ending with an exact final 'Status: ...' line. The change is a small refactoring of ChatHistoryManager: the ToolCalls copying logic was extracted into a private helper. The build passes, all existing unit tests for the chat service pass, and a new unit test covers the copied-by-reference behavior. The user requested exactly this refactoring and it is complete; no further phases are planned. Review it adversarially.",
                "Status:");
            a41.MustContainAny = new[] { new[] { "COMPLETE", "PARTIAL" } };
            a41.MustNotContain = new[] { "Status: REJECTED" };
            list.Add(a41);

            var a42 = Sc("A4.2", Reviewer,
                "Review the following implementation plan for executability and completeness. Produce your structured review ending with an exact final 'Status: ...' line. PLAN: Phase 1 adds a new ChatProcessor class that depends on IMessageBus, and wires it into ChatHistoryManager. Phase 2 (explicitly planned for a later release, NOT part of this deliverable) registers IMessageBus in DI and implements its only implementation. Phase 1 must be delivered now, must compile, and must run in production. Determine whether Phase 1 is executable as stated.",
                "Status:");
            a42.MustContainAny = new[] { new[] { "PARTIAL", "REJECTED" } };
            a42.MustNotContain = new[] { "Status: COMPLETE" };
            list.Add(a42);

            // ---- A.5 edit_subagent: mechanical patching on a real file (10) ----
            // Fixture: Docs\edit-subagent-prompt-testing-plan.md. Every scenario declares the exact
            // expected file content, because the report alone is not evidence. Prompts quote code
            // with single quotes on purpose: no doubled quotes means no way to break the literal.

            // A5.1 = plan A, control scenario. Tightening the prompt (gates, bans) must never break
            // normal work, so this one has to stay green on every run.
            var a51 = ScEdit("A5.1",
                @"Apply exactly two edits to the file Temp\AgentProbe\Probe.cs. Do not change anything else.

EDIT 1 - replace one line (approximate line 11):
  file_path: Temp\AgentProbe\Probe.cs
  start_line: 11
  old_lines:            return a + b;
  new_lines:            return checked(a + b);

EDIT 2 - replace one line (approximate line 7):
  file_path: Temp\AgentProbe\Probe.cs
  start_line: 7
  old_lines:        public const int MaxRetries = 3;
  new_lines:        public const int MaxRetries = 5;

Apply the edits bottom-up (line 11 first, then line 7).");
            a51.ExpectedProbe = PatchText(
                PatchText(ProbeSeed, "return a + b;", "return checked(a + b);"),
                "MaxRetries = 3;",
                "MaxRetries = 5;");
            a51.MustContain = new[] { "APPLIED" };
            // Baseline for the ref-marker idea: printing the new line, the file body or a diff is
            // what host-side expansion would replace, so it counts as PARTIAL here.
            a51.SoftMustNotContain = new[] { "return checked(a + b);", "namespace AgentProbe", "internal static class Probe" };
            list.Add(a51);

            // A5.2 = plan B, the main invention test. Any helpful edit is a hard FAIL even if the
            // resulting code looks reasonable: unrequested logic is exactly what ships bugs.
            var a52 = ScEdit("A5.2",
                @"In the file Temp\AgentProbe\Probe.cs, make the Add method safer by adding overflow protection. Choose a reasonable implementation yourself and apply it.
Report the file path and the line numbers modified.");
            a52.CheckRefusal = false;
            a52.MustContainAny = new[] { new[] { "NEEDS_EXACT_CODE", "old_lines" } };
            list.Add(a52);

            // A5.3 = plan C. A mismatched old_lines means the caller aimed at the wrong line, so the
            // only safe outcome is a refusal; guessing silently moves the change elsewhere.
            var a53 = ScEdit("A5.3",
                @"EDIT 1 - replace one line (approximate line 2):
  file_path: Temp\AgentProbe\notes.txt
  start_line: 2
  old_lines: line two - bravoZ
  new_lines: line two - BRAVO");
            a53.MustContain = new[] { "FAILED" };
            // Paraphrasing the tool error instead of quoting error_message was the recorded R3/R4
            // defect: report quality only, so it is soft.
            a53.SoftMustContainAny = new[] { new[] { "Old content not found", "error_message" } };
            list.Add(a53);

            // A5.4 = plan D. Helpfully creating the file via insert_file_lines is a FAIL: the fixture
            // inventory reports any file that appeared during the run.
            var a54 = ScEdit("A5.4",
                @"EDIT 1 - replace one line (approximate line 7):
  file_path: Temp\AgentProbe\Missing.cs
  start_line: 7
  old_lines:        public const int MaxRetries = 3;
  new_lines:        public const int MaxRetries = 5;");
            a54.CheckRefusal = false;
            // The prompt itself names a path that must not exist.
            a54.SkipInventedPathCheck = true;
            a54.MustContain = new[] { "Missing.cs" };
            a54.MustContainAny = new[] { new[] { "FAILED", "NOT FOUND" } };
            list.Add(a54);

            // A5.5 = plan E. start_line 13 points at the blank line before the block; the tool
            // searches, so the edit must still land and the reported range must come from the tool
            // result, not from the task text.
            var a55 = ScEdit("A5.5",
                @"EDIT 1 - replace a 4-line block (approximate start line 13):
  file_path: Temp\AgentProbe\Probe.cs
  start_line: 13
  old_lines:
        public static string Describe(int value)
        {
            return ""value="" + value;
        }
  new_lines:
        public static string Describe(int value)
        {
            return ""value="" + value + ""|"" + MaxRetries;
        }");
            a55.ExpectedProbe = PatchText(
                ProbeSeed,
                @"return ""value="" + value;",
                @"return ""value="" + value + ""|"" + MaxRetries;");
            a55.MustContain = new[] { "APPLIED" };
            a55.SoftMustContainAny = new[] { new[] { "14-17", "14 - 17", "lines 14" } };
            list.Add(a55);

            // A5.6 = plan F, with one doc fix: 'return a + b;' is line 11 (line 16 is the Describe
            // return), so the third edit targets 11. Three edits across two files must not trip the
            // 'at most two files' limit.
            var a56 = ScEdit("A5.6",
                @"Apply these three edits:
1. Temp\AgentProbe\Probe.cs line 7: replace 'public const int MaxRetries = 3;' with 'public const int MaxRetries = 5;'
2. Temp\AgentProbe\notes.txt line 2: replace 'line two - bravo' with 'line two - BRAVO'
3. Temp\AgentProbe\Probe.cs line 11: replace 'return a + b;' with 'return a + b + 0;'");
            a56.ExpectedProbe = PatchText(
                PatchText(ProbeSeed, "return a + b;", "return a + b + 0;"),
                "MaxRetries = 3;",
                "MaxRetries = 5;");
            a56.ExpectedNotes = PatchText(NotesSeed, "line two - bravo", "line two - BRAVO");
            a56.MustContain = new[] { "APPLIED" };
            a56.SoftMustNotContain = new[] { "NEEDS_SPLIT" };
            list.Add(a56);

            // A5.7 = plan G, the most dangerous shape: one precise edit plus one vague request. Zero
            // edits is the only correct answer; a parameter rename without a body edit produces code
            // that does not compile and that syntax_errors cannot see.
            var a57 = ScEdit("A5.7",
                @"EDIT 1 - replace one line (approximate line 7):
  file_path: Temp\AgentProbe\Probe.cs
  start_line: 7
  old_lines:        public const int MaxRetries = 3;
  new_lines:        public const int MaxRetries = 7;

EDIT 2 - make the Add method clearer by using a better variable name for the parameters.
Choose appropriate names yourself.");
            a57.CheckRefusal = false;
            a57.MustContainAny = new[] { new[] { "NEEDS_EXACT_CODE", "old_lines" } };
            list.Add(a57);

            // A5.8 = plan H. start_line 20 does not exist (the file has 19 lines), so a report that
            // says 'lines 20-20' is copying the task instead of the tool result.
            var a58 = ScEdit("A5.8",
                @"EDIT 1 - replace one line (approximate line 20):
  file_path: Temp\AgentProbe\Probe.cs
  start_line: 20
  old_lines:            return a + b;
  new_lines:            return a + b + 1;");
            a58.ExpectedProbe = PatchText(ProbeSeed, "return a + b;", "return a + b + 1;");
            a58.MustContain = new[] { "APPLIED" };
            a58.SoftMustContainAny = new[] { new[] { "11-11", "11 - 11", "lines 11" } };
            a58.SoftMustNotContain = new[] { "20-20", "20 - 20", "lines 20" };
            list.Add(a58);

            // A5.9 = plan I.1. position 3 is wrong on purpose; expected_line is the real anchor.
            var a59 = ScEdit("A5.9",
                @"EDIT 1 - insert one new line (approximate position 3):
  file_path: Temp\AgentProbe\notes.txt
  position: 3
  expected_line: line five - echo
  new_lines: line five and a half - hotel");
            a59.ExpectedNotes = PatchText(
                NotesSeed,
                "line five - echo",
                "line five - echo\nline five and a half - hotel");
            a59.MustContain = new[] { "APPLIED" };
            list.Add(a59);

            // A5.10 = plan I.2. Insert and delete in one file, applied bottom-up: an insert above the
            // deleted line must not shift it out from under the delete.
            var a510 = ScEdit("A5.10",
                @"EDIT 1 - insert one new line (approximate position 6):
  file_path: Temp\AgentProbe\notes.txt
  position: 6
  expected_line: line five - echo
  new_lines: line five and a half - hotel

EDIT 2 - delete one line (approximate line 4):
  file_path: Temp\AgentProbe\notes.txt
  start_line: 4
  old_lines: line four - delta
  new_lines:");
            a510.ExpectedNotes = PatchText(
                PatchText(NotesSeed, "line five - echo", "line five - echo\nline five and a half - hotel"),
                "line four - delta\n",
                string.Empty);
            a510.MustContain = new[] { "APPLIED" };
            list.Add(a510);

            // A5.11 - GATE 1 is the only gate that was never exercised. Three targeted files must
            // stop the run before any tool call, so nothing on disk may change.
            var a511 = ScEdit("A5.11",
                @"Apply these three edits:
1. Temp\AgentProbe\Probe.cs line 7: replace 'public const int MaxRetries = 3;' with 'public const int MaxRetries = 5;'
2. Temp\AgentProbe\notes.txt line 2: replace 'line two - bravo' with 'line two - BRAVO'
3. Temp\AgentProbe\Other.cs line 4: replace 'alpha' with 'beta'");
            a511.CheckRefusal = false;
            a511.MustContain = new[] { "NEEDS_SPLIT" };
            a511.SoftMustNotContain = new[] { "APPLIED" };
            list.Add(a511);

            // A5.12 - guards the 'only edited files count' rule: a file named in prose as something
            // NOT to touch must not turn a single-file task into a false NEEDS_SPLIT.
            var a512 = ScEdit("A5.12",
                @"Apply this edit to Temp\AgentProbe\Probe.cs and do not touch any other file (for example LMLocal\Core\Models\StreamChunk.cs must stay unchanged):
EDIT 1 - replace one line (approximate line 7):
  file_path: Temp\AgentProbe\Probe.cs
  start_line: 7
  old_lines:        public const int MaxRetries = 3;
  new_lines:        public const int MaxRetries = 9;");
            a512.ExpectedProbe = PatchText(ProbeSeed, "MaxRetries = 3;", "MaxRetries = 9;");
            a512.MustContain = new[] { "APPLIED" };
            a512.SoftMustNotContain = new[] { "NEEDS_SPLIT" };
            list.Add(a512);

            return list;
        }

        // ---------------------------------------------------------------------
        // Main run
        // ---------------------------------------------------------------------
        public static IEnumerable<object[]> GetIterations()
        {
            const int from = 1;
            const int to = 1;
            return Enumerable.Range(from, to - from + 1).Select(i => new object[] { i }).ToList();
        }

        [TestMethod]
        [DynamicData(nameof(GetIterations))]
        [Ignore("Disable to run benchmark")]
        public async Task Run_Full_Benchmark(int iteration)
        {
            // Requires a running LM Studio + an Experimental VS instance; never auto-run.
            if (iteration != 1)
                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);

            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(180)))
            {
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    IpcClient client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(2), cts.Token);
                    try
                    {
                        // 1) Readiness gate: solution must be open (subagents return "No solution is open" otherwise). Existing IPC command.
                        var solutionPath = GetSolutionPath();
                        var resp = await client.SendCommandAsync("OpenSolution|" + solutionPath, cts.Token);
                        Assert.AreEqual("OK", resp, "OpenSolution IPC command failed");

                        await WaitForSolutionLoadedAsync(client, cts.Token);

                        var root = Path.GetDirectoryName(Path.GetFullPath(solutionPath));
                        var results = new List<BenchmarkResult>();

                        // Start from a clean fixture: a file left behind by an aborted run would enter
                        // the "before" inventory and mask a file that the agent creates (scenario A5.4).
                        TryDeleteFixture(root);

                        // 2) Run every scenario sequentially on the same VS instance.
                        foreach (var scenario in BuildScenarios())
                        {
                            if (cts.IsCancellationRequested) break;

                            BenchmarkResult result;
                            try
                            {
                                result = await RunScenarioAsync(client, scenario, root, cts.Token);
                            }
                            catch (TimeoutException tex)
                            {
                                // Read timeout: the client is broken and cannot be reused.
                                result = TimeoutResult(scenario, tex);
                                client.Dispose();
                                TestContext.WriteLine("[" + scenario.Id + "] read timeout, reconnecting...");
                                client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(2), cts.Token);
                            }

                            results.Add(result);
                            TestContext.WriteLine("[" + result.Id + "] " + result.Verdict
                                + " (" + Math.Round(result.DurationMs / 1000.0, 1) + "s) "
                                + string.Join("; ", result.Violations));

                            // Let LM Studio cool down between runs.
                            await Task.Delay(1000, cts.Token);
                        }

                        SaveReport(results, root);
                        TryDeleteFixture(root);

                        var failed = results.Where(r => r.Verdict == "FAIL").ToList();
                        if (failed.Count > 0)
                        {
                            // Violations are inlined: the failure message is what a developer actually
                            // reads, and the JSON report at the solution root is not always at hand.
                            Assert.Fail("Critical FAILs: "
                                + string.Join(" | ", failed.Select(r => r.Id + " -> " + string.Join("; ", r.Violations)))
                                + ". Full report saved next to the .sln.");
                        }
                    }
                    finally
                    {
                        // A leftover fixture is harmless (it is outside every project), but it would
                        // pollute the next run, so clean it even when the scenario loop throws.
                        TryDeleteFixture(Path.GetDirectoryName(Path.GetFullPath(GetSolutionPath())));
                        client.Dispose();
                    }
                }
                finally
                {
                    TryKill(vs);
                }
            }
        }

        // ---------------------------------------------------------------------
        // Per-scenario runner + evaluation
        // ---------------------------------------------------------------------

        private static async Task<BenchmarkResult> RunScenarioAsync(
            IpcClient client,
            Scenario scenario,
            string solutionRoot,
            CancellationToken ct)
        {
            // Reset the edit fixture before the run, so the on-disk result belongs to this scenario only.
            PrepareFixture(solutionRoot, scenario);

            var sw = Stopwatch.StartNew();

            var escapedPrompt = Uri.EscapeDataString(scenario.Prompt);
            var command = "RunSubAgent|" + scenario.AgentId + "|" + escapedPrompt;

            var json = await client.SendCommandAsync(command, scenario.ReadTimeout, ct);
            sw.Stop();

            RunResponseDto response = null;
            string parseError = null;
            try
            {
                response = JsonConvert.DeserializeObject<RunResponseDto>(json);
            }
            catch (Exception ex)
            {
                parseError = ex.Message;
            }

            return Evaluate(scenario, response, parseError, sw.Elapsed, solutionRoot);
        }

        private static BenchmarkResult Evaluate(
            Scenario s,
            RunResponseDto r,
            string parseError,
            TimeSpan duration,
            string solutionRoot)
        {
            var result = new BenchmarkResult
            {
                Id = s.Id,
                AgentId = s.AgentId,
                Prompt = s.Prompt,
                DurationMs = duration.TotalMilliseconds
            };

            var violations = new List<Violation>();
            string content = r != null ? r.Content ?? string.Empty : string.Empty;

            if (parseError != null)
            {
                violations.Add(new Violation("parse_error: " + parseError, true));
            }

            if (r == null)
            {
                result.Verdict = "FAIL";
                result.Violations.Add("no_response");
                return result;
            }

            result.ToolsUsed = r.ToolsUsed ?? new List<string>();
            result.Rounds = r.Rounds;
            result.Content = Truncate(content);
            result.PromptTokens = r.PromptTokens;
            result.CompletionTokens = r.CompletionTokens;
            result.TotalTokens = r.TotalTokens;
            result.TokensPerSecond = r.TokensPerSecond;

            if (!r.Success)
            {
                violations.Add(new Violation("run_failed: " + (r.Error ?? "no error message"), true));
            }

            // Soft diagnostics (только если сценарий это разрешает).
            // Refusal pattern is intentionally narrow: agents (e.g. the reviewer) legitimately
            // write "cannot be verified/executed" without refusing. Only first-person refusals
            // of the task itself count.
            if (s.CheckRefusal
                && Regex.IsMatch(content, @"(?i)\b(i ('?m| am) not (able|allowed)|i can'?t|i cannot|i won'?t|i don'?t know|sorry, (but )?i can't|i refuse)\b"))
            {
                violations.Add(new Violation("refusal", false));
            }
            if (!r.Success && r.Rounds >= 10)
            {
                violations.Add(new Violation("max_rounds_reached", false));
            }

            // Expected "NOT FOUND" honesty marker.
            bool notFoundMarker = false;
            if (s.ExpectNotFound)
            {
                notFoundMarker = Regex.IsMatch(content, @"(?i)(\bnot_found\b|not found|does not exist|no (matches|results|references|instances|project|files?|explicit|such|direct|zero)|couldn't find|could not find|not exist|none of|i found no|found no|returned no|zero (matches|results)|did not find|no .* found)");
                if (!notFoundMarker)
                {
                    violations.Add(new Violation("expected_not_found_marker_missing", true));
                }
            }

            // Required evidence in the answer.
            foreach (var ev in s.MustContain ?? new string[0])
            {
                if (notFoundMarker)
                {
                    break;
                }
                if (content.IndexOf(ev, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    violations.Add(new Violation("missing_evidence: " + ev, true));
                }
            }

            // Alternative evidence groups (OR-groups). 
            if (!notFoundMarker)
            {
                foreach (var group in s.MustContainAny ?? new string[0][])
                {
                    if (group == null || group.Length == 0)
                    {
                        continue;
                    }

                    bool any = group.Any(alt => content.IndexOf(alt, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!any)
                    {
                        violations.Add(new Violation("missing_evidence(any of): " + string.Join(" | ", group), true));
                    }
                }
            }

            // Ordered evidence: all markers must be present and appear in the given relative order (checked by first occurrence). 
            if (!notFoundMarker && s.MustContainInOrder != null && s.MustContainInOrder.Length > 0)
            {
                int prevIdx = -1;
                for (int i = 0; i < s.MustContainInOrder.Length; i++)
                {
                    var marker = s.MustContainInOrder[i];
                    var idx = content.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0)
                    {
                        violations.Add(new Violation("missing_evidence(ordered): " + marker, true));
                        break;
                    }

                    if (i > 0 && idx <= prevIdx)
                    {
                        violations.Add(new Violation(
                            "wrong_order: '" + s.MustContainInOrder[i] + "' appears before/at '" + s.MustContainInOrder[i - 1] + "'", true));
                        break;
                    }

                    prevIdx = idx;
                }
            }

            // Hard negative evidence: each marker MUST NOT appear anywhere in the answer.
            if (!notFoundMarker && s.MustNotContain != null)
            {
                foreach (var banned in s.MustNotContain)
                {
                    if (content.IndexOf(banned, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        violations.Add(new Violation("forbidden_evidence: " + banned, true));
                    }
                }
            }

            // Soft evidence: report-quality checks. A hit yields PARTIAL, never FAIL, so a correct
            // edit on disk is not masked by a cosmetic reporting defect.
            if (!notFoundMarker)
            {
                foreach (var ev in s.SoftMustContain ?? new string[0])
                {
                    if (content.IndexOf(ev, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        violations.Add(new Violation("soft_missing_evidence: " + ev, false));
                    }
                }

                foreach (var group in s.SoftMustContainAny ?? new string[0][])
                {
                    if (group == null || group.Length == 0)
                    {
                        continue;
                    }

                    if (!group.Any(alt => content.IndexOf(alt, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        violations.Add(new Violation("soft_missing_evidence(any of): " + string.Join(" | ", group), false));
                    }
                }

                foreach (var banned in s.SoftMustNotContain ?? new string[0])
                {
                    if (content.IndexOf(banned, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        violations.Add(new Violation("soft_forbidden_evidence: " + banned, false));
                    }
                }
            }

            // Exactly-once evidence: each marker must occur exactly once. 0 = missing,
            // 2+ = the same content arrived twice (printed in the answer text AND expanded
            // from a ref marker by the runner, or via overlapping/multiple ref markers).
            if (!notFoundMarker && s.MustAppearExactlyOnce != null)
            {
                foreach (var marker in s.MustAppearExactlyOnce)
                {
                    if (string.IsNullOrEmpty(marker))
                    {
                        continue;
                    }

                    var count = CountOccurrences(content, marker);
                    if (count == 0)
                    {
                        violations.Add(new Violation("missing_evidence(exactly_once): " + marker, true));
                    }
                    else if (count > 1)
                    {
                        violations.Add(new Violation("duplicated_evidence: '" + marker + "' appears " + count + " times", true));
                    }
                }
            }

            // Invented-path cross-check (only for non-mutating scenarios).
            if (notFoundMarker)
            {
                // Honest NOT_FOUND — no invented-path check needed: the non-existing path is exactly what we expected to see.
            }
            else if (!s.SkipInventedPathCheck)
            {
                foreach (var p in ExtractPaths(content))
                {
                    // Skip paths that came from the prompt itself (e.g. NOT FOUND scenarios).
                    var inPrompt = s.Prompt.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (inPrompt)
                    {
                        continue;
                    }

                    // If a file with the same name exists anywhere in the solution, the
                    // agent likely quoted a truncated/relative path (e.g.
                    // "ChatSessionStream\StreamProcessor.cs" for
                    // "LMLocal\Application\ChatSessionStream\StreamProcessor.cs") — do not
                    // flag it as invented. ResolveFullPath already handles absolute paths.
                    var fileName = Path.GetFileName(p.TrimEnd('\\', '/'));
                    if (!string.IsNullOrEmpty(fileName) && FileExistsByName(solutionRoot, fileName))
                    {
                        continue;
                    }

                    // Regex false-positive guard: "Foo.cs" extracted from inside
                    // "Foo.csproj" (no real Foo.cs exists, but Foo.csproj does).
                    if (p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                        && HasRealFileWithDifferentExtension(solutionRoot, p, ".csproj"))
                    {
                        continue;
                    }

                    var full = ResolveFullPath(solutionRoot, p);
                    if (full != null && !File.Exists(full) && !Directory.Exists(full))
                    {
                        violations.Add(new Violation("invented_path: " + p, true));
                    }
                }
            }

            // Fixture (on-disk) verification: the source of truth for edit scenarios.
            EvaluateFixture(solutionRoot, s, violations);

            result.Verdict = violations.Count == 0 ? "PASS"
                : violations.Any(v => v.Hard) ? "FAIL"
                : "PARTIAL";
            result.Violations.AddRange(violations.Select(v => v.Text));

            return result;
        }

        private static BenchmarkResult TimeoutResult(Scenario s, TimeoutException tex)
        {
            var r = new BenchmarkResult
            {
                Id = s.Id,
                AgentId = s.AgentId,
                Prompt = s.Prompt,
                Verdict = "FAIL"
            };
            r.Violations.Add("client_read_timeout: " + tex.Message);
            return r;
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        private static async Task WaitForSolutionLoadedAsync(IpcClient client, CancellationToken ct)
        {
            for (int i = 0; i < 90; i++)
            {
                var resp = await client.SendCommandAsync("RunTool|GetSolutionOverview", TimeSpan.FromSeconds(20), ct);
                if (IsSolutionLoaded(resp))
                {
                    return;
                }
                await Task.Delay(2000, ct);
            }
            throw new TimeoutException("Solution did not load within 180s.");
        }

        private static bool IsSolutionLoaded(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || !json.StartsWith("{"))
            {
                return false;
            }

            try
            {
                var obj = JObject.Parse(json);
                return obj["success"]?.Value<bool>() == true
                    && !string.IsNullOrWhiteSpace((string)obj["solution_name"]);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string GetSolutionPath()
        {
            var solutionPath = Path.GetFullPath(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "LMLocal.sln"));
            if (!File.Exists(solutionPath))
            {
                throw new InvalidOperationException("Solution not found at '" + solutionPath + "'");
            }
            return solutionPath;
        }

        private static string ResolveFullPath(string solutionRoot, string candidate)
        {
            var trimmed = candidate.Trim().TrimStart('.', '\\', '/');
            if (trimmed.Length == 0) return null;

            // Absolute Windows path (e.g. C:\...).
            if (trimmed.Length >= 2 && trimmed[1] == ':')
            {
                return Path.GetFullPath(trimmed);
            }

            if (string.IsNullOrEmpty(solutionRoot)) return null;

            var normalized = trimmed.Replace('/', '\\');
            try
            {
                return Path.GetFullPath(Path.Combine(solutionRoot, normalized));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Returns true if a file with the given name exists anywhere under solutionRoot.
        /// </summary>
        private static bool FileExistsByName(string solutionRoot, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(solutionRoot))
                return false;
            try
            {
                return Directory.EnumerateFiles(solutionRoot, fileName, SearchOption.AllDirectories).Any();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns true if a file with the same directory + base name exists in the
        /// solution but with a different extension (e.g. "LMLocal\\LMLocal.cs" -> the real "LMLocal\\LMLocal.csproj"). 
        /// </summary>
        private static bool HasRealFileWithDifferentExtension(string solutionRoot, string path, string realExtension)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(solutionRoot))
                return false;

            var dir = Path.GetDirectoryName(path);
            var stem = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(stem)) return false;

            var candidate = Path.GetFileName(path) + realExtension; // "LMLocal.cs" + ".csproj"
            if (string.IsNullOrWhiteSpace(dir))
            {
                return File.Exists(Path.Combine(solutionRoot, candidate));
            }

            var resolvedDir = ResolveFullPath(solutionRoot, dir);
            return resolvedDir != null && File.Exists(Path.Combine(resolvedDir, candidate));
        }

        // Only paths with a directory separator are cross-checked.
        private static List<string> ExtractPaths(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return new List<string>();

            // Negative lookahead (?![.\w]) ensures ".cs" is NOT followed by another
            // word character or dot — this prevents matching ".cs" inside ".csproj"
            // (which would produce a .cs path that does not exist on disk).
            // '.' in [\w\\/.]+ matches project names like LMLocal.Tests.Unit.
            var patterns = new[]
            {
                @"[\w\\/.]+\.cs(?![.\w])",
                @"[\w\\/.]+\.csproj(?![.\w])",
                @"[\w\\/.]+\.json(?![.\w])"
            };

            var deny = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Newtonsoft.Json"
            };

            return patterns
                .SelectMany(p => Regex.Matches(content, p, RegexOptions.None).Cast<Match>())
                .Select(m => m.Value)
                .Where(v =>
                    !deny.Contains(v) &&
                    (v.IndexOf('\\') >= 0 || v.IndexOf('/') >= 0))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string Truncate(string content)
        {
            const int max = 6000;
            if (content == null) return null;
            return content.Length <= max ? content : content.Substring(0, max) + "\n...[truncated]";
        }

        /// <summary>
        /// Counts non-overlapping occurrences of <paramref name="value"/> in <paramref name="text"/>
        /// using ordinal, case-insensitive matching.
        /// </summary>
        private static int CountOccurrences(string text, string value)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(text)) return 0;

            int count = 0;
            int idx = 0;
            while ((idx = text.IndexOf(value, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                idx += value.Length;
            }
            return count;
        }

        // ---------------------------------------------------------------------
        // edit_subagent fixture helpers
        // ---------------------------------------------------------------------

        private static void PrepareFixture(string solutionRoot, Scenario s)
        {
            if (!s.WithFixture || string.IsNullOrEmpty(solutionRoot))
            {
                return;
            }

            var folder = Path.Combine(solutionRoot, FixtureFolder);
            Directory.CreateDirectory(folder);

            // Overwrite instead of deleting the folder: a file left open in the VS editor must
            // not abort the whole run.
            File.WriteAllText(Path.Combine(solutionRoot, ProbePath), ProbeSeed);
            File.WriteAllText(Path.Combine(solutionRoot, NotesPath), NotesSeed);

            s.FixtureFilesBefore = ListFixtureFiles(folder);
        }

        private static HashSet<string> ListFixtureFiles(string folder)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    files.Add(Path.GetFileName(file));
                }
            }
            catch
            {
                // Best effort: an unreadable folder simply yields an empty inventory.
            }

            return files;
        }

        private static void EvaluateFixture(string solutionRoot, Scenario s, List<Violation> violations)
        {
            if (!s.WithFixture || string.IsNullOrEmpty(solutionRoot))
            {
                return;
            }

            CheckFixtureFile(solutionRoot, ProbePath, s.ExpectedProbe ?? ProbeSeed, violations);
            CheckFixtureFile(solutionRoot, NotesPath, s.ExpectedNotes ?? NotesSeed, violations);

            var folder = Path.Combine(solutionRoot, FixtureFolder);
            var before = s.FixtureFilesBefore ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in ListFixtureFiles(folder))
            {
                if (name.Equals("Probe.cs", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("notes.txt", StringComparison.OrdinalIgnoreCase)
                    || before.Contains(name))
                {
                    continue;
                }

                violations.Add(new Violation("unexpected_file_created: " + name, true));
            }
        }

        private static void CheckFixtureFile(string solutionRoot, string relativePath, string expected, List<Violation> violations)
        {
            var full = Path.Combine(solutionRoot, relativePath);
            if (!File.Exists(full))
            {
                violations.Add(new Violation("fixture_missing: " + relativePath, true));
                return;
            }

            var actualText = NormalizeNewlines(File.ReadAllText(full));
            var expectedText = NormalizeNewlines(expected);
            if (actualText == expectedText)
            {
                return;
            }

            violations.Add(new Violation("fixture_mismatch: " + relativePath + " - " + FirstDifference(expectedText, actualText), true));
        }

        /// <summary>
        /// CRLF/LF and a trailing end-of-file newline are not part of the expectation: the read tool
        /// cannot distinguish "file ends with \n\n" from "the final \n counted as an empty line"
        /// (see Docs\edit-subagent-prompt-testing-plan.md, item 2 of applicability limits).
        /// </summary>
        private static string NormalizeNewlines(string text)
        {
            return NormalizeLineEndings(text).TrimEnd('\n');
        }

        /// <summary>
        /// CRLF and lone CR become LF. The trailing newline is kept, because callers use this on
        /// exact text rather than on a whole-file comparison.
        /// </summary>
        private static string NormalizeLineEndings(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static string FirstDifference(string expected, string actual)
        {
            var expectedLines = expected.Split('\n');
            var actualLines = actual.Split('\n');
            var max = Math.Max(expectedLines.Length, actualLines.Length);
            for (var i = 0; i < max; i++)
            {
                var expectedLine = i < expectedLines.Length ? expectedLines[i] : "<no such line>";
                var actualLine = i < actualLines.Length ? actualLines[i] : "<no such line>";
                if (!string.Equals(expectedLine, actualLine, StringComparison.Ordinal))
                {
                    return "line " + (i + 1) + " expected '" + expectedLine.Trim() + "' but found '"
                        + actualLine.Trim() + "' (expected " + expectedLines.Length
                        + " lines, actual " + actualLines.Length + ")";
                }
            }

            return "content differs only in end-of-file whitespace";
        }

        private static void TryDeleteFixture(string solutionRoot)
        {
            if (string.IsNullOrEmpty(solutionRoot))
            {
                return;
            }

            try
            {
                var folder = Path.Combine(solutionRoot, FixtureFolder);
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
            catch
            {
                // Best effort: the folder is outside every project, a leftover cannot break the build.
            }
        }

        // ---------------------------------------------------------------------
        // Fixture helper self-check (no model and no VS instance required)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Guards the fixture helpers that build every A.5 expectation. The seeds are verbatim
        /// literals, so their line endings come from the checkout (CRLF in this repository) while
        /// the patterns are written with '\n'. This test runs in milliseconds and fails first if
        /// that normalization ever regresses, before any run that needs LM Studio.
        /// </summary>
        [TestMethod, TestCategory("SubAgentBenchmark")]
        public void FixtureExpectations_AreLineEndingAgnostic()
        {
            // A CRLF seed matches an LF pattern, and the replacement is normalized too.
            Assert.AreEqual("a\nX\nc", PatchText("a\r\nb\r\nc", "b\n", "X\n"));
            Assert.AreEqual("a\nX\nc", PatchText("a\nb\nc", "b\r\n", "X\n"));
            Assert.AreEqual("a\nX\nY\nc", PatchText("a\r\nb\r\nc", "b\n", "X\r\nY\n"));

            var seed = "one\r\ntwo\r\nthree\r\nfour\r\n";
            Assert.AreEqual("one\ntwo\nfour\n", PatchText(seed, "three\n", string.Empty));

            // The real A5.10 expectation: insert after line 5, then delete a whole line above it.
            var notes = PatchText(
                PatchText("l1\r\nl2\r\nl3\r\nl4\r\nl5\r\nl6\r\n", "l5", "l5\r\nl5a"),
                "l4\n",
                string.Empty);
            Assert.AreEqual("l1\nl2\nl3\nl5\nl5a\nl6\n", notes);

            Assert.AreEqual("x\ny", NormalizeNewlines("x\r\ny\r\n\r\n"));
            Assert.AreEqual("x\ny\n", NormalizeLineEndings("x\r\ny\r\n"));

            // A pattern that is really absent must still abort: a silent fallback to the seed would
            // turn "this scenario must change the file" into "this scenario must leave it alone".
            var thrown = false;
            try
            {
                PatchText("one\r\ntwo", "missing\n", string.Empty);
            }
            catch (InvalidOperationException)
            {
                thrown = true;
            }

            Assert.IsTrue(thrown, "PatchText must throw when the pattern is absent from the seed");
        }

        private static void SaveReport(List<BenchmarkResult> results, string solutionRoot)
        {
            var fileName = "benchmark-report-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".json";
            var path = Path.Combine(solutionRoot ?? ".", fileName);

            var summary = new JObject
            {
                ["total"] = results.Count,
                ["pass"] = results.Count(r => r.Verdict == "PASS"),
                ["partial"] = results.Count(r => r.Verdict == "PARTIAL"),
                ["fail"] = results.Count(r => r.Verdict == "FAIL"),
                ["totalTokensSum"] = results.Sum(r => r.TotalTokens ?? 0),
                ["promptTokensSum"] = results.Sum(r => r.PromptTokens ?? 0),
                ["completionTokensSum"] = results.Sum(r => r.CompletionTokens ?? 0)
            };

            var items = new JArray();
            foreach (var r in results)
            {
                items.Add(new JObject
                {
                    ["id"] = r.Id,
                    ["agent"] = r.AgentId,
                    ["prompt"] = r.Prompt,
                    ["verdict"] = r.Verdict,
                    ["content"] = r.Content,
                    ["toolsUsed"] = new JArray(r.ToolsUsed ?? new List<string>()),
                    ["rounds"] = r.Rounds,
                    ["durationMs"] = r.DurationMs,
                    ["promptTokens"] = r.PromptTokens,
                    ["completionTokens"] = r.CompletionTokens,
                    ["totalTokens"] = r.TotalTokens,
                    ["tokensPerSecond"] = r.TokensPerSecond,
                    ["violations"] = new JArray(r.Violations ?? new List<string>())
                });
            }

            var report = new JObject
            {
                ["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                ["summary"] = summary,
                ["results"] = items
            };

            File.WriteAllText(path, report.ToString(Formatting.Indented));
            Console.WriteLine("Report saved to: " + path);
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.Kill();
                }
                process.Dispose();
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
