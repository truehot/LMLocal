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
        private const string Reviewer = "reviewer_subagent";
        private const string Architect = "architect_subagent";
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

            // ---- Location scoring: the answer is judged by "path:line" locations against ground truth computed from the repository; no words are read. ----
            // Every entry must be found in the answer. "relative\path:line" needs a location of that file covering the line
            // (a single line or a range); "relative\path" needs only the file (its last two path segments appear in the answer).
            public string[] ExpectedLocations = new string[0];
            // Alternative locations (OR-sets): each inner array is a group, and one location of every group must be found in the answer.
            // Every location of a group also counts as allowed for CheckForeignLocations.
            public string[][] ExpectedLocationsAny = new string[0][];
            // Locations that are acceptable but not required (used with CheckForeignLocations).
            public string[] AllowedLocations = new string[0];
            // True => a location in the answer that matches neither ExpectedLocations nor AllowedLocations is a hard failure (invented place).
            public bool CheckForeignLocations;
            // True => the answer must contain no location at all (the searched thing does not exist).
            public bool ExpectNoLocations;
            // Any location in one of these files is a hard failure (a claim about a file that is false).
            public string[] ForbiddenLocationPaths = new string[0];
            // > 0 => a range longer than this many lines does not cover a "path:line" expectation. Without it a marker over the whole file
            // ("path:1-9999") would cover every expected line of a search scenario. 0 = any range covers its lines.
            public int MaxCoverLines;

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

        /// <summary>code_explorer_subagent (A1.x): the reader scenarios, on LMLocal.sln itself. Its list is the method right below.</summary>
        [TestMethod]
        [DynamicData(nameof(GetIterations))]
        [Ignore("Disable to run benchmark")]
        public async Task Run_Explorer_Benchmark(int iteration)
        {
            await RunSolutionBenchmarkAsync(BuildExplorerScenarios(), iteration, "benchmark-report-");
        }

        /// <summary>
        /// Builds the code_explorer_subagent list (A1.x, the reader: find, search, read, symbols). Every expectation taken from live code
        /// goes through LineOf, LineAt, MatchingLines, Ev, EvFile or Absent, which throw when the code no longer matches, so
        /// <c>SubAgentBenchmarkSelfTests.Scenarios_MatchCurrentSources</c> catches drift without a model run.
        /// </summary>
        internal static List<Scenario> BuildExplorerScenarios()
        {
            var list = new List<Scenario>();
            var guid = Guid.NewGuid().ToString("N").Substring(0, 12);
            // ---- A.1 code_explorer_subagent: discovery, reading, symbols (30) ----

            var a12 = Sc("A1.2", Explorer,
                "Find the file named ChatLogSerializer.cs and report its path.");
            EvFile(@"LMLocal\Infrastructure\Persistence\ChatLogSerializer.cs"); // throws when the file moved: the expectation is fixed before a model run
            a12.ExpectedLocations = new[] { @"LMLocal\Infrastructure\Persistence\ChatLogSerializer.cs" };
            list.Add(a12);

            var a13 = Sc("A1.3", Explorer,
                "Search for the exact XML text PackageReference Include=\"Microsoft.CodeAnalysis\" and report every matched line with file:line.");
            a13.ExpectedLocations = LocationsOf("PackageReference Include=\"Microsoft.CodeAnalysis\"", ".csproj", ".").ToArray();
            a13.CheckForeignLocations = true;
            a13.MaxCoverLines = 3;
            a13.MustNotContain = new[] { "@LMLocal" };
            list.Add(a13);

            var a15 = Sc("A1.5", Explorer,
                $"Search for the literal text bench-{guid}-{guid} and report where it occurs, if anywhere.");
            a15.ExpectNoLocations = true;
            a15.CheckRefusal = false;
            list.Add(a15);

            // Only a soft check: the answer is a list of project names, not locations.
            var a17 = Sc("A1.7", Explorer,
                "Get the solution overview and list the projects of the solution.");
            EvFile(@"LMLocal.Tests.Unit\LMLocal.Tests.Unit.csproj");
            a17.SoftMustContain = new[] { "LMLocal.Tests.Unit" };
            a17.SoftMustNotContain = new[] { "LMLocal.Web" };
            list.Add(a17);

            var a108Lines = MatchingLines(A108File, A108Text);
            var a108Last = a108Lines[a108Lines.Count - 1];
            var a108 = Sc("A1.8", Explorer,
                "In " + A108File + " search for the exact text " + A108Text + " with max_results " + A108PageSize + " and retrieve every page of results. Report the total number of matching lines and the last matched line with its text.",
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

                a112Q3File + ":" + LineOf(a112Q3File, a112Q3Anchor) + ": `" + a112Q3Text + "`");
            a112.Mutations.Add(new FileMutation(a112Q3File, a112Q3Anchor, a112Q3Text + "\r\n" + a112Q3Anchor));
            a112.MustNotContain = new[] { "@LMLocal" };
            list.Add(a112);

            const string a113File = @"LMLocal\GettingStarted.txt";
            list.Add(ScVerbatim("A1.13", Explorer,
                "Read lines 1-12 of LMLocal\\GettingStarted.txt and return them verbatim.",
                Ev(a113File, "LM Local", 12), Ev(a113File, "Visual Studio", 12)));


            var a114 = Sc("A1.14", Explorer,
                "Find the file named exactly SseFrameParser.cs and read it (there is also a SseFrameParserFactory.cs in the same folder - do not pick the factory).",
                Ev(ReaderCodeDir + "SseFrameParser.cs", "FrameRecord"), Ev(ReaderCodeDir + "SseFrameParser.cs", "SseLineSplitter"));
            a114.MustNotContain = new[] { Ev(ReaderCodeDir + "SseFrameParserFactory.cs", "CreateForEndpoint") };
            list.Add(a114);

            list.Add(ScVerbatim("A1.15", Explorer,
                "Find and read these 4 files and return them verbatim: SessionLogWriter.cs, CompletionSummary.cs, SseFrameParser.cs, HistoryLedger.cs.",
                Ev(ReaderCodeDir + "SessionLogWriter.cs", "WriteSessionHeader"),
                Ev(ReaderCodeDir + "SessionLogWriter.cs", "internal const int MaxTitleLength = 120;"),
                Ev(ReaderCodeDir + "SessionLogWriter.cs", "sb.Append(\"...\");"),
                Ev(ReaderCodeDir + "CompletionSummary.cs", "FinishReasonCode"),
                Ev(ReaderCodeDir + "SseFrameParser.cs", "ParseFramesAsync"),
                Ev(ReaderCodeDir + "HistoryLedger.cs", "MergeLastExchangeAsync")));


            // The file does not exist: the answer must name no location. How the absence is worded is not checked.
            EvMissing(@"LMLocal\Infrastructure\Persistence\NoSuchSerializer.cs");
            var a116 = Sc("A1.16", Explorer,
                "Read LMLocal/Infrastructure/Persistence/NoSuchSerializer.cs. If the file does not exist, say so.");
            a116.ExpectNoLocations = true;
            a116.CheckRefusal = false;
            a116.SkipInventedPathCheck = true;
            list.Add(a116);

            var a117 = ScVerbatim("A1.17", Explorer,
                "Read the file at exact path LMLocal.Tests.E2E.VSIX\\Fixtures\\reader-truth-fixture.md and return it verbatim.",
                "## 1. First Section", "## Appendix A. References", "## 2. Second Section", "Tail-Anchor-Open");
            a117.MustAppearExactlyOnce = new[] { "Seed-One-Anchor", "Ref-Anchor-Alpha", "Tail-Anchor-Open" };
            list.Add(a117);

            var a118 = Sc("A1.18", Explorer,
                "Read the file at exact path LMLocal.Tests.E2E.VSIX\\Fixtures\\reader-truth-fixture.md. Verify whether these headings appear in this exact order: \"## 1. First Section\", \"## 2. Second Section\", \"## 3. Third Section\", \"## Appendix A. References\". Use the actual file contents and line positions. Do not infer or assume any heading or position that is not present in the retrieved content.",
                "## Appendix A. References");
            a118.MustContainInOrder = new[] { "## 1. First Section", "## Appendix A. References", "## 2. Second Section", "## 3. Third Section" };
            list.Add(a118);

            list.Add(Sc("A1.19", Explorer,
                "Read the entire file LMLocal.Tests.E2E.VSIX\\Fixtures\\reader-large-fixture.md and return it verbatim. Continue reading until the entire file has been retrieved; do not stop while has_more_results is true.",
                "Reader-Start", "Reader-End"));

            var a120 = ScVerbatim("A1.20", Explorer,
                "In LMLocal\\Application\\ModelsList\\ModelsListService.cs return VERBATIM only the two lines that contain ExceptionFormatter.Format. Return no other line of the file and do not mark the file as missing.",
                Ev(@"LMLocal\Application\ModelsList\ModelsListService.cs", "var errorResponse = new UnifiedListModelsResponse { Error = ExceptionFormatter.Format(ex) };"),
                Ev(@"LMLocal\Application\ModelsList\ModelsListService.cs", "return new UnifiedListModelsResponse { Error = ExceptionFormatter.Format(ex) };"));
            a120.MustNotContain = new[] { "Missing files" };
            list.Add(a120);

            var a121 = ScVerbatim("A1.21", Explorer,
                "Read the file at exact path LMLocal.Tests.E2E.VSIX\\Fixtures\\reader-ranges-fixture.md and return ONLY lines 11-15 and lines 30-39 VERBATIM, with each line carrying its actual 1-based line number. Do not include any other line of the file, do not substitute different ranges, and do not mark the file as missing.",
                "RANGE-ANCHOR-A1-OPEN", "RANGE-ANCHOR-A1-CLOSE", "RANGE-ANCHOR-A3-CLOSE", "RANGE-ANCHOR-APP-OPEN");
            a121.MustContainInOrder = new[] { "RANGE-ANCHOR-A1-OPEN", "RANGE-ANCHOR-A1-CLOSE", "RANGE-ANCHOR-A3-CLOSE", "RANGE-ANCHOR-APP-OPEN" };
            a121.MustNotContain = new[] { "RANGE-ANCHOR-A2-OPEN", "RANGE-ANCHOR-A2-CLOSE", "RANGE-ANCHOR-A3-OPEN", "RANGE-ANCHOR-APP-CLOSE", "RANGE-ANCHOR-TAIL-OPEN", "RANGE-ANCHOR-A1-OPEN-COPY", "RANGE-ANCHOR-A1-CLOSE-COPY", "# End of fixture" };
            list.Add(a121);

            var a122 = ScVerbatim("A1.22", Explorer,
                "Read the file at exact path LMLocal.Tests.E2E.VSIX\\Fixtures\\reader-symbols-fixture.md and return it verbatim, with each line carrying its 1-based line number. Preserve every character exactly — multiple spaces, tabs, single and double quotes, and every bracket. Do not normalize, drop, or rebalance any of them.",
                "SYM-SPACE-MID: two  spaces  between  words",
                "SYM-QUOTE-NESTED-SINGLE: 'outer \"inner\" value'",
                "SYM-QUOTE-AT-END: trailing quote\"",
                "SYM-PAREN-OPEN: function(",
                "SYM-PAREN-BALANCED: ((a, b), [c, d], {e: f})",
                "SYM-BRACKET-MIXED: {[(\"a\", {\"b\": [1,2]})]}",
                "SYM-EMPTY-AFTER: this line follows the empty line above",
                "SYM-END-TAIL: last line anchor");

            a122.MustNotContain = new[]
            {
                "SYM-EMPTY-UNWANTED",
                "//SECTION-SKIP",
                "///"
            };
            list.Add(a122);

            const string a123Cs = @"LMLocal\Core\Models\StreamChunk.cs";

            var a123 = ScVerbatim("A1.23", Explorer,
                "Read verbatim with exact line numbers (I will use them for precise line-range replacements):\n"
                + "1. LMLocal.Tests.E2E.VSIX\\Fixtures\\reader-ranges-fixture.md - lines 11-15\n"
                + "2. " + a123Cs + " - lines 1-6",
                "reader-ranges-fixture.md, lines 11-15",
                "StreamChunk.cs, lines 1-6",
                "RANGE-ANCHOR-A1-OPEN",
                "RANGE-ANCHOR-A1-CLOSE",
                LineAt(a123Cs, 6));
            a123.MustNotContain = new[] { "// Line", "RANGE-ANCHOR-A2-OPEN", "Missing files" };
            list.Add(a123);

            const string StreamCompletionResultFile = @"LMLocal\Core\Models\StreamCompletionResult.cs";
            list.Add(Sc("A1.24", Explorer,
                "Find the C# symbol ToolCallRecord and report its declaration file and line.",
                EvFile(StreamCompletionResultFile), LineOf(StreamCompletionResultFile, "internal class ToolCallRecord").ToString()));

            list.Add(Sc("A1.25", Explorer,
                "Find all references to ChatLogSerializer. Report file:line coordinates.",
                "ChatLogSerializer", Ev(@"LMLocal\Infrastructure\Persistence\ChatPersistenceService.cs", "ChatLogSerializer", fileNameOnly: true)));

            list.Add(Sc("A1.26", Explorer,
                "Inspect the type StreamCompletionResult and list its public members with file:line locations.",
                Ev(StreamCompletionResultFile, "ContentResponse"), Ev(StreamCompletionResultFile, "ToolCalls"),
                "StreamCompletionResult.cs:" + LineOf(StreamCompletionResultFile, "public IReadOnlyList<ToolCallRecord> ToolCalls { get; set; }"),
                "StreamCompletionResult.cs:" + LineOf(StreamCompletionResultFile, "public int? HttpStatusCode { get; set; }")));

            list.Add(Sc("A1.27", Explorer,
                "Find the JavaScript symbol lmInit and report its file and line. Use the semantic JavaScript tool.",
                Ev(@"LMLocal\Resources\js\app.js", "lmInit", fileNameOnly: true)));

            list.Add(NotFoundSc("A1.28", Explorer,
                "Find the symbol GeminiThoughtSignaturePolicyEnforcer.",
                Absent("LMLocal", "GeminiThoughtSignaturePolicyEnforcer")));

            var a129 = Sc("A1.29", Explorer,
                "Find all references to the ToolCalls property of the type ChatMessage (declared in LMLocal/Core/Models/ChatMessage.cs). Ignore the same-named ToolCalls property on other types such as Message and StreamCompletionResult. Retrieve ALL pages of references and state the total reference count.",
                "ToolCalls", Ev(@"LMLocal\Core\Models\ChatMessage.cs", "ToolCalls", fileNameOnly: true));
            a129.MustContainAny = new[] { new[] { Ev(@"LMLocal\Infrastructure\LlmApi\ApiRequestBuilder.cs", "ToolCalls", fileNameOnly: true), "ApiRequestBuilder" } };
            list.Add(a129);

            // A1.30 was judged by words ("reference", the method name). Since 2026-11-21 it is judged by the place: either the line that takes the
            // final assistant message out of the fragment or the line that puts it into the new list unchanged; both decide the answer.
            var a130 = Sc("A1.30", Explorer,
                "Inspect ConsolidateLastExchangeAsync and determine whether the ToolCalls of the final assistant message in the consolidated fragment are copied by reference or reconstructed. Read the actual source before answering and quote the deciding lines with file:line.");
            a130.ExpectedLocationsAny = new[]
            {
                new[]
                {
                    ChatHistoryManagerFile + ":" + LineOf(ChatHistoryManagerFile, "var finalAssistant = fragment[fragment.Count - 1];"),
                    ChatHistoryManagerFile + ":" + LineOf(ChatHistoryManagerFile, "finalAssistant")
                }
            };
            // A marker over the whole method or file must not count as "the deciding line".
            a130.MaxCoverLines = 25;
            list.Add(a130);

            list.Add(Sc("A1.31", Explorer,
                "Find the C# method AddAssistantMessage in LMLocal/Application/Chat/ChatHistoryManager.cs. It has an interface declaration and two concrete overloads. Select the concrete implementation overload that accepts IReadOnlyList<ToolCallRecord> and return its declaration line together with its full implementation body.",
                "AddAssistantMessage", Ev(ChatHistoryManagerFile, "toolCallObjects"), Ev(ChatHistoryManagerFile, "normalizedArguments")));

            // ---- Added 2026-11-21 from the real requests of 2026-11-20 (Docs\subagent-benchmark-scenarios-plan.md, section 6.3). ----
            // Judged by locations against ground truth from the repository, not by words of the answer.

            // A1.32 - a batch of six items of different types in one request (64% of the real explorer requests had four or more items).
            // Items 1-2 are checked by the lines the runner expands from the markers; items 3-5 by locations; item 6 must not
            // produce a location: CheckForeignLocations rejects every location outside the expected ones.
            const string a132Factory = @"LMLocal\Application\Tool\ToolMessageFactory.cs";
            var a132Decl = LocationsOf("internal static class ToolHistoryPruner", ".cs", @"LMLocal\Application");
            var a132Calls = LocationsOf("ToolHistoryPruner.Prune(", ".cs", "LMLocal");
            var a132Text = LocationsOf("PrunedKey", ".cs", @"LMLocal\Application\Chat");
            var a132 = ScVerbatim("A1.32", Explorer,
                "Answer all six items in one report:\n"
                + "1. Read " + a132Factory + " and return it verbatim.\n"
                + "2. Read LMLocal.Tests.E2E.VSIX\\Fixtures\\reader-ranges-fixture.md lines 11-15 and return them verbatim with their line numbers.\n"
                + "3. Where is the class ToolHistoryPruner declared? Report file:line with the declaration line text.\n"
                + "4. Search only in project LMLocal: which places call ToolHistoryPruner.Prune? Report file:line with the matched line text for every call site.\n"
                + "5. Search the folder LMLocal\\Application\\Chat for the exact text PrunedKey and report every matched line with file:line and its text.\n"
                + $"6. Search the entire solution for the literal text bench-{guid}-q6 and report every hit, or state clearly that there are none.",
                Ev(a132Factory, "public static ChatMessage CreateFromToolResult(ToolResultMessage toolResult)"),
                "reader-ranges-fixture.md, lines 11-15",
                "RANGE-ANCHOR-A1-OPEN",
                "RANGE-ANCHOR-A1-CLOSE");
            a132.ExpectedLocations = a132Decl.Concat(a132Calls).Concat(a132Text).Distinct().ToArray();
            // Items 1-2 are whole files or ranges the runner expands as "File: path, lines a-b": those two files may appear with any lines.
            a132.AllowedLocations = new[] { a132Factory, @"LMLocal.Tests.E2E.VSIX\Fixtures\reader-ranges-fixture.md" };
            a132.CheckForeignLocations = true;
            // Items 3-5 are single matched lines: a range over a whole file must not cover them.
            a132.MaxCoverLines = 3;
            a132.MustNotContain = new[] { "@LMLocal" };
            list.Add(a132);

            // A1.33 - verify a claim that is true (22% of the real items were checks of a claim about the code). The call line is the one expected location.
            const string a133File = @"LMLocal\Application\ChatSessionStream\StreamingRoundService.cs";
            var a133 = Sc("A1.33", Explorer,
                "Verify that ToolMessageFactory.CreateFromToolResult is called from StreamingRoundService. Quote the call line with file:line.");
            a133.ExpectedLocations = new[] { a133File + ":" + LineOf(a133File, "pendingToolMessages.Add(ToolMessageFactory.CreateFromToolResult(toolResult));") };
            a133.MustNotContain = new[] { "@LMLocal" };
            a133.MaxCoverLines = 3;
            list.Add(a133);

            // A1.34 - verify a claim that is false: SubAgentsService.cs never mentions ToolHistoryPruner (the pruner is used by HistoryCompactor only).
            // Absent throws when the folder starts to mention it. No location in SubAgentsService.cs may be named; the wording of the denial is not checked.
            const string a134File = @"LMLocal\Application\SubAgents\SubAgentsService.cs";
            Absent(@"LMLocal\Application\SubAgents", "ToolHistoryPruner");
            var a134 = Sc("A1.34", Explorer,
                "Verify that SubAgentsService.cs uses ToolHistoryPruner. Quote the line with file:line, or say clearly that it does not.");
            a134.ForbiddenLocationPaths = new[] { a134File };
            a134.CheckRefusal = false;
            list.Add(a134);

            // A1.35 - a stale path (the 2026-11-20 logs had 24 failed read_file_lines, 19 of them for a file that exists in another folder).
            // The file lives in the ChatSession subfolder; the lines come from the real file, expanded by the runner.
            const string a135Stale = @"LMLocal\Application\ChatSessionState.cs";
            const string a135Real = @"LMLocal\Application\ChatSession\ChatSessionState.cs";
            EvMissing(a135Stale);
            var a135 = ScVerbatim("A1.35", Explorer,
                "Read " + a135Stale + " and return its first 10 lines with their line numbers.",
                "ChatSessionState.cs, lines 1-10",
                LineAt(a135Real, 1),
                LineAt(a135Real, 6));
            list.Add(a135);

            return list;
        }

        /// <summary>reviewer_subagent (A4.x) and architect_subagent (A6.x), on LMLocal.sln itself. Its list is the method right below.</summary>
        [TestMethod]
        [DynamicData(nameof(GetIterations))]
        [Ignore("Disable to run benchmark")]
        public async Task Run_ReviewerArchitect_Benchmark(int iteration)
        {
            await RunSolutionBenchmarkAsync(BuildReviewerArchitectScenarios(), iteration, "reviewer-architect-benchmark-report-");
        }

        /// <summary>
        /// Builds the reviewer_subagent (A4.x) and architect_subagent (A6.x) list. They answer with a structured review or a design,
        /// not with locations, so they run apart from the explorer list.
        /// </summary>
        internal static List<Scenario> BuildReviewerArchitectScenarios()
        {
            var list = new List<Scenario>();

            // The deliverable is a frozen fixture, so every code claim can be checked with read_file_lines / search_file_content.
            // Build and test results cannot be checked with the reviewer tools, hence PARTIAL is a valid outcome.
            var a41 = Sc("A4.1", Reviewer,
                "Review the following completed deliverable and produce your structured review ending with an exact final 'Status: ...' line. " +
                "The change is a small refactoring of ToolCallLedger (" + ReviewCodeDir + "ToolCallLedger.cs): the ToolCalls copying logic of ConsolidateLastExchange was extracted into the private helper CopyToolCalls. " +
                "A new unit test ConsolidateLastExchange_KeepsToolCallsOfFinalAssistantByReference (" + ReviewCodeDir + "ToolCallLedgerTests.cs) covers the copied-by-reference behavior. " +
                "The build passes and all existing unit tests pass. The user requested exactly this refactoring and it is complete; no further phases are planned. " +
                "The change is already committed, so get_changes_diff is expected to show no pending changes; that alone is not a defect of the deliverable. " +
                "Verify the code with read_file_lines and search_file_content. Review it adversarially.",
                "Status:", "CopyToolCalls");
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
                + "Verified facts (given as premises; do not re-check them in the code):\n"
                + "- SearchLedger (LMLocal\\Application\\SubAgents\\ReadRefs\\SearchLedger.cs) stores (file, line, text) records. ReadRefExpander replaces an @path:line marker in a subagent's final answer with 'path:line: `text`' only for lines recorded in SearchLedger.\n"
                + "- SubAgentRunEvidence (LMLocal\\Application\\SubAgents\\ReadRefs\\SubAgentRunEvidence.cs) records search_file_content results into SearchLedger through SearchResultRecorder.TryRecord. No other tool result is recorded there.\n"
                + "- get_symbol_info and get_symbol_info_js results are not recorded anywhere.\n"
                + "- The code_explorer_subagent prompt contains no instructions about markers.\n"
                + "Deliverable (plan text added to Docs\\explorer-search-quality-plan.md):\n"
                + "Step 1. No code change is needed: references returned by get_symbol_info are already recorded in SearchLedger, so the explorer's @path:line markers expand today.\n"
                + "Step 2. Add a short section about markers to the code_explorer_subagent prompt.\n"
                + "Step 3. Measure the share of explorer answers with expanded markers.\n"
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

            var a61 = Sc("A6.1", Architect,
                @"Requirement: show the number of pending (uncommitted) file changes in the Visual Studio status bar and keep it current whenever the snapshot changes. Single process, one consumer, no persistence or retries needed.
            Verified context:
            - ISnapshotManager (LMLocal\Infrastructure\Tooling\BuiltInVs\Snapshot\SnapshotManager.cs) exposes event Func<IReadOnlyList<SnapshotFileChange>, Task> SnapshotChangedAsync, raised after every snapshot change, commit, rollback and reset.
            - WebViewBridge (LMLocal\Infrastructure\WebView\WebViewBridge.cs) already subscribes to it in its constructor: _snapshotManager.SnapshotChangedAsync += OnSnapshotChangedAsync.
            - ISnapshotManager is resolved from DI.
            Choose the architecture for this change.",
                "SnapshotChangedAsync");
            // The architect prompt has no decision status: the answer is three sections and verdicts are forbidden.
            a61.MustContainAny = new[] { new[] { "As-Is" }, new[] { "Recommendation" } };
            a61.MustNotContain = new[] { "Status: APPROVED", "Status: REJECTED" };
            // The architect may propose new files, which do not exist yet.
            a61.SkipInventedPathCheck = true;
            list.Add(a61);

            return list;
        }

        /// <summary>context_retriever_subagent (A8.x), on the knowledge fixture. Its list is the method right below.</summary>
        [TestMethod]
        [DynamicData(nameof(GetIterations))]
        [Ignore("Disable to run benchmark")]
        public async Task Run_Knowledge_Benchmark(int iteration)
        {
            // iteration only gives GetIterations its rows: each row is a separate run with its own VS instance and report.
            var fixturePath = GetKnowledgeFixtureSolutionPath();
            AssertKnowledgeBaseReachesFixtureDocs(Path.GetDirectoryName(fixturePath));

            await RunFixtureBenchmarkAsync(fixturePath, BuildKnowledgeScenarios(), "knowledge-benchmark-report-");
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

            // A8.2 (added 2026-11-21) - the most common real retriever request (8 of 9 on 2026-11-20): find the active plan and the
            // conventions of a task. The two documents are the expected locations (file level); the answer wording is not checked.
            // The distractor documents of A8.1 are a soft finding only.
            var a82 = Sc("A8.2", Retriever,
                "Find the active implementation plan for the chat export feature and the project conventions that apply to it. Report the source documents.");
            a82.ExpectedLocations = new[] { @"docs\chat-export-plan.md", @"docs\project-conventions.md" };
            a82.SoftMustNotContain = new[] { "set-file-project-status-batch", "create-file-open-documents" };
            list.Add(a82);

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
                            run.Recoveries?.Any(r => r.IndexOf("repeated tool call loop", StringComparison.Ordinal) >= 0),
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
                            run.Recoveries?.Any(r => r.IndexOf("unresolved ref markers", StringComparison.Ordinal) >= 0) == true,
                            "No unresolved ref marker recovery in run '" + run.RunId + "'. "
                            + "Either recovery did not fire, or the model's first answer had no marker "
                            + "(probe inconclusive - re-run before suspecting the feature).");

                        // The second answer may still carry the marker: there is one attempt, after it the answer
                        // is returned as is, so UnresolvedRefCount is only logged.
                        Assert.IsTrue(run.Success, "A recovered run must complete, not fail: " + (run.Error ?? "<no error>"));
                        Assert.IsGreaterThanOrEqualTo(2, run.Rounds, "The nudge needs its own round; rounds=" + run.Rounds);
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
        // Round limit recovery
        //
        // Guards the round-limit path: tool calls in the last round are not executed; one extra round is sent
        // without tools and the model answers from the results it already has, instead of the run failing.
        // ---------------------------------------------------------------------

        /// <summary>
        /// The probe runs with maxRounds 2 (IPC override): round 1 gathers a real result, round 2 is the last one and
        /// its tool call hits the limit. The model and every other setting come from the explorer entry in subagents.json.
        /// </summary>
        private const int RoundLimitProbeMaxRounds = 2;

        /// <summary>
        /// Holds one value per search key. The values are written only there, never in this file, so the answer
        /// can contain a value only if the search that returns it was executed.
        /// </summary>
        private const string RoundLimitFixture = @"LMLocal.Tests.E2E.VSIX\Fixtures\round-limit-fixture.md";

        private const string RoundLimitAlphaKey = "RoundLimit-Alpha";
        private const string RoundLimitBetaKey = "RoundLimit-Beta";

        /// <summary>
        /// Two calls, one per response: the Alpha search runs in round 1; the Beta search lands in the last round and
        /// is not executed. The answer must then carry the Alpha value (partial result) and not the Beta value.
        /// </summary>
        private const string RoundLimitProbePrompt =
            "You are being instrumented. Do exactly this, one tool call per response, waiting for each result:\n" +
            "1. Call search_file_content with text \"" + RoundLimitAlphaKey + "\" and path \"" + RoundLimitFixture + "\".\n" +
            "2. Then call search_file_content with text \"" + RoundLimitBetaKey + "\" and path \"" + RoundLimitFixture + "\".\n" +
            "Then answer: the first line is ROUND_LIMIT_PROBE_DONE; then, for each search, its matched line, " +
            "or 'not checked' for a search you could not run.";

        /// <summary>Value after "<paramref name="key"/>:" in the fixture.</summary>
        private static string ReadRoundLimitValue(string key)
        {
            var path = Path.Combine(Path.GetDirectoryName(GetSolutionPath()), RoundLimitFixture);
            var line = File.ReadAllLines(path).Single(l => l.IndexOf(key + ":", StringComparison.Ordinal) >= 0);
            var value = line.Substring(line.IndexOf(key + ":", StringComparison.Ordinal) + key.Length + 1).Trim();
            Assert.IsFalse(string.IsNullOrEmpty(value), "No value for " + key + " in " + RoundLimitFixture);
            return value;
        }

        [TestMethod]
        [Ignore("Disable to run benchmark")]
        public async Task Run_RoundLimitRecovery_ToolCallInLastRound_AnswersWithoutTools()
        {
            var alphaValue = ReadRoundLimitValue(RoundLimitAlphaKey);
            var betaValue = ReadRoundLimitValue(RoundLimitBetaKey);

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

                        // Empty model part: keep the model from subagents.json; only maxRounds is overridden.
                        var command = "RunSubAgent|" + Explorer + "|" + Uri.EscapeDataString(RoundLimitProbePrompt)
                            + "||" + RoundLimitProbeMaxRounds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        var json = await client.SendCommandAsync(command, TimeSpan.FromMinutes(5), cts.Token);

                        var run = JsonConvert.DeserializeObject<RunResponseDto>(json);
                        Assert.IsNotNull(run, "RunSubAgent returned no parsable response: " + json);

                        TestContext.WriteLine("round limit probe: success=" + run.Success
                            + " rounds=" + run.Rounds
                            + " runId=" + run.RunId
                            + " recoveries=" + (run.Recoveries == null ? "<none>" : string.Join(" | ", run.Recoveries))
                            + " error=" + (run.Error ?? "<none>")
                            + " content=" + (run.Content ?? "<none>"));

                        // Proven by the run's recovery note: if the model stops calling tools before the last round,
                        // the limit is never reached and the guard does not run.
                        Assert.IsTrue(
                            run.Recoveries?.Any(r => r.IndexOf("reached the round limit (" + RoundLimitProbeMaxRounds + ")", StringComparison.Ordinal) >= 0) == true,
                            "No round limit recovery in run '" + run.RunId + "'. "
                            + "Either recovery did not fire, or the model answered without a tool call "
                            + "(probe inconclusive - re-run before suspecting the feature).");

                        Assert.IsTrue(run.Success, "The run must end with an answer, not fail: " + (run.Error ?? "<no error>"));
                        Assert.DoesNotMatchRegex(
                            new Regex("exceeded maximum rounds"),
                            run.Error ?? string.Empty,
                            "The hard stop is only for tool calls in the extra round.");
                        Assert.AreEqual(RoundLimitProbeMaxRounds + 1, run.Rounds,
                            "Expected the configured rounds plus one extra round without tools.");
                        Assert.Contains("ROUND_LIMIT_PROBE_DONE", run.Content ?? string.Empty,
                            "The extra round must end with the text answer.");
                        // Partial result: the round 1 search was executed, so its value must reach the answer.
                        Assert.Contains(alphaValue, run.Content ?? string.Empty,
                            "The answer must report the result gathered in round 1 (" + RoundLimitAlphaKey + ").");
                        // The last-round search was not executed, so its value cannot be known.
                        Assert.DoesNotContain(betaValue, run.Content ?? string.Empty,
                            "The last-round call must not run, so " + RoundLimitBetaKey + " cannot be in the answer.");
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

        /// <summary>build_subagent (B1.x), on the build fixture. Its list is the method right below.</summary>
        [TestMethod]
        [DynamicData(nameof(GetIterations))]
        [Ignore("Disable to run benchmark")]
        public async Task Run_Build_Benchmark(int iteration)
        {
            // iteration only gives GetIterations its rows: each row is a separate run with its own VS instance and report.
            AssertWriteToolsEnabled();

            var fixturePath = GetBuildFixtureSolutionPath();

            // Outputs of an earlier run must not let the first scenario pass without a real build.
            CleanBuildFixtureOutputs(Path.GetDirectoryName(fixturePath));

            await RunFixtureBenchmarkAsync(fixturePath, BuildBuildScenarios(), "build-benchmark-report-");
        }

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

            // B1.2 - a compile error (added 2026-11-21): on 2026-11-20 two of 42 real build runs returned an empty answer after compile errors.
            // The ground truth is the place of the error: the compiler reports it as Calculator.cs(line,col). The wording of the answer is not checked,
            // so an empty answer fails because it names no location.
            const string calc = @"BuildFixture.Lib\Calculator.cs";
            var b12 = Sc("B1.2", Builder,
                "Build the solution and report every compiler error verbatim with file and line.");
            b12.Mutations.Add(new FileMutation(calc, "return a + b;", "return a + undefinedName;"));
            b12.ExpectedLocations = new[] { calc + ":" + FixtureLineOf(calc, "return a + b;") };
            list.Add(b12);

            // B1.3 - failing tests: with Add returning a - b both Add tests fail and the Describe test passes. The test names are identifiers
            // copied from the test output (not phrasing); a listed passing test is only a soft finding.
            var b13 = Sc("B1.3", Builder,
                "Build the solution and run all unit tests in the project BuildFixture.Tests. Report every failing test with its full name and error message.",
                "Add_ReturnsSum", "Add_WithNegative_ReturnsSum");
            b13.Mutations.Add(new FileMutation(calc, "return a + b;", "return a - b;"));
            b13.MustNotContain = new[] { "error CS" };
            b13.SoftMustNotContain = new[] { "Describe_PrefixesValue" };
            list.Add(b13);

            // B1.4 - tests with a filter (21 of 42 real build runs): FormatTests.cs says a filter by CalculatorTests runs 2 of the 3 tests.
            // The answer format of build_subagent is not fixed, so the counts are only a soft check; the hard checks are a successful build and no compile error.
            var b14 = Sc("B1.4", Builder,
                "Build the solution and run only the tests of the project BuildFixture.Tests whose name contains CalculatorTests. Report how many tests ran and how many passed.");
            b14.MustNotContain = new[] { "error CS" };
            b14.SoftMustContainAny = new[]
            {
                new[] { "Total: 2", "Total tests: 2", "2 total", "2 tests", "Passed: 2", "2 passed", "passed 2", "all 2", "Total = 2" }
            };
            list.Add(b14);

            // B1.5 - a compile error when tests were also asked for (added 2026-11-21): both empty answers of 2026-11-20 (08:45:33, 08:45:39)
            // came from "build + run tests with a filter" tasks with a failed build; gemma ended with a 1-token empty reply right after the failed
            // build_solution. The same failed build with a build-only task (B1.2) was answered. Scored like B1.2, by the place of the error.
            var b15 = Sc("B1.5", Builder,
                "Build the solution and then run the tests of the project BuildFixture.Tests whose name contains CalculatorTests. Report the build result with every compiler error (file and line) and the test results.");
            b15.Mutations.Add(new FileMutation(calc, "return a + b;", "return a + undefinedName;"));
            b15.ExpectedLocations = new[] { calc + ":" + FixtureLineOf(calc, "return a + b;") };
            list.Add(b15);

            return list;
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

        /// <summary>
        /// Opens LMLocal.sln in a new Experimental VS instance and runs <paramref name="scenarios"/> on it, one after another.
        /// The report is saved next to LMLocal.sln as <paramref name="reportPrefix"/> + a time stamp.
        /// </summary>
        private async Task RunSolutionBenchmarkAsync(List<Scenario> scenarios, int iteration, string reportPrefix)
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
                        foreach (var scenario in scenarios)
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

                        SaveReport(results, root, reportPrefix);
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

            // Location scoring: independent of the "not found" wording above, the answer's path:line locations are compared with the
            // ground truth taken from the repository.
            EvaluateLocations(s, content, violations);

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
        // A1.8 tests walking the cursor, not reading a long list: a narrow term (91 lines, last asset-290 on line 1167) in small
        // pages gives 4 pages. "Include" (582 lines, max_results 500) put every line into the context, and on 2026-11-21 the model
        // listed them all in its answer until the 240 s timeout.
        internal const string A108Text = "asset-2";
        internal const int A108PageSize = 25;

        internal const string ReaderCodeDir = @"LMLocal.Tests.E2E.VSIX\Fixtures\ReaderCode\";
        internal const string ReviewCodeDir = @"LMLocal.Tests.E2E.VSIX\Fixtures\ReviewCode\";
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

        // ---------------------------------------------------------------------
        // Location scoring (path:line against ground truth; no words are read)
        // ---------------------------------------------------------------------

        /// <summary>One place named by an answer: a file and a line range (a single line has Start == End; no line = 0).</summary>
        internal sealed class AnswerLocation
        {
            public string Path;
            public int Start;
            public int End;

            public override string ToString()
            {
                return Start == 0 ? Path : Start == End ? Path + ":" + Start : Path + ":" + Start + "-" + End;
            }
        }

        // path:line, path:a-b, path:12,40,52 (also with a leading '@') and the compiler form path(line[,col]).
        private static readonly Regex LocationRegex = new Regex(
            @"(?<![\w.\\/\-])@?(?<path>(?:[A-Za-z]:[\\/])?(?:[\w.\-]+[\\/])*[\w.\-]+\.[A-Za-z0-9]{1,6})"
            + @"(?::(?<a>\d+)(?:-(?<b>\d+))?(?<more>(?:,\d+)*)|\((?<pl>\d+)(?:,\d+)?\))(?!\d)",
            RegexOptions.Compiled);

        // "File: path, lines a-b": a range marker expanded by the runner (FileContentRenderer.AppendRefContentBlock).
        private static readonly Regex RangeHeaderRegex = new Regex(
            @"^\s*File:\s+(?<path>\S.*?\.[A-Za-z0-9]{1,6}),\s+lines\s+(?<a>\d+)-(?<b>\d+)\s*$",
            RegexOptions.Compiled);

        // The runner fences an expanded range with four backticks (a model writes three).
        private static readonly Regex RunnerFenceRegex = new Regex(@"^\s*`{4,}", RegexOptions.Compiled);

        /// <summary>
        /// Extracts the locations an answer names. When a line starts with a location followed by ": `text`" (a reference
        /// expanded by the runner), the quoted text is ignored, so a "name.cs:12" inside the quoted source line is not
        /// taken for a location.
        /// </summary>
        internal static List<AnswerLocation> ExtractLocations(string content)
        {
            var result = new List<AnswerLocation>();
            if (string.IsNullOrEmpty(content)) return result;

            var inRunnerBlock = false;
            foreach (var line in content.Replace("\r\n", "\n").Split('\n'))
            {
                // The block the runner puts under a "File: path, lines a-b" header (a fence of four backticks) is source text:
                // a "name.cs:12" inside it is code, not a location.
                if (RunnerFenceRegex.IsMatch(line))
                {
                    inRunnerBlock = !inRunnerBlock;
                    continue;
                }

                if (inRunnerBlock) continue;

                // A range marker expanded by the runner: "File: path, lines a-b" (the lines read by read_file_lines).
                var header = RangeHeaderRegex.Match(line);
                if (header.Success)
                {
                    var a0 = int.Parse(header.Groups["a"].Value);
                    var b0 = int.Parse(header.Groups["b"].Value);
                    result.Add(new AnswerLocation
                    {
                        Path = NormalizeLocationPath(header.Groups["path"].Value),
                        Start = Math.Min(a0, b0),
                        End = Math.Max(a0, b0)
                    });
                    continue;
                }

                var matches = LocationRegex.Matches(line).Cast<Match>().ToList();
                if (matches.Count == 0) continue;

                var first = matches[0];
                var quoted = Regex.IsMatch(line.Substring(first.Index + first.Length), @"^\s*:\s*`");
                foreach (var m in matches)
                {
                    if (quoted && m != first) continue;

                    var path = NormalizeLocationPath(m.Groups["path"].Value);
                    if (m.Groups["pl"].Success)
                    {
                        var l = int.Parse(m.Groups["pl"].Value);
                        result.Add(new AnswerLocation { Path = path, Start = l, End = l });
                        continue;
                    }

                    var a = int.Parse(m.Groups["a"].Value);
                    var b = m.Groups["b"].Success ? int.Parse(m.Groups["b"].Value) : a;
                    result.Add(new AnswerLocation { Path = path, Start = Math.Min(a, b), End = Math.Max(a, b) });
                    foreach (var extra in m.Groups["more"].Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var e = int.Parse(extra);
                        result.Add(new AnswerLocation { Path = path, Start = e, End = e });
                    }
                }
            }

            return result;
        }

        private static string NormalizeLocationPath(string path)
        {
            var p = (path ?? string.Empty).Trim().Replace('/', '\\');
            while (p.StartsWith(".\\", StringComparison.Ordinal)) p = p.Substring(2);
            return p;
        }

        /// <summary>The two paths name the same file: equal, or one is a trailing part of the other (a path cut to its last folders).</summary>
        internal static bool SamePath(string a, string b)
        {
            a = NormalizeLocationPath(a);
            b = NormalizeLocationPath(b);
            if (a.Length == 0 || b.Length == 0) return false;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
                || a.EndsWith("\\" + b, StringComparison.OrdinalIgnoreCase)
                || b.EndsWith("\\" + a, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Splits "relative\path:line" into the path and the line (0 when the entry names only a file).</summary>
        internal static void SplitExpectedLocation(string entry, out string path, out int line)
        {
            var m = Regex.Match(entry ?? string.Empty, @"^(?<p>.*\.[A-Za-z0-9]{1,6}):(?<l>\d+)$");
            if (m.Success)
            {
                path = m.Groups["p"].Value;
                line = int.Parse(m.Groups["l"].Value);
            }
            else
            {
                path = entry ?? string.Empty;
                line = 0;
            }
        }

        private static bool Covers(AnswerLocation found, string path, int line, int maxCoverLines = 0)
        {
            if (!SamePath(found.Path, path)) return false;
            if (line == 0) return true;
            if (maxCoverLines > 0 && found.End - found.Start + 1 > maxCoverLines) return false;
            return found.Start <= line && line <= found.End;
        }

        /// <summary>The last two segments of a path ("folder\file.cs"), used to recognise a file named without its full path.</summary>
        private static string LastSegments(string path)
        {
            var parts = NormalizeLocationPath(path).Split('\\');
            return parts.Length <= 2 ? string.Join("\\", parts) : string.Join("\\", parts.Skip(parts.Length - 2));
        }

        private static void EvaluateLocations(Scenario s, string content, List<Violation> violations)
        {
            var any = s.ExpectedLocations.Length > 0 || s.ExpectedLocationsAny.Length > 0 || s.ForbiddenLocationPaths.Length > 0 || s.ExpectNoLocations || s.CheckForeignLocations;
            if (!any) return;

            var found = ExtractLocations(content);
            var flat = (content ?? string.Empty).Replace('/', '\\');

            foreach (var entry in s.ExpectedLocations)
            {
                string path;
                int line;
                SplitExpectedLocation(entry, out path, out line);

                var ok = found.Any(f => Covers(f, path, line, s.MaxCoverLines));
                if (!ok && line == 0)
                {
                    // A file named without a line: its path (at least the last two segments) or its file name is in the answer.
                    ok = flat.IndexOf(LastSegments(path), StringComparison.OrdinalIgnoreCase) >= 0
                        || flat.IndexOf(Path.GetFileName(NormalizeLocationPath(path)), StringComparison.OrdinalIgnoreCase) >= 0;
                }

                if (!ok)
                {
                    violations.Add(new Violation("missing_location: " + entry, true));
                }
            }

            foreach (var group in s.ExpectedLocationsAny)
            {
                var ok = group.Any(entry =>
                {
                    string path;
                    int line;
                    SplitExpectedLocation(entry, out path, out line);
                    return found.Any(f => Covers(f, path, line, s.MaxCoverLines));
                });

                if (!ok)
                {
                    violations.Add(new Violation("missing_location: one of " + string.Join(" | ", group), true));
                }
            }

            if (s.ExpectNoLocations)
            {
                foreach (var f in found.Take(3))
                {
                    violations.Add(new Violation("unexpected_location: " + f, true));
                }
            }

            foreach (var forbidden in s.ForbiddenLocationPaths)
            {
                foreach (var f in found.Where(x => SamePath(x.Path, forbidden)).Take(3))
                {
                    violations.Add(new Violation("forbidden_location: " + f, true));
                }
            }

            if (s.CheckForeignLocations)
            {
                var allowed = s.ExpectedLocations.Concat(s.ExpectedLocationsAny.SelectMany(g => g)).Concat(s.AllowedLocations).ToList();
                foreach (var f in found)
                {
                    var known = allowed.Any(entry =>
                    {
                        string path;
                        int line;
                        SplitExpectedLocation(entry, out path, out line);
                        return Covers(f, path, line, s.MaxCoverLines);
                    });
                    if (!known)
                    {
                        violations.Add(new Violation("foreign_location: " + f, true));
                    }
                }
            }
        }

        /// <summary>
        /// "relative\path:line" of every line, in the solution-relative files or folders, that contains <paramref name="text"/>
        /// (case-insensitive substring, like search_file_content). <paramref name="extension"/> limits a folder scan (e.g. ".cs").
        /// Read at run time; throws when nothing matches, so a stale expectation fails before a model run.
        /// </summary>
        internal static List<string> LocationsOf(string text, string extension, params string[] relativePaths)
        {
            var root = Path.GetDirectoryName(GetSolutionPath());
            var result = new List<string>();
            foreach (var relative in relativePaths)
            {
                var full = Path.GetFullPath(Path.Combine(root, relative));
                var files = File.Exists(full)
                    ? new List<string> { full }
                    : Directory.EnumerateFiles(full, "*" + (extension ?? string.Empty), SearchOption.AllDirectories).ToList();

                foreach (var file in files)
                {
                    var sep = Path.DirectorySeparatorChar;
                    if (file.Contains(sep + "bin" + sep) || file.Contains(sep + "obj" + sep)) continue;

                    var lines = File.ReadAllLines(file);
                    for (var i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            result.Add(file.Substring(root.TrimEnd(sep).Length + 1) + ":" + (i + 1));
                        }
                    }
                }
            }

            if (result.Count == 0)
                throw new InvalidOperationException("'" + text + "' not found in " + string.Join(", ", relativePaths));
            return result;
        }

        /// <summary>1-based number of the first line of a build-fixture file (relative to the fixture folder) whose trimmed text equals <paramref name="trimmedLine"/>.</summary>
        internal static int FixtureLineOf(string fixtureRelativePath, string trimmedLine)
        {
            var root = Path.GetDirectoryName(GetBuildFixtureSolutionPath());
            var lines = File.ReadAllLines(Path.Combine(root, fixtureRelativePath));
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() == trimmedLine) return i + 1;
            }

            throw new InvalidOperationException("'" + trimmedLine + "' not found in " + fixtureRelativePath);
        }

        /// <summary>Returns <paramref name="relativePath"/> after checking that no such file exists (a scenario about a stale path).</summary>
        internal static string EvMissing(string relativePath)
        {
            var root = Path.GetDirectoryName(GetSolutionPath());
            if (File.Exists(Path.Combine(root, relativePath)))
                throw new InvalidOperationException("Benchmark expects a missing file, but it exists now: " + relativePath);
            return relativePath;
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
                if (process?.HasExited == false)
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
