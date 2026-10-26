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
