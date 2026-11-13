using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
        private const string Architect = "architect_subagent";
        private const string Planner = "task_planner_subagent";
        private const string Retriever = "context_retriever_subagent";

        public TestContext TestContext { get; set; }

        // ---------------------------------------------------------------------
        // Data model
        // ---------------------------------------------------------------------

        internal sealed class Scenario
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
            // Only the A5 block sets these fields; while it is compiled out (EDIT_SUBAGENT_BENCHMARK) they keep
            // their defaults, hence CS0649 is suppressed here.
#pragma warning disable CS0649
            // True => both seed files are (re)created before the run and verified on disk after it.
            public bool WithFixture;

            // Exact expected content after the run. null => the file must still equal its seed.
            public string ExpectedProbe;
            public string ExpectedNotes;

            // Folder inventory right after the reset; files that appear later were created by the agent.
            public HashSet<string> FixtureFilesBefore;

            // The scenario legitimately names a path that must not exist (a missing file).
            public bool SkipInventedPathCheck;
#pragma warning restore CS0649

            // Soft evidence: same semantics as MustContain / MustContainAny / MustNotContain, but a
            // hit produces a non-hard violation (PARTIAL) instead of FAIL. Used for report quality,
            // so a correct edit on disk is never masked by a cosmetic reporting defect.
            public string[] SoftMustContain = new string[0];
            public string[][] SoftMustContainAny = new string[0][];
            public string[] SoftMustNotContain = new string[0];

            public TimeSpan ReadTimeout = TimeSpan.FromMinutes(5);

            // Text replacements applied to files (relative to the opened solution) right before the run and
            // reverted byte-for-byte right after it. Used by the build benchmark to plant a compile error or a
            // failing test in the build fixture.
            public List<FileMutation> Mutations = new List<FileMutation>();
        }

        internal sealed class FileMutation
        {
            public string RelativePath;
            public string Find;
            public string Replace;

            public FileMutation(string relativePath, string find, string replace)
            {
                RelativePath = relativePath;
                Find = find;
                Replace = replace;
            }
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
            // Automated recoveries of the run (repeated call, malformed call, unresolved ref markers); empty when none.
            public List<string> Recoveries = new List<string>();
            public int UnresolvedRefCount;
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
            public List<string> Recoveries { get; set; }
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
        // edit_subagent fixture
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
        internal static string PatchText(string text, string oldText, string newText)
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

        /// <summary>
        /// Builds the main benchmark list. Every expectation taken from live code goes through LineOf, LineAt,
        /// MatchingLines, Ev, EvFile or Absent, which throw when the code no longer matches, so
        /// <c>SubAgentBenchmarkSelfTests.Scenarios_MatchCurrentSources</c> catches drift without a model run.
        /// </summary>
        internal static List<Scenario> BuildScenarios()
        {
            var list = new List<Scenario>();
            var guid = Guid.NewGuid().ToString("N").Substring(0, 12);
            // ---- A.1 code_explorer_subagent: discovery (12) ----

            list.Add(Sc("A1.1", Explorer,
                "Search for the exact identifier GetAsync in the solution. Report yes if the returned results include GetAsync and report the returned total count.",
                "yes"));

            list.Add(Sc("A1.2", Explorer,
                "Find the file named ChatLogSerializer.cs. Report yes if the returned path includes Persistence.",
                "yes"));

            list.Add(Sc("A1.3", Explorer,
                "Search for the exact XML text PackageReference Include=\"Microsoft.CodeAnalysis\". Report yes if the returned result is a PackageReference to Microsoft.CodeAnalysis.",
                "yes"));

            list.Add(Sc("A1.4", Explorer,
                "Search for the exact identifier ConsolidateLastExchangeAsync with project filter LMLocal. Report yes if the returned results belong to project LMLocal.",
                "yes"));

            list.Add(Sc("A1.5", Explorer,
                $"Search for the literal text bench-{guid}-{guid}. Report yes if the search tool returned zero matches.",
                "yes"));

            list.Add(Sc("A1.6", Explorer,
                "Search for the exact text using Newtonsoft. Retrieve all returned pages. Report yes if the final result contains the complete returned result list and the reported total count.",
                "yes"));

            list.Add(Sc("A1.7", Explorer,
                "Get the solution overview and report yes if returned project list doen't contain LMLocal.Web.",
                "yes"));

            var a108Lines = MatchingLines(A108File, A108Text);
            var a108Last = a108Lines[a108Lines.Count - 1];
            var a108 = Sc("A1.8", Explorer,
                "In " + A108File + " search for the exact text " + A108Text + " and retrieve every page of results. Report the total number of matching lines and the last matched line with its text.",
                a108Lines.Count.ToString(),
                A108File + ":" + a108Last + ": `" + LineAt(A108File, a108Last) + "`");
            a108.MustNotContain = new[] { "@LMLocal" };
            list.Add(a108);

            const string a109File = @"LMLocal\Resources\js\lib\token.stats.js";
            const string a109Text = "parts.push(`${formatSpeed(stats.tokenSpeed)} ${UIText.TEXT_TOKENS_PER_SECOND}`);";
            var a109 = Sc("A1.9", Explorer,
                "Search for the exact text formatSpeed(stats.tokenSpeed) with extension filter .js and report every matched line with its text.",
                a109File + ":" + LineOf(a109File, a109Text) + ": ``" + a109Text + "``");
            a109.MustNotContain = new[] { @"\`", "@LMLocal" };
            list.Add(a109);


            const string a110File = @"LMLocal\Application\Chat\HistoryCompactor.cs";
            const string a110Text = "internal enum CompactionOutcome";
            var a110 = Sc("A1.10", Explorer,
                @"Search for the exact text CompactionOutcome in the folder LMLocal\Application\Chat and report every matched line with its text.",
                a110File + ":" + LineOf(a110File, a110Text) + ": `" + a110Text + "`");
            a110.MustNotContain = new[] { "ChatSession", "@LMLocal" };
            list.Add(a110);

            list.Add(Sc("A1.11", Explorer,
                "Search for CompactForcedAsync OR ConsolidateLastExchangeAsync in project LMLocal and report the files that contain each of them.",
                Ev(HistoryCompactorFile, "CompactForcedAsync"), Ev(ChatHistoryManagerFile, "ConsolidateLastExchangeAsync"),
                EvFile(HistoryCompactorFile), EvFile(ChatHistoryManagerFile)));

            const string a112Decl = @"LMLocal\Application\SubAgents\ReadRefs\SearchResultRecorder.cs";
            const string a112DeclText = "internal static class SearchResultRecorder";

            const string a112Call = @"LMLocal\Application\SubAgents\ReadRefs\SubAgentRunEvidence.cs";
            const string a112CallText = "SearchResultRecorder.TryRecord(_searchLedger, result);";

            const string a112Q3File = @"LMLocal.Tests.Unit\InstructionsControllerTests.cs";
            const string a112Q3Anchor = "namespace LMLocal.Tests.Unit";
            var a112Q3Text = $"// bench-{guid}-q3";
            var a112 = Sc("A1.12", Explorer,
                "Perform three independent searches and report all three results in one report:\n"
                + "1. Search only in project LMLocal: where is the class SearchResultRecorder declared? Report file:line with the declaration line text.\n"
                + "2. Search only in project LMLocal: which places call SearchResultRecorder.TryRecord? Report file:line with the matched line text for every call site.\n"
                + $"3. Search the entire solution, including test projects, for the literal text bench-{guid}-q3. Report every hit with file:line and the matched line text, or state clearly that there are none.",
                a112Decl + ":" + LineOf(a112Decl, a112DeclText) + ": `" + a112DeclText + "`",
                a112Call + ":" + LineOf(a112Call, a112CallText) + ": `" + a112CallText + "`",
                // The planted line takes the anchor's line number; the anchor moves one line down.
                a112Q3File + ":" + LineOf(a112Q3File, a112Q3Anchor) + ": `" + a112Q3Text + "`");
            a112.Mutations.Add(new FileMutation(a112Q3File, a112Q3Anchor, a112Q3Text + "\r\n" + a112Q3Anchor));
            a112.MustNotContain = new[] { "@LMLocal" };
            list.Add(a112);

            // ---- A.2 code_reader_subagent: deterministic reading (14) ----------

            const string a201File = @"LMLocal\GettingStarted.txt";
            list.Add(ScVerbatim("A2.1", Reader,
                "Read lines 1-12 of LMLocal\\GettingStarted.txt and return them verbatim.",
                Ev(a201File, "LM Local", 12), Ev(a201File, "Visual Studio", 12)));

            // A2.2, A2.3 and A2.5 read frozen copies in Fixtures\ReaderCode, so refactoring LMLocal never breaks them.
            var a202 = Sc("A2.2", Reader,
                "Find the file named exactly SseFrameParser.cs and read it (there is also a SseFrameParserFactory.cs in the same folder - do not pick the factory).",
                Ev(ReaderCodeDir + "SseFrameParser.cs", "FrameRecord"), Ev(ReaderCodeDir + "SseFrameParser.cs", "SseLineSplitter"));
            a202.MustNotContain = new[] { Ev(ReaderCodeDir + "SseFrameParserFactory.cs", "CreateForEndpoint") };
            list.Add(a202);

            list.Add(ScVerbatim("A2.3", Reader,
                "Read these 4 files and return them verbatim: SessionLogWriter.cs, CompletionSummary.cs, SseFrameParser.cs, HistoryLedger.cs.",
                Ev(ReaderCodeDir + "SessionLogWriter.cs", "WriteSessionHeader"),
                Ev(ReaderCodeDir + "CompletionSummary.cs", "FinishReasonCode"),
                Ev(ReaderCodeDir + "SseFrameParser.cs", "ParseFramesAsync"),
                Ev(ReaderCodeDir + "HistoryLedger.cs", "MergeLastExchangeAsync")));

            list.Add(Sc("A2.4", Reader,
                "Read LMLocal/Infrastructure/Persistence/NoSuchSerializer.cs and return yes if not found.",
                "yes"));

            // The anchors are in the last lines of the file: a partial read cannot contain them.
            list.Add(ScVerbatim("A2.5", Reader,
                "Read the file at exact path " + ReaderCodeDir + "SessionLogWriter.cs and return its contents verbatim. If the entire file is not returned, clearly state that it is partial and identify the missing portion.",
                Ev(ReaderCodeDir + "SessionLogWriter.cs", "internal const int MaxTitleLength = 120;"),
                Ev(ReaderCodeDir + "SessionLogWriter.cs", "sb.Append(\"...\");")));

            var a206 = ScVerbatim("A2.6", Reader,
                "Read the file at exact path LMLocal.Tests.E2E.VSIX\\Fixtures\\reader-truth-fixture.md and return it verbatim.",
                "## 1. First Section", "## Appendix A. References", "## 2. Second Section", "Tail-Anchor-Open");
            a206.MustAppearExactlyOnce = new[] { "Seed-One-Anchor", "Ref-Anchor-Alpha", "Tail-Anchor-Open" };
            list.Add(a206);

            var a207 = Sc("A2.7", Reader,
                "Read the file at exact path LMLocal.Tests.E2E.VSIX\\Fixtures\\reader-truth-fixture.md. Verify whether these headings appear in this exact order: \"## 1. First Section\", \"## 2. Second Section\", \"## 3. Third Section\", \"## Appendix A. References\". Use the actual file contents and line positions. Do not infer or assume any heading or position that is not present in the retrieved content.",
                "## Appendix A. References");
            a207.MustContainInOrder = new[] { "## 1. First Section", "## Appendix A. References", "## 2. Second Section", "## 3. Third Section" };
            list.Add(a207);

            list.Add(Sc("A2.8", Reader,
                "Read the entire file LMLocal.Tests.E2E.VSIX\\Fixtures\\reader-large-fixture.md and return it verbatim. Continue reading until the entire file has been retrieved; do not stop while has_more_results is true.",
                "Reader-Start", "Reader-End"));

            var a209 = ScVerbatim("A2.9", Reader,
                "In LMLocal\\Infrastructure\\Security\\TestConnectionErrorClassifier.cs return VERBATIM only the single line that contains ExceptionFormatter.Format. Return no other line of the file and do not mark the file as missing.",
                Ev(@"LMLocal\Infrastructure\Security\TestConnectionErrorClassifier.cs", "return ExceptionFormatter.Format(ex);"));
            a209.MustNotContain = new[] { "Missing files" };
            list.Add(a209);

            var a210 = ScVerbatim("A2.10", Reader,
                "In LMLocal\\Application\\ModelsList\\ModelsListService.cs return VERBATIM only the two lines that contain ExceptionFormatter.Format. Return no other line of the file and do not mark the file as missing.",
                Ev(@"LMLocal\Application\ModelsList\ModelsListService.cs", "var errorResponse = new UnifiedListModelsResponse { Error = ExceptionFormatter.Format(ex) };"),
                Ev(@"LMLocal\Application\ModelsList\ModelsListService.cs", "return new UnifiedListModelsResponse { Error = ExceptionFormatter.Format(ex) };"));
            a210.MustNotContain = new[] { "Missing files" };
            list.Add(a210);

            var a211 = ScVerbatim("A2.11", Reader,
                "Read the file at exact path LMLocal.Tests.E2E.VSIX\\Fixtures\\reader-ranges-fixture.md and return ONLY lines 11-15 and lines 30-39 VERBATIM, with each line carrying its actual 1-based line number. Do not include any other line of the file, do not substitute different ranges, and do not mark the file as missing.",
                "RANGE-ANCHOR-A1-OPEN", "RANGE-ANCHOR-A1-CLOSE", "RANGE-ANCHOR-A3-CLOSE", "RANGE-ANCHOR-APP-OPEN");
            a211.MustContainInOrder = new[] { "RANGE-ANCHOR-A1-OPEN", "RANGE-ANCHOR-A1-CLOSE", "RANGE-ANCHOR-A3-CLOSE", "RANGE-ANCHOR-APP-OPEN" };
            a211.MustNotContain = new[] { "RANGE-ANCHOR-A2-OPEN", "RANGE-ANCHOR-A2-CLOSE", "RANGE-ANCHOR-A3-OPEN", "RANGE-ANCHOR-APP-CLOSE", "RANGE-ANCHOR-TAIL-OPEN", "RANGE-ANCHOR-A1-OPEN-COPY", "RANGE-ANCHOR-A1-CLOSE-COPY", "# End of fixture" };
            list.Add(a211);

            var a212 = ScVerbatim("A2.12", Reader,
                "Read the file at exact path LMLocal.Tests.E2E.VSIX\\Fixtures\\reader-symbols-fixture.md and return it verbatim, with each line carrying its 1-based line number. Preserve every character exactly — multiple spaces, tabs, single and double quotes, and every bracket. Do not normalize, drop, or rebalance any of them.",
                "SYM-SPACE-MID: two  spaces  between  words",
                "SYM-QUOTE-NESTED-SINGLE: 'outer \"inner\" value'",
                "SYM-QUOTE-AT-END: trailing quote\"",
                "SYM-PAREN-OPEN: function(",
                "SYM-PAREN-BALANCED: ((a, b), [c, d], {e: f})",
                "SYM-BRACKET-MIXED: {[(\"a\", {\"b\": [1,2]})]}",
                "SYM-EMPTY-AFTER: this line follows the empty line above",
                "SYM-END-TAIL: last line anchor");

            a212.MustNotContain = new[]
            {
                "SYM-EMPTY-UNWANTED",
                "//SECTION-SKIP",
                "///"
            };
            list.Add(a212);

            const string a213Cs = @"LMLocal\Core\Models\StreamChunk.cs";
            var a213 = ScVerbatim("A2.13", Reader,
                "Read verbatim with exact line numbers (I will use them for precise line-range replacements):\n"
                + "1. LMLocal.Tests.E2E.VSIX\\Fixtures\\reader-ranges-fixture.md - lines 11-15\n"
                + "2. " + a213Cs + " - lines 1-6",
                "reader-ranges-fixture.md, lines 11-15",
                "StreamChunk.cs, lines 1-6",
                "RANGE-ANCHOR-A1-OPEN",
                "RANGE-ANCHOR-A1-CLOSE",
                LineAt(a213Cs, 6));
            a213.MustNotContain = new[] { "// Line", "RANGE-ANCHOR-A2-OPEN", "Missing files" };
            list.Add(a213);

            var a214 = ScVerbatim("A2.14", Reader,
                "In LMLocal\\Application\\SubAgents\\ReadRefs\\FileContentRenderer.cs return VERBATIM the whole method AppendSearchLocation, from its signature line to its closing brace. Return no other method of the file and do not mark the file as missing.",
                Ev(@"LMLocal\Application\SubAgents\ReadRefs\FileContentRenderer.cs", "public static void AppendSearchLocation(StringBuilder sb, string filePath, int lineNumber, string text)"),
                Ev(@"LMLocal\Application\SubAgents\ReadRefs\FileContentRenderer.cs", "sb.Append(fence);"));
            a214.MustNotContain = new[] { "AppendRefContentBlock(StringBuilder", "AppendNumberedLines(sb, lines);", "Missing files" };
            list.Add(a214);

            // ---- A.3 symbol_analyzer_subagent: symbols & references (9) ----

            const string a33File = @"LMLocal\Core\Models\StreamCompletionResult.cs";
            list.Add(Sc("A3.1", Analyzer,
                "Find the C# symbol ToolCallRecord and report its declaration file and line.",
                EvFile(a33File), LineOf(a33File, "internal class ToolCallRecord").ToString()));

            list.Add(Sc("A3.2", Analyzer,
                "Find all references to ChatLogSerializer. Report file:line coordinates.",
                "ChatLogSerializer", Ev(@"LMLocal\Infrastructure\Persistence\ChatPersistenceService.cs", "ChatLogSerializer", fileNameOnly: true)));

            list.Add(Sc("A3.3", Analyzer,
                "Inspect the type StreamCompletionResult and list its public members with file:line locations.",
                Ev(a33File, "ContentResponse"), Ev(a33File, "ToolCalls"),
                "StreamCompletionResult.cs:" + LineOf(a33File, "public IReadOnlyList<ToolCallRecord> ToolCalls { get; set; }"),
                "StreamCompletionResult.cs:" + LineOf(a33File, "public int? HttpStatusCode { get; set; }")));

            list.Add(Sc("A3.4", Analyzer,
                "Find the JavaScript symbol lmInit and report its file and line. Use the semantic JavaScript tool.",
                Ev(@"LMLocal\Resources\js\app.js", "lmInit", fileNameOnly: true)));

            list.Add(NotFoundSc("A3.5", Analyzer,
                "Find the symbol GeminiThoughtSignaturePolicyEnforcer.",
                Absent("LMLocal", "GeminiThoughtSignaturePolicyEnforcer")));

            list.Add(Sc("A3.6", Analyzer,
                "Find the symbol ToolCallRecord, then read its declaration context.",
                Ev(a33File, "ToolCallRecord"), EvFile(a33File)));

            var a37 = Sc("A3.7", Analyzer,
                "Find all references to the ToolCalls property of the type ChatMessage (declared in LMLocal/Core/Models/ChatMessage.cs). Ignore the same-named ToolCalls property on other types such as Message and StreamCompletionResult. Retrieve ALL pages of references and state the total reference count.",
                "ToolCalls", Ev(@"LMLocal\Core\Models\ChatMessage.cs", "ToolCalls", fileNameOnly: true));
            a37.MustContainAny = new[] { new[] { Ev(@"LMLocal\Infrastructure\LlmApi\ApiRequestBuilder.cs", "ToolCalls", fileNameOnly: true), "ApiRequestBuilder" } };
            list.Add(a37);

            var a38 = Sc("A3.8", Analyzer,
                "Inspect ConsolidateLastExchangeAsync and determine whether the ToolCalls of the final assistant message in the consolidated fragment are copied by reference or reconstructed. Read the actual source before answering.",
                Ev(ChatHistoryManagerFile, "ConsolidateLastExchangeAsync"), "ToolCalls");
            a38.MustContainAny = new[] { new[] { "reference", "same instance", "reused", "not reconstructed" } };
            list.Add(a38);

            list.Add(Sc("A3.9", Analyzer,
                "Find the C# method AddAssistantMessage in LMLocal/Application/Chat/ChatHistoryManager.cs. It has an interface declaration and two concrete overloads. Select the concrete implementation overload that accepts IReadOnlyList<ToolCallRecord> and return its declaration line together with its full implementation body.",
                "AddAssistantMessage", Ev(ChatHistoryManagerFile, "toolCallObjects"), Ev(ChatHistoryManagerFile, "normalizedArguments")));

            // ---- A.4 reviewer_subagent: structured status review (3) ----

            var a41 = Sc("A4.1", Reviewer,
                "Review the following completed deliverable and produce your structured review ending with an exact final 'Status: ...' line. The change is a small refactoring of ChatHistoryManager: the ToolCalls copying logic was extracted into a private helper. The build passes, all existing unit tests for the chat service pass, and a new unit test covers the copied-by-reference behavior. The user requested exactly this refactoring and it is complete; no further phases are planned. The change is already committed, so get_changes_diff is expected to show no pending changes; that alone is not a defect of the deliverable. Review it adversarially.",
                "Status:");
            a41.MustContainAny = new[] { new[] { "COMPLETE", "PARTIAL" } };
            a41.MustNotContain = new[] { "Status: REJECTED" };

            a41.CheckRefusal = false;
            list.Add(a41);

            var a42 = Sc("A4.2", Reviewer,
                "Review the following implementation plan for executability and completeness. Produce your structured review ending with an exact final 'Status: ...' line. PLAN: Phase 1 adds a new ChatProcessor class that depends on IMessageBus, and wires it into ChatHistoryManager. Phase 2 (explicitly planned for a later release, NOT part of this deliverable) registers IMessageBus in DI and implements its only implementation. Phase 1 must be delivered now, must compile, and must run in production. Determine whether Phase 1 is executable as stated.",
                "Status:");
            a42.MustContainAny = new[] { new[] { "PARTIAL", "REJECTED" } };
            a42.MustNotContain = new[] { "Status: COMPLETE" };
            list.Add(a42);

            var a43 = Sc("A4.3", Reviewer,
                "Review a documentation-only deliverable. Nothing in it changes code, and the change is already committed, so get_changes_diff is expected to show no pending changes; that alone is not a defect.\n"
                + "User request: \"add to the plan how symbol references from get_symbol_info reach the main chat as verified lines\".\n"
                + "Verified facts (read from the code):\n"
                + "- SearchLedger (LMLocal\\Application\\SubAgents\\ReadRefs\\SearchLedger.cs) stores (file, line, text) records. ReadRefExpander replaces an @path:line marker in a subagent's final answer with 'path:line: `text`' only for lines recorded in SearchLedger.\n"
                + "- SubAgentRunEvidence (LMLocal\\Application\\SubAgents\\ReadRefs\\SubAgentRunEvidence.cs) records search_file_content results into SearchLedger through SearchResultRecorder.TryRecord. No other tool result is recorded there.\n"
                + "- get_symbol_info and get_symbol_info_js results are not recorded anywhere.\n"
                + "- The symbol_analyzer_subagent prompt contains no instructions about markers.\n"
                + "Deliverable (plan text added to Docs\\explorer-search-quality-plan.md):\n"
                + "Step 1. No code change is needed: references returned by get_symbol_info are already recorded in SearchLedger, so the analyzer's @path:line markers expand today.\n"
                + "Step 2. Add a short section about markers to the symbol_analyzer_subagent prompt.\n"
                + "Step 3. Measure the share of analyzer answers with expanded markers.\n"
                + "Review the deliverable against the verified facts and produce your structured review ending with an exact final 'Status: ...' line.",
                "Status:");
            a43.MustContainAny = new[]
            {
                new[] { "PARTIAL", "REJECTED" },
                new[] { "Step 1", "not recorded", "no code change" }
            };
            a43.MustNotContain = new[] { "Status: COMPLETE" };
            a43.CheckRefusal = false;
            list.Add(a43);

            // ---- A.6 architect_subagent: architecture decision (1) ----

            var a61 = Sc("A6.1", Architect,
                @"Requirement: show the number of pending (uncommitted) file changes in the Visual Studio status bar and keep it current whenever the snapshot changes. Single process, one consumer, no persistence or retries needed.
            Verified context:
            - ISnapshotManager (LMLocal\Infrastructure\Tooling\BuiltInVs\Snapshot\SnapshotManager.cs) exposes event Func<IReadOnlyList<SnapshotFileChange>, Task> SnapshotChangedAsync, raised after every snapshot change, commit, rollback and reset.
            - WebViewBridge (LMLocal\Infrastructure\WebView\WebViewBridge.cs) already subscribes to it in its constructor: _snapshotManager.SnapshotChangedAsync += OnSnapshotChangedAsync.
            - ISnapshotManager is resolved from DI.
            Choose the architecture for this change.",
                "SnapshotChangedAsync");
            a61.MustContainAny = new[] { new[] { "NO_ARCH_CHANGE", "DECIDED", "CONDITIONAL" } };
            list.Add(a61);

            // ---- A.7 task_planner_subagent: planning decisions (2) ----

            var a71 = Sc("A7.1", Planner,
                @"Task: in LMLocal\Application\Chat\HistoryCompactor.cs change the modifier of the private struct ChatStepRange from 'private struct' to 'private readonly struct'. All its fields are already readonly. One file, one line, no callers change.",
                "needs_plan: false", "persist: false", "blocked: false");
            list.Add(a71);

            list.Add(Sc("A7.2", Planner,
                @"Task: add a recorder so that references returned by get_symbol_info and get_symbol_info_js are written into the per-run SearchLedger, so that symbol_analyzer_subagent can report @path:line markers that the runner expands.
            Verified context:
            - LMLocal\Application\SubAgents\ReadRefs\SearchResultRecorder.cs records search_file_content results into SearchLedger: internal static class with TryRecord(SearchLedger ledger, object result).
            - LMLocal\Application\SubAgents\ReadRefs\SubAgentRunEvidence.cs calls SearchResultRecorder.TryRecord in Record(toolName, result), which the subagent tool loop calls after every successful tool call.
            - LMLocal.Tests.Unit\Application\SubAgents\SearchRefExpansionTests.cs holds the NUnit tests for SearchResultRecorder and marker expansion.
            - LMLocal\LMLocal.csproj is a legacy project with explicit Compile Include entries; C# 7.3, .NET Framework 4.7.2.
            Files to change: a new LMLocal\Application\SubAgents\ReadRefs\SymbolReferenceRecorder.cs, SubAgentRunEvidence.cs, LMLocal.csproj, SearchRefExpansionTests.cs.",
                "needs_plan: true", "SymbolReferenceRecorder", "SubAgentRunEvidence", "SearchRefExpansionTests"));



            return list;
        }

        /// <summary>
        /// context_retriever_subagent scenarios on Fixtures\KnowledgeFixture: its docs\ folder is the knowledge base.
        /// Until 2026-11-11 A8.1 ran on LMLocal's own docs\, which change every day; the benchmark analysis of A8.1
        /// itself, written to docs\, became the top search hit there.
        /// </summary>
        internal static List<Scenario> BuildKnowledgeScenarios()
        {
            var list = new List<Scenario>();

            // The decision is in docs\set-file-project-status-reload.md; the two other docs mention the same
            // tool or the same "unsaved changes" topic, but not this decision.
            // The evidence is the first sentence of the decision section: search_solution_knowledge returns only
            // ~150 characters on each side of the match (the section heading), and the retriever cannot read the
            // file, so "discards" two lines further down never reaches it (run 2026-11-11 19:45).
            // "reload" is not usable: both distractors contain it.
            var a81 = Sc("A8.1", Retriever,
                "Find the earlier documented decision on what set_file_project_status does when the project has unsaved changes in Visual Studio. Report the source document and the decision.",
                "set-file-project-status-reload");
            a81.MustContainAny = new[] { new[] { "from disk", "source of truth" } };
            list.Add(a81);

            return list;
        }

        // ---------------------------------------------------------------------
        // Main run
        // ---------------------------------------------------------------------
        public static IEnumerable<object[]> GetIterations()
        {
            const int from = 1;
            const int to = 5;
            return Enumerable.Range(from, to - from + 1).Select(i => new object[] { i }).ToList();
        }

        // ---------------------------------------------------------------------
        // Loop recovery
        //
        // Guards the recovery path: an identical repeated tool call must close the turn and
        // continue on the same history instead of hard-stopping the run.
        // ---------------------------------------------------------------------

        /// <summary>
        /// Forces three byte-identical tool calls. The audit-trail sentence is deliberate: a plain
        /// "search three times" is silently skipped by the model (observed during a manual probe),
        /// which would let the test pass without the recovery branch ever running.
        /// </summary>
        private const string LoopProbePrompt =
            "You are being instrumented. Do exactly this:\n" +
            "Make exactly one tool call per response and wait for its result before the next call.\n" +
            "1. Call search_file_content with query \"zzz_nonexistent_marker_zzz\" and max_results 25.\n" +
            "2. Call search_file_content a SECOND time with exactly the same arguments.\n" +
            "3. Call search_file_content a THIRD time with exactly the same arguments.\n" +
            "An empty result is NOT a reason to skip a call. Do not change the query. Do not use find_files.\n" +
            "Then answer with one line only: CALLS_MADE: <how many calls you actually made>";

        [TestMethod]
        [Ignore("Disable to run benchmark")]
        public async Task Run_LoopRecovery_RepeatedToolCall_IsRecoveredNotFailed()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15)))
            {
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(2), cts.Token);
                    try
                    {
                        var solutionPath = GetSolutionPath();
                        var open = await client.SendCommandAsync("OpenSolution|" + solutionPath, cts.Token);
                        Assert.AreEqual("OK", open, "OpenSolution IPC command failed");

                        await WaitForSolutionLoadedAsync(client, cts.Token);

                        var command = "RunSubAgent|" + Explorer + "|" + Uri.EscapeDataString(LoopProbePrompt);
                        var json = await client.SendCommandAsync(command, TimeSpan.FromMinutes(5), cts.Token);

                        var run = JsonConvert.DeserializeObject<RunResponseDto>(json);
                        Assert.IsNotNull(run, "RunSubAgent returned no parsable response: " + json);

                        TestContext.WriteLine("loop probe: success=" + run.Success
                            + " rounds=" + run.Rounds
                            + " runId=" + run.RunId
                            + " recoveries=" + (run.Recoveries == null ? "<none>" : string.Join(" | ", run.Recoveries))
                            + " error=" + (run.Error ?? "<none>"));

                        // The recovery branch is proven by the run's own recovery notes, never by the answer
                        // text: the model can confidently answer without repeating a single call,
                        // so a content-only assertion would pass while the branch under test never ran.
                        Assert.IsTrue(
                            run.Recoveries != null && run.Recoveries.Any(r => r.IndexOf("repeated tool call loop", StringComparison.Ordinal) >= 0),
                            "No loop recovery in run '" + run.RunId + "'. "
                            + "Either recovery did not fire, or the model never repeated the call "
                            + "(probe inconclusive - re-run before suspecting the feature).");

                        Assert.IsTrue(run.Success, "A recovered run must complete, not fail: " + (run.Error ?? "<no error>"));
                        Assert.DoesNotMatchRegex(
                            new Regex("repeated tool call loop"),
                            run.Error ?? string.Empty,
                            "The hard stop must not be reached while recovery is still available.");
                    }
                    finally
                    {
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
        // Unresolved ref marker recovery
        //
        // Guards the answer-side recovery path: a final answer with a ref marker the runner cannot expand
        // must go back to the model once with the UNRESOLVED REF MARKERS nudge instead of being returned as is.
        // ---------------------------------------------------------------------

        /// <summary>
        /// A file that does not exist in the solution: no tool can return its lines, so a marker on it stays
        /// unresolved even if the model reads or searches before answering.
        /// </summary>
        private const string RefProbeMissingFile = @"LMLocal\Core\Models\RefGuardProbeMissing.cs";

        /// <summary>
        /// Asks for a first answer that is only a marker on <see cref="RefProbeMissingFile"/>. Unlike the loop
        /// probe, one answer is enough to trigger the guard.
        /// </summary>
        private const string RefProbePrompt =
            "You are being instrumented. Do not call any tools.\n" +
            "Answer with exactly this one line and nothing else:\n" +
            "@" + RefProbeMissingFile + ":12";

        [TestMethod]
        [Ignore("Disable to run benchmark")]
        public async Task Run_UnresolvedRefRecovery_MarkerOnUnreadLines_IsRecoveredNotFailed()
        {
            Assert.IsFalse(
                File.Exists(Path.Combine(Path.GetDirectoryName(GetSolutionPath()), RefProbeMissingFile)),
                RefProbeMissingFile + " exists, so the probe marker could be expanded; pick a missing path.");

            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15)))
            {
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(2), cts.Token);
                    try
                    {
                        var solutionPath = GetSolutionPath();
                        var open = await client.SendCommandAsync("OpenSolution|" + solutionPath, cts.Token);
                        Assert.AreEqual("OK", open, "OpenSolution IPC command failed");

                        await WaitForSolutionLoadedAsync(client, cts.Token);

                        var command = "RunSubAgent|" + Explorer + "|" + Uri.EscapeDataString(RefProbePrompt);
                        var json = await client.SendCommandAsync(command, TimeSpan.FromMinutes(5), cts.Token);

                        var run = JsonConvert.DeserializeObject<RunResponseDto>(json);
                        Assert.IsNotNull(run, "RunSubAgent returned no parsable response: " + json);

                        TestContext.WriteLine("ref probe: success=" + run.Success
                            + " rounds=" + run.Rounds
                            + " runId=" + run.RunId
                            + " unresolvedRefCount=" + run.UnresolvedRefCount
                            + " recoveries=" + (run.Recoveries == null ? "<none>" : string.Join(" | ", run.Recoveries))
                            + " error=" + (run.Error ?? "<none>")
                            + " content=" + (run.Content ?? "<none>"));

                        // Proven by the run's recovery notes, not by the answer: the model may skip the marker
                        // on its first answer, and then the guard never runs.
                        Assert.IsTrue(
                            run.Recoveries != null && run.Recoveries.Any(r => r.IndexOf("unresolved ref markers", StringComparison.Ordinal) >= 0),
                            "No unresolved ref marker recovery in run '" + run.RunId + "'. "
                            + "Either recovery did not fire, or the model's first answer had no marker "
                            + "(probe inconclusive - re-run before suspecting the feature).");

                        // The second answer may still carry the marker: there is one attempt, after it the answer
                        // is returned as is, so UnresolvedRefCount is only logged.
                        Assert.IsTrue(run.Success, "A recovered run must complete, not fail: " + (run.Error ?? "<no error>"));
                        Assert.IsTrue(run.Rounds >= 2, "The nudge needs its own round; rounds=" + run.Rounds);
                    }
                    finally
                    {
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
        // Build benchmark (build_subagent) on the build fixture
        //
        // Building LMLocal.sln inside the experimental instance would rebuild the extension that runs in it,
        // so build_subagent is benchmarked on Fixtures\BuildFixture\BuildFixture.sln instead.
        // Plan: Docs\build-subagent-fixture-benchmark-plan.md.
        // ---------------------------------------------------------------------

        private const string Builder = "build_subagent";

        internal static List<Scenario> BuildBuildScenarios()
        {
            var list = new List<Scenario>();

            // B1.1 - the most common real build task (2026-11-04..10: 77% build + tests, 65% errors verbatim):
            // clean fixture, everything compiles and all 3 tests pass. The answer format of build_subagent is not
            // fixed, so the counts are matched in several common spellings.
            var b11 = Sc("B1.1", Builder,
                "Build the solution and run all unit tests in the project BuildFixture.Tests. Report total/passed/failed counts and any compiler error verbatim with file and line.");
            b11.MustContainAny = new[]
            {
                new[] { "Total: 3", "Total tests: 3", "total 3", "3 total", "3 tests", "TotalTests: 3", "\"total_tests\": 3", "Total = 3", "Total:** 3", "Total Tests:** 3" },
                // "Passed Tests: 3" added after run 2026-11-11-143210: a correct answer ("Passed Tests: 3") failed on the list.
                new[] { "Passed: 3", "Passed Tests: 3", "3 passed", "passed 3", "PassedTests: 3", "\"passed_tests\": 3", "Passed = 3", "Passed:** 3", "Passed Tests:** 3", "all 3" }
            };
            b11.MustNotContain = new[] { "error CS" };
            list.Add(b11);

            return list;
        }

        [TestMethod]
        //[Ignore("Disable to run benchmark")]
        public async Task Run_Build_Benchmark()
        {
            AssertWriteToolsEnabled();

            var fixturePath = GetBuildFixtureSolutionPath();

            // Outputs of an earlier run must not let the first scenario pass without a real build.
            CleanBuildFixtureOutputs(Path.GetDirectoryName(fixturePath));

            await RunFixtureBenchmarkAsync(fixturePath, BuildBuildScenarios(), "build-benchmark-report-");
        }

        [TestMethod]
        //[Ignore("Disable to run benchmark")]
        public async Task Run_Knowledge_Benchmark()
        {
            var fixturePath = GetKnowledgeFixtureSolutionPath();
            AssertKnowledgeBaseReachesFixtureDocs(Path.GetDirectoryName(fixturePath));

            await RunFixtureBenchmarkAsync(fixturePath, BuildKnowledgeScenarios(), "knowledge-benchmark-report-");
        }

        /// <summary>
        /// Opens a fixture solution in a new Experimental VS instance and runs <paramref name="scenarios"/> on it.
        /// Paths in the answers are checked against the fixture, not LMLocal.sln; the report is saved next to LMLocal.sln.
        /// </summary>
        private async Task RunFixtureBenchmarkAsync(string fixturePath, List<Scenario> scenarios, string reportPrefix)
        {
            // Requires a running LM Studio + an Experimental VS instance; never auto-run.
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(60)))
            {
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    IpcClient client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(2), cts.Token);
                    try
                    {
                        var fixtureRoot = Path.GetDirectoryName(fixturePath);

                        var resp = await client.SendCommandAsync("OpenSolution|" + fixturePath, cts.Token);
                        Assert.AreEqual("OK", resp, "OpenSolution IPC command failed for " + Path.GetFileName(fixturePath));

                        await WaitForSolutionLoadedAsync(client, cts.Token);

                        var results = new List<BenchmarkResult>();
                        foreach (var scenario in scenarios)
                        {
                            if (cts.IsCancellationRequested) break;

                            BenchmarkResult result;
                            try
                            {
                                // Paths in the answer are checked against the fixture, not LMLocal.sln.
                                result = await RunScenarioAsync(client, scenario, fixtureRoot, cts.Token);
                            }
                            catch (TimeoutException tex)
                            {
                                result = TimeoutResult(scenario, tex);
                                client.Dispose();
                                TestContext.WriteLine("[" + scenario.Id + "] read timeout, reconnecting...");
                                client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(2), cts.Token);
                            }

                            results.Add(result);
                            TestContext.WriteLine("[" + result.Id + "] " + result.Verdict
                                + " (" + Math.Round(result.DurationMs / 1000.0, 1) + "s) "
                                + string.Join("; ", result.Violations));

                            await Task.Delay(1000, cts.Token);
                        }

                        SaveReport(results, Path.GetDirectoryName(GetSolutionPath()), reportPrefix);

                        var failed = results.Where(r => r.Verdict == "FAIL").ToList();
                        if (failed.Count > 0)
                        {
                            Assert.Fail("Critical FAILs: "
                                + string.Join(" | ", failed.Select(r => r.Id + " -> " + string.Join("; ", r.Violations)))
                                + ". Full report saved next to LMLocal.sln.");
                        }
                    }
                    finally
                    {
                        client.Dispose();
                    }
                }
                finally
                {
                    TryKill(vs);
                }
            }
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

                        // Start from an empty get_changes_diff: the snapshot history survives VS restarts, so
                        // fixture edits of an earlier run would otherwise show up as pending deletions in the
                        // reviewer scenarios (A4.x) and be flagged as invented paths.
                        await CommitSnapshotAsync(client, cts.Token);

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
                        await CommitSnapshotAsync(client, cts.Token);
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

            var originals = ApplyMutations(solutionRoot, scenario);
            var sw = Stopwatch.StartNew();
            string json;
            try
            {
                var escapedPrompt = Uri.EscapeDataString(scenario.Prompt);
                var command = "RunSubAgent|" + scenario.AgentId + "|" + escapedPrompt;

                json = await client.SendCommandAsync(command, scenario.ReadTimeout, ct);
            }
            finally
            {
                sw.Stop();
                RevertMutations(originals);
            }

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
            result.Recoveries = r.Recoveries ?? new List<string>();
            result.UnresolvedRefCount = r.UnresolvedRefCount;

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
                notFoundMarker = Regex.IsMatch(content, @"(?i)(\bnot_found\b|not found|(could not|cannot|can't|can not) be (found|located)|does not exist|no (matches|results|references|instances|project|files?|explicit|such|direct|zero)|couldn't find|could not find|not exist|none of|i found no|found no|returned no|zero (matches|results)|did not find|no .* found)");
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

        /// <summary>
        /// Applies <see cref="Scenario.Mutations"/> and returns the original bytes of every touched file.
        /// A Find text that is not in the file throws: a stale expectation must not turn into a scenario that
        /// silently runs on the unmodified fixture and passes.
        /// </summary>
        internal static Dictionary<string, byte[]> ApplyMutations(string solutionRoot, Scenario s)
        {
            var originals = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (s.Mutations == null || s.Mutations.Count == 0)
                return originals;

            try
            {
                foreach (var m in s.Mutations)
                {
                    var path = Path.Combine(solutionRoot, m.RelativePath);
                    if (!originals.ContainsKey(path))
                        originals[path] = File.ReadAllBytes(path);

                    var text = File.ReadAllText(path);
                    var index = text.IndexOf(m.Find, StringComparison.Ordinal);
                    if (index < 0)
                        throw new InvalidOperationException("Mutation text not found in " + m.RelativePath + " (fix the scenario, not the fixture): " + m.Find);

                    File.WriteAllText(path, text.Substring(0, index) + m.Replace + text.Substring(index + m.Find.Length), new UTF8Encoding(false));
                }
            }
            catch
            {
                RevertMutations(originals);
                throw;
            }

            return originals;
        }

        internal static void RevertMutations(Dictionary<string, byte[]> originals)
        {
            foreach (var kv in originals)
                File.WriteAllBytes(kv.Key, kv.Value);
        }

        internal static string GetBuildFixtureSolutionPath()
        {
            var path = Path.Combine(Path.GetDirectoryName(GetSolutionPath()),
                "LMLocal.Tests.E2E.VSIX", "Fixtures", "BuildFixture", "BuildFixture.sln");
            if (!File.Exists(path))
                throw new InvalidOperationException("Build fixture not found at '" + path + "'");
            return path;
        }

        internal static string GetKnowledgeFixtureSolutionPath()
        {
            var path = Path.Combine(Path.GetDirectoryName(GetSolutionPath()),
                "LMLocal.Tests.E2E.VSIX", "Fixtures", "KnowledgeFixture", "KnowledgeFixture.sln");
            if (!File.Exists(path))
                throw new InvalidOperationException("Knowledge fixture not found at '" + path + "'");
            return path;
        }

        /// <summary>
        /// search_solution_knowledge reads the folders of KnowledgeBasePaths (user settings) relative to the open
        /// solution. The knowledge benchmark needs one of them to be the fixture's docs\ folder, otherwise every
        /// A8.x would fail on an empty knowledge base instead of on the agent.
        /// </summary>
        private static void AssertKnowledgeBaseReachesFixtureDocs(string fixtureRoot)
        {
            var paths = ReadKnowledgeBasePaths();
            var docs = Path.Combine(fixtureRoot, "docs");
            bool reachesDocs = paths
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0 && !Path.IsPathRooted(p))
                .Any(p => string.Equals(
                    Path.GetFullPath(Path.Combine(fixtureRoot, p)).TrimEnd('\\', '/'),
                    docs, StringComparison.OrdinalIgnoreCase));
            if (!reachesDocs)
                Assert.Inconclusive("KnowledgeBasePaths '" + paths + "' in LMLocalChat\\settings.json has no relative 'docs' folder; "
                    + "the knowledge benchmark needs one (for example 'docs/').");
        }

        /// <summary>
        /// build_solution and run_tests are Execution-level tools: in read-only mode (EnableAiWriteTools false, the
        /// AppSettings default) ToolQueueProvider drops them from every queue, so build_subagent gets neither and loops
        /// on get_solution_overview (2026-11-13 08:31). That is the mode, not the agent.
        /// </summary>
        private static void AssertWriteToolsEnabled()
        {
            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LMLocalChat", "settings.json");
            var value = File.Exists(file) ? JObject.Parse(File.ReadAllText(file))["EnableAiWriteTools"] : null;
            bool enabled = value != null && value.Type == JTokenType.Boolean && (bool)value;
            if (!enabled)
                Assert.Inconclusive("EnableAiWriteTools is off in LMLocalChat\\settings.json (read-only mode): build_solution and run_tests "
                    + "are not offered to build_subagent. Switch LMLocal to a mode with write tools and re-run.");
        }

        /// <summary>KnowledgeBasePaths from %LOCALAPPDATA%\LMLocalChat\settings.json; the AppSettings default when absent.</summary>
        private static string ReadKnowledgeBasePaths()
        {
            const string defaultPaths = "./; ./docs";
            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LMLocalChat", "settings.json");
            if (!File.Exists(file))
                return defaultPaths;
            var value = JObject.Parse(File.ReadAllText(file))["KnowledgeBasePaths"];
            return value == null || value.Type == JTokenType.Null ? defaultPaths : (string)value;
        }

        /// <summary>
        /// Deletes bin/obj of every fixture project, so the first scenario cannot pass on outputs of an earlier run.
        /// </summary>
        private static void CleanBuildFixtureOutputs(string fixtureRoot)
        {
            foreach (var projectDir in Directory.GetDirectories(fixtureRoot))
            {
                foreach (var name in new[] { "bin", "obj" })
                {
                    var dir = Path.Combine(projectDir, name);
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                }
            }
        }

        internal static string GetSolutionPath()
        {
            var solutionPath = Path.GetFullPath(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "LMLocal.sln"));
            if (!File.Exists(solutionPath))
            {
                throw new InvalidOperationException("Solution not found at '" + solutionPath + "'");
            }
            return solutionPath;
        }

        /// <summary>
        /// 1-based number of the first line of a solution-relative file whose trimmed text equals <paramref name="trimmedLine"/>.
        /// Lets scenarios expect "path:line: `text`" without hard-coding a line that shifts on every edit above it.
        /// </summary>
        private static int LineOf(string relativePath, string trimmedLine)
        {
            var root = Path.GetDirectoryName(GetSolutionPath());
            var lines = File.ReadAllLines(Path.Combine(root, relativePath));
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() == trimmedLine) return i + 1;
            }
            throw new InvalidOperationException("'" + trimmedLine + "' not found in " + relativePath);
        }

        /// <summary>
        /// Trimmed text of the 1-based line <paramref name="lineNumber"/> of a solution-relative file, read at run time.
        /// </summary>
        private static string LineAt(string relativePath, int lineNumber)
        {
            var root = Path.GetDirectoryName(GetSolutionPath());
            var lines = File.ReadAllLines(Path.Combine(root, relativePath));
            if (lineNumber < 1 || lineNumber > lines.Length || lines[lineNumber - 1].Trim().Length == 0)
                throw new InvalidOperationException("Line " + lineNumber + " of " + relativePath + " is missing or empty");
            return lines[lineNumber - 1].Trim();
        }

        // A frozen fixture, not LMLocal.csproj: the live project file grows with every new source file, and
        // on 2026-11-13 a shift from 573 to 582 matching lines changed the model's answer.
        internal const string A108File = @"LMLocal.Tests.E2E.VSIX\Fixtures\explorer-paging-fixture.xml";
        internal const string A108Text = "Include";

        internal const string ReaderCodeDir = @"LMLocal.Tests.E2E.VSIX\Fixtures\ReaderCode\";
        private const string ChatHistoryManagerFile = @"LMLocal\Application\Chat\ChatHistoryManager.cs";
        private const string HistoryCompactorFile = @"LMLocal\Application\Chat\HistoryCompactor.cs";

        private static string ReadSolutionFile(string relativePath)
        {
            var root = Path.GetDirectoryName(GetSolutionPath());
            return File.ReadAllText(Path.Combine(root, relativePath));
        }

        /// <summary>
        /// Evidence taken from a solution file: returns <paramref name="text"/> (or the file name when
        /// <paramref name="fileNameOnly"/> is set) after checking that the file contains the text, within the
        /// first <paramref name="maxLine"/> lines when it is above 0. Throws when the code has drifted, so
        /// the expectation is fixed before a model run instead of failing as a model error.
        /// </summary>
        internal static string Ev(string relativePath, string text, int maxLine = 0, bool fileNameOnly = false)
        {
            var content = ReadSolutionFile(relativePath);
            if (maxLine > 0)
                content = string.Join("\n", content.Replace("\r\n", "\n").Split('\n').Take(maxLine));

            if (content.IndexOf(text, StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Benchmark evidence '" + text + "' not found in " + relativePath
                    + (maxLine > 0 ? " (lines 1-" + maxLine + ")" : ""));

            return fileNameOnly ? Path.GetFileName(relativePath) : text;
        }

        /// <summary>File name of a solution file, after checking that the file exists.</summary>
        internal static string EvFile(string relativePath)
        {
            var root = Path.GetDirectoryName(GetSolutionPath());
            if (!File.Exists(Path.Combine(root, relativePath)))
                throw new InvalidOperationException("Benchmark evidence file not found: " + relativePath);
            return Path.GetFileName(relativePath);
        }

        /// <summary>
        /// Returns <paramref name="text"/> after checking that no *.cs file under the solution-relative folder
        /// contains it: a NOT FOUND scenario is wrong once the symbol appears in the code.
        /// </summary>
        internal static string Absent(string relativeDir, string text)
        {
            var root = Path.GetDirectoryName(GetSolutionPath());
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, relativeDir), "*.cs", SearchOption.AllDirectories))
            {
                var sep = Path.DirectorySeparatorChar;
                if (file.Contains(sep + "bin" + sep) || file.Contains(sep + "obj" + sep))
                    continue;
                if (File.ReadAllText(file).IndexOf(text, StringComparison.Ordinal) >= 0)
                    throw new InvalidOperationException("NOT FOUND scenario: '" + text + "' now exists in " + file);
            }

            return text;
        }

        /// <summary>
        /// 1-based numbers of the lines of a solution-relative file that contain <paramref name="text"/>
        /// (case-insensitive substring, like search_file_content), read at run time.
        /// </summary>
        internal static List<int> MatchingLines(string relativePath, string text)
        {
            var root = Path.GetDirectoryName(GetSolutionPath());
            var lines = File.ReadAllLines(Path.Combine(root, relativePath));
            var result = new List<int>();
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) result.Add(i + 1);
            }

            if (result.Count == 0)
                throw new InvalidOperationException("'" + text + "' not found in " + relativePath);
            return result;
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
        /// cannot distinguish "file ends with \n\n" from "the final \n counted as an empty line".
        /// </summary>
        internal static string NormalizeNewlines(string text)
        {
            return NormalizeLineEndings(text).TrimEnd('\n');
        }

        /// <summary>
        /// CRLF and lone CR become LF. The trailing newline is kept, because callers use this on
        /// exact text rather than on a whole-file comparison.
        /// </summary>
        internal static string NormalizeLineEndings(string text)
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

        private static async Task CommitSnapshotAsync(IpcClient client, CancellationToken ct)
        {
            var resp = await client.SendCommandAsync("CommitSnapshot", TimeSpan.FromSeconds(30), ct);
            Assert.AreEqual("OK", resp, "CommitSnapshot IPC command failed");
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

        private static void SaveReport(List<BenchmarkResult> results, string solutionRoot, string filePrefix = "benchmark-report-")
        {
            var fileName = filePrefix + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".json";
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
                    ["violations"] = new JArray(r.Violations ?? new List<string>()),
                    ["recoveries"] = new JArray(r.Recoveries ?? new List<string>()),
                    ["unresolvedRefCount"] = r.UnresolvedRefCount
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
