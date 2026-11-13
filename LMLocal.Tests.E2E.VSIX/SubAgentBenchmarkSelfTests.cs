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
        /// A1.8 tests pagination only while its term has more matching lines than one page of
        /// search_file_content can hold (max_results is capped at 500). Runs without VS or LM Studio.
        /// </summary>
        [TestMethod]
        public void ExplorerPagingScenario_NeedsSecondPage()
        {
            const int maxPageSize = 500;
            var count = MatchingLines(A108File, A108Text).Count;
            Assert.IsGreaterThan(maxPageSize, count,
                $"A1.8: '{A108Text}' has {count} matching lines in {A108File}; it must exceed {maxPageSize} so the last match is on page 2.");
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
            foreach (var s in BuildScenarios())
            {
                foreach (var m in s.Mutations)
                {
                    var text = File.ReadAllText(Path.Combine(root, m.RelativePath));
                    Assert.IsTrue(text.Contains(m.Find), $"{s.Id}: mutation anchor '{m.Find}' not found in {m.RelativePath}");
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

        [TestMethod]
        public void KnowledgeFixture_IsPresentAndScenariosMatchIt()
        {
            var docs = Path.Combine(Path.GetDirectoryName(GetKnowledgeFixtureSolutionPath()), "docs");
            const string target = "set-file-project-status-reload";
            var targetText = File.ReadAllText(Path.Combine(docs, target + ".md"));
            var others = Directory.GetFiles(docs, "*.md").Where(f => Path.GetFileNameWithoutExtension(f) != target).ToList();
            Assert.IsGreaterThanOrEqualTo(2, others.Count, "A8.1 needs distractor documents next to the target one.");

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
