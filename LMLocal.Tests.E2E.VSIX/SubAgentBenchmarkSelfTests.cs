using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static LMLocal.Tests.E2E.VSIX.SubAgentBenchmarkTests;

namespace LMLocal.Tests.E2E.VSIX
{
    /// <summary>
    /// Self-checks of the subagent benchmark data: fixtures, expected lines, file mutations.
    /// No model and no VS instance: they run in milliseconds and fail before a benchmark run would.
    /// </summary>
    [TestClass, TestCategory("SubAgentBenchmark.SelfCheck")]
    public class SubAgentBenchmarkSelfTests
    {
        /// <summary>
        /// Guards the fixture helpers that build every A.5 expectation. The seeds are verbatim
        /// literals, so their line endings come from the checkout (CRLF in this repository) while
        /// the patterns are written with '\n'. This test runs in milliseconds and fails first if
        /// that normalization ever regresses, before any run that needs LM Studio.
        /// </summary>
        [TestMethod]
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

        /// <summary>
        /// Build benchmark mutations: applied before a scenario, reverted byte-for-byte after it, and a Find text
        /// that is not in the file aborts without leaving the file changed. Runs without VS or LM Studio.
        /// </summary>
        [TestMethod]
        public void BuildFixtureMutations_ApplyAndRevertExactly()
        {
            var root = Path.Combine(Path.GetTempPath(), "lmlocal-mutation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var path = Path.Combine(root, "Calc.cs");
                var original = new UTF8Encoding(true).GetBytes("class C\r\n{\r\n    int Add(int a, int b) { return a + b; }\r\n}\r\n");
                File.WriteAllBytes(path, original);

                var ok = new Scenario { Mutations = { new FileMutation("Calc.cs", "return a + b;", "return a + c;") } };
                var saved = ApplyMutations(root, ok);
                Assert.Contains("return a + c;", File.ReadAllText(path));
                RevertMutations(saved);
                CollectionAssert.AreEqual(original, File.ReadAllBytes(path), "revert must restore the exact bytes (BOM, CRLF)");

                var bad = new Scenario
                {
                    Mutations =
                    {
                        new FileMutation("Calc.cs", "return a + b;", "return a - b;"),
                        new FileMutation("Calc.cs", "missing text", "x")
                    }
                };
                var thrown = false;
                try
                {
                    ApplyMutations(root, bad);
                }
                catch (InvalidOperationException)
                {
                    thrown = true;
                }

                Assert.IsTrue(thrown, "a Find text that is not in the file must abort the scenario");
                CollectionAssert.AreEqual(original, File.ReadAllBytes(path), "an aborted mutation must leave the file unchanged");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>
        /// A1.8 tests the cursor only while its term fills more than two pages of the max_results its task names,
        /// so the last match is on page 3 or later. Runs without VS or LM Studio.
        /// </summary>
        [TestMethod]
        public void ExplorerPagingScenario_NeedsThreePages()
        {
            var count = MatchingLines(A108File, A108Text).Count;
            Assert.IsGreaterThan(2 * A108PageSize, count,
                $"A1.8: '{A108Text}' has {count} matching lines in {A108File}; it must exceed {2 * A108PageSize} so the last match is on page 3 or later.");
        }

        /// <summary>
        /// Every expectation of the main benchmark that comes from live code is checked while the list is built
        /// (LineOf, LineAt, MatchingLines, Ev, EvFile, Absent throw on drift). Building the list here catches a
        /// refactoring that moved or removed that code without a model run; the planted mutations must still find
        /// their anchors too.
        /// </summary>
        [TestMethod]
        public void Scenarios_MatchCurrentSources()
        {
            var root = Path.GetDirectoryName(GetSolutionPath());
            foreach (var s in BuildExplorerScenarios().Concat(BuildReviewerArchitectScenarios()))
            {
                foreach (var m in s.Mutations)
                {
                    var text = File.ReadAllText(Path.Combine(root, m.RelativePath));
                    Assert.Contains(m.Find, text, $"{s.Id}: mutation anchor '{m.Find}' not found in {m.RelativePath}");
                }
            }
        }

        [TestMethod]
        public void BuildFixture_IsPresentAndScenariosMatchIt()
        {
            var fixturePath = GetBuildFixtureSolutionPath();
            var fixtureRoot = Path.GetDirectoryName(fixturePath);

            // Every mutation must find its text in the current fixture, otherwise the scenario would abort mid-run.
            foreach (var s in BuildBuildScenarios())
                RevertMutations(ApplyMutations(fixtureRoot, s));
        }

        /// <summary>
        /// Location scoring reads "path:line" places out of an answer and never its words: a reference expanded by the runner
        /// ("path:12: `text`"), a range, a comma list, the compiler form "path(line,col)". A "name.cs:99" inside the quoted source text
        /// of an expanded reference is not a location. Runs without VS or LM Studio.
        /// </summary>
        [TestMethod]
        public void ExtractLocations_ReadsReferencesRangesListsAndCompilerForm()
        {
            var content = string.Join("\n",
                "1. LMLocal\\A\\B.cs:12: `var x = other.cs:99;`",
                "- @LMLocal\\A\\C.cs:5-9",
                "LMLocal/A/D.cs:3,7,11",
                "error CS0103: D:\\Work\\fx\\BuildFixture.Lib\\Calculator.cs(9,31): The name does not exist",
                "Plain prose without any location.");

            var found = ExtractLocations(content).Select(l => l.ToString()).ToList();

            CollectionAssert.AreEqual(
                new[]
                {
                    @"LMLocal\A\B.cs:12",
                    @"LMLocal\A\C.cs:5-9",
                    @"LMLocal\A\D.cs:3",
                    @"LMLocal\A\D.cs:7",
                    @"LMLocal\A\D.cs:11",
                    @"D:\Work\fx\BuildFixture.Lib\Calculator.cs:9"
                },
                found);
        }

        /// <summary>
        /// A range marker is expanded by the runner into "File: path, lines a-b" and a four-backtick block with the source lines.
        /// The header is a location (the lines read); a "name.cs:12" inside the block is code, not a location. A block of three
        /// backticks is the model's own text and is read as usual.
        /// </summary>
        [TestMethod]
        public void ExtractLocations_ReadsRangeHeadersAndSkipsRunnerBlocks()
        {
            var content = string.Join("\n",
                @"File: LMLocal\Application\Chat\ToolHistoryPruner.cs, lines 49-49",
                "````csharp",
                "    // see other.cs:77 and Foo.cs:5-9",
                "    internal static class ToolHistoryPruner",
                "````",
                @"2.  The final message: File: LMLocal\Application\Chat\ChatHistoryManager.cs, lines 585-585",
                "````csharp",
                "    var finalAssistant = fragment[fragment.Count - 1];",
                "````",
                @"LMLocal\Application\Chat\HistoryCompactor.cs:276: `var pruned = ToolHistoryPruner.Prune(`",
                "```",
                @"LMLocal\A\B.cs:3",
                "```");

            var found = ExtractLocations(content).Select(l => l.ToString()).ToList();

            CollectionAssert.AreEqual(
                new[]
                {
                    @"LMLocal\Application\Chat\ToolHistoryPruner.cs:49",
                    @"LMLocal\Application\Chat\ChatHistoryManager.cs:585",
                    @"LMLocal\Application\Chat\HistoryCompactor.cs:276",
                    @"LMLocal\A\B.cs:3"
                },
                found);
        }

        [TestMethod]
        public void SamePath_AcceptsAPathCutToItsLastFolders()
        {
            Assert.IsTrue(SamePath(@"Persistence\ChatLogSerializer.cs", @"LMLocal\Infrastructure\Persistence\ChatLogSerializer.cs"));
            Assert.IsTrue(SamePath(@"D:\Work\fx\BuildFixture.Lib\Calculator.cs", @"BuildFixture.Lib\Calculator.cs"));
            Assert.IsTrue(SamePath(@".\LMLocal\A.cs", @"LMLocal\A.cs"));
            Assert.IsFalse(SamePath(@"Other\ChatLogSerializer.cs", @"LMLocal\Infrastructure\Persistence\ChatLogSerializer.cs"));
            Assert.IsFalse(SamePath(string.Empty, @"LMLocal\A.cs"));

            string path;
            int line;
            SplitExpectedLocation(@"LMLocal\A.cs:42", out path, out line);
            Assert.AreEqual(@"LMLocal\A.cs", path);
            Assert.AreEqual(42, line);
            SplitExpectedLocation(@"docs\plan.md", out path, out line);
            Assert.AreEqual(@"docs\plan.md", path);
            Assert.AreEqual(0, line);
        }

        /// <summary>
        /// The rewritten and new scenarios are judged by locations: each must carry a location expectation, and none may
        /// ask for a "yes" (the answer words are not a check). Runs without VS or LM Studio.
        /// </summary>
        [TestMethod]
        public void LocationScenarios_AreJudgedByLocationsNotByYes()
        {
            var all = BuildExplorerScenarios().Concat(BuildReviewerArchitectScenarios()).Concat(BuildBuildScenarios()).Concat(BuildKnowledgeScenarios()).ToList();
            foreach (var id in new[] { "A1.2", "A1.3", "A1.5", "A1.16", "A1.30", "A1.32", "A1.33", "A1.34", "B1.2", "A8.2" })
            {
                var s = all.Single(x => x.Id == id);
                Assert.IsTrue(
                    s.ExpectedLocations.Length > 0 || s.ExpectedLocationsAny.Length > 0 || s.ExpectNoLocations || s.ForbiddenLocationPaths.Length > 0,
                    id + ": a location scenario needs a location expectation");
            }

            foreach (var s in all)
                Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(s.Prompt, @"(?i)\byes\b"),
                    s.Id + ": the prompt must not ask for a yes");
        }

        [TestMethod]
        public void KnowledgeFixture_IsPresentAndScenariosMatchIt()
        {
            var docs = Path.Combine(Path.GetDirectoryName(GetKnowledgeFixtureSolutionPath()), "docs");
            const string target = "set-file-project-status-reload";
            var targetText = File.ReadAllText(Path.Combine(docs, target + ".md"));
            var others = Directory.GetFiles(docs, "*.md").Where(f => Path.GetFileNameWithoutExtension(f) != target).ToList();
            Assert.IsGreaterThanOrEqualTo(2, others.Count, "A8.1 needs distractor documents next to the target one.");

            // A8.2 expects the plan and the conventions documents in the fixture.
            var a82 = BuildKnowledgeScenarios().Single(s => s.Id == "A8.2");
            foreach (var expected in a82.ExpectedLocations)
                Assert.IsTrue(File.Exists(Path.Combine(Path.GetDirectoryName(GetKnowledgeFixtureSolutionPath()), expected)),
                    "A8.2 expected document is missing from the fixture: " + expected);

            // The decision evidence is in the target document only, so naming a distractor cannot pass A8.1.
            var a81 = BuildKnowledgeScenarios().Single(s => s.Id == "A8.1");
            var evidence = a81.MustContain.Where(e => e != target).Concat(a81.MustContainAny.SelectMany(g => g));
            foreach (var ev in evidence)
            {
                Assert.Contains(ev, targetText, StringComparison.OrdinalIgnoreCase, "A8.1 evidence missing from the target document");
                foreach (var other in others)
                    Assert.DoesNotContain(ev, File.ReadAllText(other), StringComparison.OrdinalIgnoreCase,
                        "A8.1 evidence '" + ev + "' also occurs in " + Path.GetFileName(other));
            }
        }
    }
}
