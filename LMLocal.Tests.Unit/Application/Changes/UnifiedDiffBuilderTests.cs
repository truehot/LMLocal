using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LMLocal.Application.Changes;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.Changes
{
    [TestFixture]
    public class UnifiedDiffBuilderTests
    {
        private static readonly string[] NoLines = new string[0];

        // ── SplitLines ───────────────────────────────────────────────────────

        [Test]
        public void SplitLines_NormalizesAllLineEndings()
        {
            Assert.That(UnifiedDiffBuilder.SplitLines("a\r\nb"), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(UnifiedDiffBuilder.SplitLines("a\rb"), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(UnifiedDiffBuilder.SplitLines("a\nb"), Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void SplitLines_DropsTrailingEmptyLineFromFinalNewline()
        {
            Assert.That(UnifiedDiffBuilder.SplitLines("a\nb\n"), Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void SplitLines_KeepsMeaningfulEmptyLines()
        {
            Assert.That(UnifiedDiffBuilder.SplitLines("\n"), Is.EqualTo(new[] { "" }));
            Assert.That(UnifiedDiffBuilder.SplitLines("a\n\nb"), Is.EqualTo(new[] { "a", "", "b" }));
        }

        [Test]
        public void SplitLines_EmptyOrNullContent_ReturnsNoLines()
        {
            Assert.That(UnifiedDiffBuilder.SplitLines(null), Is.Empty);
            Assert.That(UnifiedDiffBuilder.SplitLines(""), Is.Empty);
        }

        [Test]
        public void SplitLines_HandlesMixedLineEndingsAndTrailingBreaks()
        {
            Assert.That(UnifiedDiffBuilder.SplitLines("a\r\nb\nc\rd"), Is.EqualTo(new[] { "a", "b", "c", "d" }));
            Assert.That(UnifiedDiffBuilder.SplitLines("a\r\n"), Is.EqualTo(new[] { "a" }));
            Assert.That(UnifiedDiffBuilder.SplitLines("\r\n"), Is.EqualTo(new[] { "" }));
            Assert.That(UnifiedDiffBuilder.SplitLines("\r\n\r\n"), Is.EqualTo(new[] { "", "" }));
            Assert.That(UnifiedDiffBuilder.SplitLines("a\r"), Is.EqualTo(new[] { "a" }));
            Assert.That(UnifiedDiffBuilder.SplitLines("a\n\r\nb"), Is.EqualTo(new[] { "a", "", "b" }));
            Assert.That(UnifiedDiffBuilder.SplitLines("a\n\n\n"), Is.EqualTo(new[] { "a", "", "" }));
        }

        [Test]
        public void SplitLines_MatchesNaiveReferenceOnRandomInput()
        {
            var random = new Random(987654321);
            var alphabet = new[] { "a", "b", "\r", "\n", "\r\n" };

            for (int iteration = 0; iteration < 500; iteration++)
            {
                var builder = new System.Text.StringBuilder();
                int parts = random.Next(0, 10);
                for (int i = 0; i < parts; i++)
                    builder.Append(alphabet[random.Next(alphabet.Length)]);

                string content = builder.ToString();

                Assert.That(
                    UnifiedDiffBuilder.SplitLines(content),
                    Is.EqualTo(SplitLinesReference(content)),
                    "content: " + Escape(content));
            }
        }

        [Test]
        public void SplitLines_MatchesNaiveReferenceExhaustivelyForShortInputs()
        {
            // Every string of length 0..6 over {'a', '\r', '\n'}: covers all terminator permutations,
            // including consecutive lone '\r', "\r\n" split by a stray '\r' and trailing terminators.
            var alphabet = new[] { 'a', '\r', '\n' };
            int checkedInputs = 0;

            for (int length = 0; length <= 6; length++)
            {
                int combinations = 1;
                for (int i = 0; i < length; i++)
                    combinations *= alphabet.Length;

                for (int value = 0; value < combinations; value++)
                {
                    var builder = new System.Text.StringBuilder(length);
                    int remaining = value;
                    for (int i = 0; i < length; i++)
                    {
                        builder.Append(alphabet[remaining % alphabet.Length]);
                        remaining /= alphabet.Length;
                    }

                    string content = builder.ToString();
                    checkedInputs++;

                    Assert.That(
                        UnifiedDiffBuilder.SplitLines(content),
                        Is.EqualTo(SplitLinesReference(content)),
                        "content: " + Escape(content));
                }
            }

            Assert.That(checkedInputs, Is.EqualTo(1093));
        }

        [Test]
        public void SplitLines_ConsecutiveLoneCarriageReturns_ProduceEmptyLines()
        {
            Assert.That(UnifiedDiffBuilder.SplitLines("a\r\rb"), Is.EqualTo(new[] { "a", "", "b" }));
            Assert.That(UnifiedDiffBuilder.SplitLines("a\r\n\rb"), Is.EqualTo(new[] { "a", "", "b" }));
            Assert.That(UnifiedDiffBuilder.SplitLines("\r\r\r"), Is.EqualTo(new[] { "", "", "" }));
        }

        // ── Hunk shape ───────────────────────────────────────────────────────

        [Test]
        public void Compute_IdenticalInput_ProducesNoHunks()
        {
            var hunks = UnifiedDiffBuilder.Compute(new[] { "a", "b" }, new[] { "a", "b" });

            Assert.That(hunks, Is.Empty);
            Assert.That(UnifiedDiffBuilder.CountAdded(hunks), Is.EqualTo(0));
            Assert.That(UnifiedDiffBuilder.CountRemoved(hunks), Is.EqualTo(0));
        }

        [Test]
        public void Compute_ReplacedLine_ProducesRemovedThenAddedWithCorrectHeader()
        {
            var hunks = UnifiedDiffBuilder.Compute(new[] { "a", "b", "c" }, new[] { "a", "B", "c" });

            Assert.That(hunks.Count, Is.EqualTo(1));
            Assert.That(hunks[0].ToText(), Is.EqualTo("@@ -1,3 +1,3 @@\n a\n-b\n+B\n c\n"));
            Assert.That(hunks[0].OldStart, Is.EqualTo(1));
            Assert.That(hunks[0].NewStart, Is.EqualTo(1));
            Assert.That(UnifiedDiffBuilder.CountAdded(hunks), Is.EqualTo(1));
            Assert.That(UnifiedDiffBuilder.CountRemoved(hunks), Is.EqualTo(1));
        }

        [Test]
        public void Compute_CreatedFromEmpty_HeaderStartsAtZero()
        {
            var hunks = UnifiedDiffBuilder.Compute(NoLines, new[] { "x", "y" });

            Assert.That(hunks.Count, Is.EqualTo(1));
            Assert.That(hunks[0].ToText(), Is.EqualTo("@@ -0,0 +1,2 @@\n+x\n+y\n"));
            Assert.That(hunks[0].OldCount, Is.EqualTo(0));
            Assert.That(hunks[0].NewCount, Is.EqualTo(2));
        }

        [Test]
        public void Compute_DeletedToEmpty_HeaderEndsAtZero()
        {
            var hunks = UnifiedDiffBuilder.Compute(new[] { "x", "y" }, NoLines);

            Assert.That(hunks.Count, Is.EqualTo(1));
            Assert.That(hunks[0].ToText(), Is.EqualTo("@@ -1,2 +0,0 @@\n-x\n-y\n"));
            Assert.That(hunks[0].NewCount, Is.EqualTo(0));
        }

        [Test]
        public void Compute_EveryHunkHeader_MatchesItsBody()
        {
            var original = new string[40];
            var modified = new string[40];
            for (int i = 0; i < 40; i++)
            {
                original[i] = "line" + i;
                modified[i] = i % 10 == 0 ? "changed" + i : "line" + i;
            }

            var hunks = UnifiedDiffBuilder.Compute(original, modified);

            Assert.That(hunks.Count, Is.GreaterThan(1));
            foreach (var hunk in hunks)
                AssertHeaderMatchesBody(hunk.ToText(), hunk);
        }

        [Test]
        public void Compute_ContextIsLimitedToThreeLines()
        {
            var original = MakeLines(20);
            var modified = (string[])original.Clone();
            modified[10] = "changed";

            var hunks = UnifiedDiffBuilder.Compute(original, modified);

            Assert.That(hunks.Count, Is.EqualTo(1));
            Assert.That(hunks[0].OldStart, Is.EqualTo(8));
            Assert.That(hunks[0].OldCount, Is.EqualTo(7));
            Assert.That(hunks[0].NewCount, Is.EqualTo(7));

            // 3 context lines + 3 context lines around the change, plus the removed and the added line.
            Assert.That(hunks[0].Lines.Count, Is.EqualTo(8));
        }

        [Test]
        public void Compute_ChangesFarApart_ProduceSeparateHunks()
        {
            var original = MakeLines(20);
            var modified = (string[])original.Clone();
            modified[2] = "changed-a";
            modified[17] = "changed-b";

            var hunks = UnifiedDiffBuilder.Compute(original, modified);

            Assert.That(hunks.Count, Is.EqualTo(2));
            Assert.That(hunks[0].OldStart, Is.EqualTo(1));
            Assert.That(hunks[1].OldStart, Is.EqualTo(15));
        }

        [Test]
        public void Compute_ChangesCloseTogether_ProduceSingleHunk()
        {
            var original = MakeLines(20);
            var modified = (string[])original.Clone();
            modified[5] = "changed-a";
            modified[8] = "changed-b";

            var hunks = UnifiedDiffBuilder.Compute(original, modified);

            Assert.That(hunks.Count, Is.EqualTo(1));
        }

        [Test]
        public void Compute_ZeroContextLines_ProducesChangesOnly()
        {
            var hunks = UnifiedDiffBuilder.Compute(new[] { "a", "b", "c" }, new[] { "a", "B", "c" }, 0);

            Assert.That(hunks.Count, Is.EqualTo(1));
            Assert.That(hunks[0].ToText(), Is.EqualTo("@@ -2,1 +2,1 @@\n-b\n+B\n"));
        }

        // ── Rendering rules ──────────────────────────────────────────────────

        [Test]
        public void ToText_BlankContextLine_IsExactlyOneSpace()
        {
            var hunks = UnifiedDiffBuilder.Compute(new[] { "a", "", "b" }, new[] { "a", "", "B" });

            Assert.That(hunks[0].ToText(), Is.EqualTo("@@ -1,3 +1,3 @@\n a\n \n-b\n+B\n"));
        }

        [Test]
        public void ToText_ContextLineStartingWithMarkerCharacter_KeepsSingleMarker()
        {
            var hunks = UnifiedDiffBuilder.Compute(new[] { "a", "+x" }, new[] { "a", "+x", "y" });

            // The marker is only the first character: a context line whose text starts with '+' is
            // rendered as one space followed by the verbatim text, exactly like git does.
            Assert.That(hunks[0].ToText(), Is.EqualTo("@@ -1,2 +1,3 @@\n a\n +x\n+y\n"));
        }

        [Test]
        public void ToText_IndentationIsPreservedVerbatim()
        {
            var hunks = UnifiedDiffBuilder.Compute(new[] { "    indented", "x" }, new[] { "    indented", "y" });

            Assert.That(hunks[0].ToText(), Is.EqualTo("@@ -1,2 +1,2 @@\n     indented\n-x\n+y\n"));
        }

        [Test]
        public void ToText_AlwaysEndsWithNewlineAndNeverEmitsNoNewlineMarker()
        {
            var hunks = UnifiedDiffBuilder.Compute(new[] { "a" }, new[] { "a", "b" });

            string text = hunks[0].ToText();
            Assert.That(text.EndsWith("\n"), Is.True);
            Assert.That(text, Does.Not.Contain("\\ No newline at end of file"));
        }

        [Test]
        public void Compute_IsDeterministic()
        {
            var original = MakeLines(30);
            var modified = (string[])original.Clone();
            modified[3] = "x";
            modified[20] = "y";

            Assert.That(UnifiedDiffBuilder.Compute(original, modified)[0].ToText(),
                Is.EqualTo(UnifiedDiffBuilder.Compute(original, modified)[0].ToText()));
        }

        [Test]
        public void Compute_LargeRewrite_FallsBackToCoarseReplacementAndStillApplies()
        {
            var original = new string[UnifiedDiffBuilder.MaxEditDistance + 200];
            var modified = new string[original.Length];
            for (int i = 0; i < original.Length; i++)
            {
                original[i] = "left" + i;
                modified[i] = "right" + i;
            }

            var hunks = UnifiedDiffBuilder.Compute(original, modified);

            Assert.That(UnifiedDiffBuilder.CountRemoved(hunks), Is.EqualTo(original.Length));
            Assert.That(UnifiedDiffBuilder.CountAdded(hunks), Is.EqualTo(modified.Length));
            Assert.That(ApplyHunks(original, hunks), Is.EqualTo(modified));
        }

        [Test]
        public void Compute_ChangeOnFirstAndLastLine_UsesAvailableContextOnly()
        {
            var original = MakeLines(10);
            var modified = (string[])original.Clone();
            modified[0] = "first";
            modified[9] = "last";

            var hunks = UnifiedDiffBuilder.Compute(original, modified);

            Assert.That(hunks.Count, Is.EqualTo(2));

            // No context before line 1 and none after line 10: fewer than 3 context lines on that side.
            Assert.That(hunks[0].OldStart, Is.EqualTo(1));
            Assert.That(hunks[0].OldCount, Is.EqualTo(4));
            Assert.That(hunks[1].OldStart, Is.EqualTo(7));
            Assert.That(hunks[1].OldCount, Is.EqualTo(4));
            Assert.That(hunks[1].NewStart + hunks[1].NewCount - 1, Is.EqualTo(10));
            Assert.That(ApplyHunks(original, hunks), Is.EqualTo(modified));
        }

        // ── Round-trip / minimality ──────────────────────────────────────────

        [Test]
        public void Compute_RandomSmallInputs_AreMinimalAndApplyCleanly()
        {
            var random = new Random(20240601);
            var alphabet = new[] { "a", "b", "c", "" };

            for (int iteration = 0; iteration < 200; iteration++)
            {
                string[] original = RandomLines(random, alphabet, random.Next(0, 9));
                string[] modified = RandomLines(random, alphabet, random.Next(0, 9));

                var hunks = UnifiedDiffBuilder.Compute(original, modified, 3);
                int lcs = LcsLength(original, modified);

                Assert.That(UnifiedDiffBuilder.CountAdded(hunks), Is.EqualTo(modified.Length - lcs),
                    "added mismatch (iteration " + iteration + ")");
                Assert.That(UnifiedDiffBuilder.CountRemoved(hunks), Is.EqualTo(original.Length - lcs),
                    "removed mismatch (iteration " + iteration + ")");
                Assert.That(ApplyHunks(original, hunks), Is.EqualTo(modified),
                    "apply mismatch (iteration " + iteration + ")");
            }
        }

        // ── Diff model ───────────────────────────────────────────────────────
        // ── Middle edit script: trace layout, limits, empty inputs ───────────

        [Test]
        public void BuildMiddleEditScript_MatchesOnePassReferenceOnCorpus()
        {
            // The production search records its backtracking trace in one flat buffer, row d at d*(d+1)/2.
            // The oracle below is the plain one-int[]-per-step Myers it replaced, so this test fails if the row
            // arithmetic, the growth or the backtracking bounds change the *script*, not just the memory layout.
            var corpus = new List<string[][]>
            {
                new[] { new string[0], new string[0] },
                new[] { new[] { "a" }, new[] { "a" } },
                new[] { new string[0], new[] { "a" } },
                new[] { new[] { "a" }, new string[0] },
                new[] { new[] { "a", "b", "c" }, new[] { "a", "b", "c" } },          // distance 0
                new[] { new[] { "a", "b", "c" }, new[] { "x", "y", "z" } },          // distance n + m
                new[] { new[] { "a", "b", "c" }, new[] { "a", "c", "b" } },          // transposition
                new[] { new[] { "a" }, new[] { "b", "a", "c" } },                     // one shared line
                new[] { new[] { "a", "b", "c", "d" }, new[] { "x", "b", "c", "y" } }, // shared middle
                new[] { MakeLines(26), WithChangedMiddle(MakeLines(26), 12, "changed") }
            };

            var random = new Random(20240725);
            var alphabet = new[] { "a", "b", "c", "" };
            for (int i = 0; i < 20; i++)
            {
                corpus.Add(new[]
                {
                    RandomLines(random, alphabet, random.Next(0, 9)),
                    RandomLines(random, alphabet, random.Next(0, 9))
                });
            }

            for (int i = 0; i < corpus.Count; i++)
            {
                string[] original = corpus[i][0];
                string[] modified = corpus[i][1];
                string label = "case " + i;

                List<DiffLine> expected = OnePassMyersReference(original, modified);

                // A context window this large collapses every change into a single hunk, so the flattened hunk
                // body is the complete edit script - the only view in which "same script" can be compared.
                int contextLines = original.Length + modified.Length + 1;
                var hunks = UnifiedDiffBuilder.Compute(original, modified, contextLines);

                Assert.That(ApplyHunks(original, hunks), Is.EqualTo(modified), "apply mismatch (" + label + ")");

                bool hasChange = false;
                foreach (var line in expected)
                {
                    if (line.Kind != DiffLineKind.Context)
                    {
                        hasChange = true;
                        break;
                    }
                }

                if (!hasChange)
                {
                    Assert.That(hunks, Is.Empty, "identical input must produce no hunks (" + label + ")");
                    continue;
                }

                var actual = new List<DiffLine>();
                foreach (var hunk in hunks)
                    foreach (var line in hunk.Lines)
                        actual.Add(line);

                Assert.That(actual, Is.EqualTo(expected), "script mismatch (" + label + ")");
                Assert.That(UnifiedDiffBuilder.CountAdded(hunks) + UnifiedDiffBuilder.CountRemoved(hunks),
                    Is.EqualTo(DistanceOf(expected)), "distance mismatch (" + label + ")");
            }
        }

        [Test]
        public void Compute_EditDistanceAtLimit_ProducesMinimalScript()
        {
            // One shared line in the middle of two 1001-line blocks: the minimal distance is
            // n + m - 2 * 1 = 2000, i.e. exactly MaxEditDistance, the largest trace the search can record.
            // The shared line is what makes the test meaningful: a coarse replacement would report 2002 edits
            // (1001 removed + 1001 added), so the counts below distinguish the real search from the fallback.
            var original = new string[1001];
            var modified = new string[original.Length];
            for (int i = 0; i < original.Length; i++)
            {
                original[i] = "left" + i;
                modified[i] = "right" + i;
            }

            modified[500] = original[500];

            Assert.That(original.Length + modified.Length - 2 * LcsLength(original, modified),
                Is.EqualTo(UnifiedDiffBuilder.MaxEditDistance));

            var hunks = UnifiedDiffBuilder.Compute(original, modified);

            Assert.That(UnifiedDiffBuilder.CountRemoved(hunks), Is.EqualTo(original.Length - 1));
            Assert.That(UnifiedDiffBuilder.CountAdded(hunks), Is.EqualTo(modified.Length - 1));
            Assert.That(ApplyHunks(original, hunks), Is.EqualTo(modified));
        }

        [Test]
        public void Compute_EditDistanceAboveLimit_ProducesCoarseReplacement()
        {
            // One line more on each side than the limit allows: the search never reaches the last diagonal, so
            // the block is reported as a coarse replacement instead of a minimal script.
            int length = UnifiedDiffBuilder.MaxEditDistance / 2 + 1;
            var original = new string[length];
            var modified = new string[length];
            for (int i = 0; i < length; i++)
            {
                original[i] = "left" + i;
                modified[i] = "right" + i;
            }

            Assert.That(original.Length + modified.Length,
                Is.GreaterThan(UnifiedDiffBuilder.MaxEditDistance));

            var hunks = UnifiedDiffBuilder.Compute(original, modified);

            Assert.That(UnifiedDiffBuilder.CountRemoved(hunks), Is.EqualTo(original.Length));
            Assert.That(UnifiedDiffBuilder.CountAdded(hunks), Is.EqualTo(modified.Length));
            Assert.That(ApplyHunks(original, hunks), Is.EqualTo(modified));
        }

        [Test]
        public void Compute_EmptyAndNullInputs_ProduceNoHunks()
        {
            Assert.That(UnifiedDiffBuilder.Compute(null, null), Is.Empty);
            Assert.That(UnifiedDiffBuilder.Compute(new string[0], new string[0]), Is.Empty);
            Assert.That(UnifiedDiffBuilder.Compute(null, new string[0]), Is.Empty);
            Assert.That(UnifiedDiffBuilder.Compute(new string[0], null), Is.Empty);
            Assert.That(UnifiedDiffBuilder.CountAdded(null), Is.EqualTo(0));
            Assert.That(UnifiedDiffBuilder.CountRemoved(null), Is.EqualTo(0));
        }

        [Test]
        public void Compute_PartialOverlap_ProducesExactlyTheEditedCount()
        {
            // 1000 lines, exactly 100 distinct ones rewritten: the minimal script has 200 edits, an order of
            // magnitude below the limit. If the limit is ever lowered under this shape, the coarse fallback would
            // report 2000 changed lines and this test fails before the diff silently degrades.
            var original = MakeLines(1000);
            var modified = (string[])original.Clone();

            var indices = new List<int>();
            for (int i = 0; i < modified.Length; i++)
                indices.Add(i);

            var random = new Random(424242);
            for (int i = indices.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                int swap = indices[i];
                indices[i] = indices[j];
                indices[j] = swap;
            }

            for (int i = 0; i < 100; i++)
                modified[indices[i]] = "changed" + i + "-" + indices[i];

            var hunks = UnifiedDiffBuilder.Compute(original, modified);

            Assert.That(UnifiedDiffBuilder.CountRemoved(hunks), Is.EqualTo(100));
            Assert.That(UnifiedDiffBuilder.CountAdded(hunks), Is.EqualTo(100));
            Assert.That(ApplyHunks(original, hunks), Is.EqualTo(modified));
        }

        // ── Diff model ───────────────────────────────────────────────────────


        [Test]
        public void DiffLine_IsValueTypeWithValueEquality()
        {
            var line = new DiffLine(DiffLineKind.Added, "text");
            var same = new DiffLine(DiffLineKind.Added, "text");
            var otherKind = new DiffLine(DiffLineKind.Removed, "text");
            var otherText = new DiffLine(DiffLineKind.Added, "other");

            Assert.That(typeof(DiffLine).IsValueType, Is.True);
            Assert.That(line, Is.EqualTo(same));
            Assert.That(line == same, Is.True);
            Assert.That(line != otherKind, Is.True);
            Assert.That(line != otherText, Is.True);
            Assert.That(line.GetHashCode(), Is.EqualTo(same.GetHashCode()));
        }

        [Test]
        public void DiffLine_NullText_IsCoercedToEmptyString()
        {
            Assert.That(new DiffLine(DiffLineKind.Context, null).Text, Is.Empty);

            // default(DiffLine) is reachable through a default-initialized array. The value type is total:
            // a defaulted instance reports an empty Text and compares equal to one built with a null argument.
            DiffLine[] array = new DiffLine[1];
            DiffLine defaulted = array[0];
            Assert.That(defaulted.Text, Is.Empty);
            Assert.That(defaulted, Is.EqualTo(new DiffLine(DiffLineKind.Context, null)));
            Assert.That(defaulted, Is.EqualTo(new DiffLine(DiffLineKind.Context, string.Empty)));
            Assert.That(defaulted.GetHashCode(),
                Is.EqualTo(new DiffLine(DiffLineKind.Context, null).GetHashCode()));
        }

        [Test]
        public void DiffHunk_NullLines_ExposesEmptyListAndStillRendersHeader()
        {
            var hunk = new DiffHunk(0, 0, 0, 0, null);

            Assert.That(hunk.Lines, Is.Not.Null);
            Assert.That(hunk.Lines.Count, Is.EqualTo(0));
            Assert.That(hunk.RenderedLineCount, Is.EqualTo(1));
            Assert.That(hunk.ToText(), Is.EqualTo("@@ -0,0 +0,0 @@\n"));
        }

        [Test]
        public void ToText_PreallocatedBufferMatchesGrownBufferOutput()
        {
            // The capacity estimate must never truncate output: build a hunk whose text is longer than
            // the estimate's per-line share and verify the rendered text is complete.
            var lines = new List<DiffLine>
            {
                new DiffLine(DiffLineKind.Context, new string('x', 500)),
                new DiffLine(DiffLineKind.Added, new string('y', 500)),
                new DiffLine(DiffLineKind.Removed, new string('z', 500))
            };

            string text = new DiffHunk(1, 2, 1, 2, lines).ToText();

            Assert.That(text, Is.EqualTo("@@ -1,2 +1,2 @@\n " + new string('x', 500) + "\n"
                                         + "+" + new string('y', 500) + "\n"
                                         + "-" + new string('z', 500) + "\n"));
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Straightforward reference implementation of the documented SplitLines contract
        /// ("\r\n" then lone "\r" normalized to "\n"; one trailing empty line dropped). It allocates an
        /// intermediate string on purpose — the production code must produce the same lines without it.
        /// </summary>
        private static string[] SplitLinesReference(string content)
        {
            if (string.IsNullOrEmpty(content))
                return new string[0];

            string[] parts = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (parts.Length > 0 && parts[parts.Length - 1].Length == 0)
            {
                var trimmed = new string[parts.Length - 1];
                Array.Copy(parts, trimmed, trimmed.Length);
                return trimmed;
            }

            return parts;
        }

        private static string Escape(string value)
        {
            return value == null
                ? "<null>"
                : value.Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static string[] MakeLines(int count)
        {
            var lines = new string[count];
            for (int i = 0; i < count; i++)
                lines[i] = "line" + i;

            return lines;
        }

        /// <summary>
        /// Replaces one line of a copy of <paramref name="lines"/>, so a case has a common prefix and suffix.
        /// </summary>
        private static string[] WithChangedMiddle(string[] lines, int index, string replacement)
        {
            var copy = (string[])lines.Clone();
            copy[index] = replacement;
            return copy;
        }

        /// <summary>
        /// Number of removed + added lines in a script, i.e. the edit distance the search found.
        /// </summary>
        private static int DistanceOf(List<DiffLine> script)
        {
            int distance = 0;
            foreach (var line in script)
            {
                if (line.Kind != DiffLineKind.Context)
                    distance++;
            }

            return distance;
        }

        /// <summary>
        /// Oracle: the plain one-pass Myers search, with the furthest-reaching <c>x</c> of every step kept in its
        /// own <c>int[]</c> and no edit-distance cap (the corpus is small enough for full coverage). It mirrors
        /// the recurrence and the tie-breaking rule the production code used before the trace was flattened, so
        /// comparing the two scripts pins down that the refactor changed memory only.
        /// </summary>
        private static List<DiffLine> OnePassMyersReference(string[] original, string[] modified)
        {
            string[] a = original ?? new string[0];
            string[] b = modified ?? new string[0];
            var ops = new List<DiffLine>(a.Length + b.Length);

            int maxPrefix = Math.Min(a.Length, b.Length);
            int prefix = 0;
            while (prefix < maxPrefix && string.Equals(a[prefix], b[prefix], StringComparison.Ordinal))
                prefix++;

            int maxSuffix = maxPrefix - prefix;
            int suffix = 0;
            while (suffix < maxSuffix &&
                   string.Equals(a[a.Length - 1 - suffix], b[b.Length - 1 - suffix], StringComparison.Ordinal))
            {
                suffix++;
            }

            for (int i = 0; i < prefix; i++)
                ops.Add(new DiffLine(DiffLineKind.Context, a[i]));

            int aStart = prefix;
            int aEnd = a.Length - suffix;
            int bStart = prefix;
            int bEnd = b.Length - suffix;
            int n = aEnd - aStart;
            int m = bEnd - bStart;

            if (n == 0)
            {
                for (int i = bStart; i < bEnd; i++)
                    ops.Add(new DiffLine(DiffLineKind.Added, b[i]));
            }
            else if (m == 0)
            {
                for (int i = aStart; i < aEnd; i++)
                    ops.Add(new DiffLine(DiffLineKind.Removed, a[i]));
            }
            else
            {
                ReferenceMiddleEditScript(a, aStart, aEnd, b, bStart, bEnd, ops);
            }

            for (int i = a.Length - suffix; i < a.Length; i++)
                ops.Add(new DiffLine(DiffLineKind.Context, a[i]));

            return ops;
        }

        private static void ReferenceMiddleEditScript(
            string[] a, int aStart, int aEnd, string[] b, int bStart, int bEnd, List<DiffLine> ops)
        {
            int n = aEnd - aStart;
            int m = bEnd - bStart;
            int maxD = n + m;
            int offset = maxD + 1;

            var v = new int[2 * maxD + 3];
            for (int i = 0; i < v.Length; i++)
                v[i] = -1;
            v[offset + 1] = 0;

            var trace = new List<int[]>();
            int finalD = -1;

            for (int d = 0; d <= maxD; d++)
            {
                for (int k = -d; k <= d; k += 2)
                {
                    int x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])
                        ? v[offset + k + 1]
                        : v[offset + k - 1] + 1;

                    int y = x - k;
                    while (x < n && y < m && x >= 0 && y >= 0 &&
                           string.Equals(a[aStart + x], b[bStart + y], StringComparison.Ordinal))
                    {
                        x++;
                        y++;
                    }

                    v[offset + k] = x;

                    if (x >= n && y >= m)
                    {
                        finalD = d;
                        break;
                    }
                }

                if (finalD >= 0)
                    break;

                var snapshot = new int[d + 1];
                for (int k = -d; k <= d; k += 2)
                    snapshot[(k + d) / 2] = v[offset + k];
                trace.Add(snapshot);
            }

            Assert.That(finalD, Is.GreaterThanOrEqualTo(0),
                "The reference search must always reach a script on inputs this small.");

            var reversed = new List<DiffLine>(n + m);
            int cx = n;
            int cy = m;

            for (int d = finalD; d > 0; d--)
            {
                int k = cx - cy;
                int prevK;
                if (k == -d || (k != d && ReferenceAfter(trace, d - 1, k - 1) < ReferenceAfter(trace, d - 1, k + 1)))
                    prevK = k + 1;
                else
                    prevK = k - 1;

                int prevX = ReferenceAfter(trace, d - 1, prevK);
                int prevY = prevX - prevK;

                int snakeX = prevK == k + 1 ? prevX : prevX + 1;
                int snakeY = prevK == k + 1 ? prevY + 1 : prevY;

                while (cx > snakeX && cy > snakeY)
                {
                    reversed.Add(new DiffLine(DiffLineKind.Context, a[aStart + cx - 1]));
                    cx--;
                    cy--;
                }

                if (prevK == k + 1)
                    reversed.Add(new DiffLine(DiffLineKind.Added, b[bStart + prevY]));
                else
                    reversed.Add(new DiffLine(DiffLineKind.Removed, a[aStart + prevX]));

                cx = prevX;
                cy = prevY;
            }

            while (cx > 0 && cy > 0)
            {
                reversed.Add(new DiffLine(DiffLineKind.Context, a[aStart + cx - 1]));
                cx--;
                cy--;
            }

            for (int i = reversed.Count - 1; i >= 0; i--)
                ops.Add(reversed[i]);
        }

        private static int ReferenceAfter(List<int[]> trace, int step, int k)
        {
            if (step < 0 || step >= trace.Count || k < -step || k > step)
                return -1;

            return trace[step][(k + step) / 2];
        }

        private static string[] RandomLines(Random random, string[] alphabet, int count)
        {
            var lines = new string[count];
            for (int i = 0; i < count; i++)
                lines[i] = alphabet[random.Next(alphabet.Length)];

            return lines;
        }

        private static int LcsLength(string[] a, string[] b)
        {
            var dp = new int[a.Length + 1, b.Length + 1];
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    dp[i, j] = string.Equals(a[i - 1], b[j - 1], StringComparison.Ordinal)
                        ? dp[i - 1, j - 1] + 1
                        : Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }

            return dp[a.Length, b.Length];
        }

        private static void AssertHeaderMatchesBody(string hunkText, DiffHunk hunk)
        {
            var match = Regex.Match(hunkText, "^@@ -(\\d+),(\\d+) \\+(\\d+),(\\d+) @@\\n");
            Assert.That(match.Success, Is.True, "unparsable header: " + hunkText);

            int oldCount = int.Parse(match.Groups[2].Value);
            int newCount = int.Parse(match.Groups[4].Value);

            int context = 0;
            int added = 0;
            int removed = 0;

            foreach (string line in hunkText.Substring(match.Length).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Assert.That(line.Length, Is.GreaterThan(0), "line without marker");
                switch (line[0])
                {
                    case ' ':
                        context++;
                        break;
                    case '+':
                        added++;
                        break;
                    case '-':
                        removed++;
                        break;
                    default:
                        Assert.Fail("unexpected marker '" + line[0] + "'");
                        break;
                }
            }

            Assert.That(oldCount, Is.EqualTo(context + removed), "old count: " + hunkText);
            Assert.That(newCount, Is.EqualTo(context + added), "new count: " + hunkText);
            Assert.That(hunk.OldCount, Is.EqualTo(oldCount));
            Assert.That(hunk.NewCount, Is.EqualTo(newCount));
        }

        /// <summary>
        /// Rebuilds the modified side by applying the hunks to the original lines; also verifies the
        /// original side of every context/removed line.
        /// </summary>
        private static string[] ApplyHunks(string[] original, List<DiffHunk> hunks)
        {
            var result = new List<string>();
            int index = 0;

            foreach (var hunk in hunks)
            {
                int from = hunk.OldCount == 0 ? hunk.OldStart : hunk.OldStart - 1;
                while (index < from)
                    result.Add(original[index++]);

                foreach (var line in hunk.Lines)
                {
                    switch (line.Kind)
                    {
                        case DiffLineKind.Added:
                            result.Add(line.Text);
                            break;
                        case DiffLineKind.Removed:
                            Assert.That(original[index], Is.EqualTo(line.Text), "removed line mismatch at " + index);
                            index++;
                            break;
                        default:
                            Assert.That(original[index], Is.EqualTo(line.Text), "context line mismatch at " + index);
                            result.Add(line.Text);
                            index++;
                            break;
                    }
                }
            }

            while (index < original.Length)
                result.Add(original[index++]);

            return result.ToArray();
        }
    }
}
