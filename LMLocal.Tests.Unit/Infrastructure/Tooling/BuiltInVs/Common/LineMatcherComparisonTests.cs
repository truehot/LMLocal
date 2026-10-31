using System.Collections.Generic;

using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Pins what <see cref="LineMatcher.LinesEqual"/> — the comparison used by
    /// <c>replace_file_lines</c> / <c>insert_file_lines</c> — considers equal.
    /// This is a property of the comparator, not of any particular file, so the data lives here
    /// as inline cases instead of a fixture file.
    /// Rules: comparison is ordinal with <c>TrimEnd()</c> on both sides and no Unicode normalization.
    /// </summary>
    [TestFixture]
    public class LineMatcherComparisonTests
    {
        [TestCase(" ", "\u00A0", "space vs no-break space", TestName = "Equal_Space_vs_NoBreakSpace")]
        [TestCase(" ", "\u2009", "space vs thin space", TestName = "Equal_Space_vs_ThinSpace")]
        [TestCase(" ", "\u200A", "space vs hair space", TestName = "Equal_Space_vs_HairSpace")]
        [TestCase(" ", "\u2003", "space vs em space", TestName = "Equal_Space_vs_EmSpace")]
        [TestCase(" ", "\u3000", "space vs ideographic space", TestName = "Equal_Space_vs_IdeographicSpace")]
        [TestCase("\t", " ", "tab vs space (trailing whitespace is trimmed)", TestName = "Equal_Tab_vs_Space")]
        [TestCase("X ", "X\u00A0", "trailing no-break space is trimmed", TestName = "Equal_TrailingNoBreakSpace")]
        [TestCase("\u2581", "\u2581", "control: identical markers", TestName = "Equal_Control_IdenticalMarkers")]
        [TestCase(" ", " ", "control: identical spaces", TestName = "Equal_Control_IdenticalSpaces")]
        public void LinesEqual_ReportsSame(string left, string right, string why)
        {
            Assert.That(LineMatcher.LinesEqual(left, right), Is.True, why);
        }

        [TestCase(" ", "\u2581", "space vs lower eighth block (a visible glyph)", TestName = "Different_Space_vs_LowerEighthBlock")]
        [TestCase(" ", "\u200B", "space vs zero width space", TestName = "Different_Space_vs_ZeroWidthSpace")]
        [TestCase("a", "a\u00AD", "a vs a + soft hyphen", TestName = "Different_A_vs_A_SoftHyphen")]
        [TestCase("[\u0020]", "[\u00A0]", "no-break space inside brackets", TestName = "Different_InsideBrackets")]
        [TestCase("a b", "a\u00A0b", "no-break space inside a token", TestName = "Different_InsideToken")]
        public void LinesEqual_ReportsDifferent(string left, string right, string why)
        {
            Assert.That(LineMatcher.LinesEqual(left, right), Is.False, why);
        }

        [Test]
        public void LinesEqualIgnoringLeadingWhitespace_IgnoresOnlyTheLeadingPart()
        {
            // This is the relaxed rule the replace tool applies to the FIRST old_lines line only.
            Assert.That(LineMatcher.LinesEqualIgnoringLeadingWhitespace(" X", "\u00A0X"), Is.True);
            Assert.That(LineMatcher.LinesEqualIgnoringLeadingWhitespace(" X", "\tX"), Is.True);
            Assert.That(LineMatcher.LinesEqualIgnoringLeadingWhitespace(" X", "Y"), Is.False);
        }

        [Test]
        public void LinesEqual_DoesNotNormalizeInvisibleCharacters()
        {
            // Guards the incident: the model dropping or replacing an invisible character breaks the match.
            Assert.That(LineMatcher.LinesEqual("ab", "a\u200Bb"), Is.False);
            Assert.That(LineMatcher.LinesEqual("a b", "a\u200Bb"), Is.False);
        }

        // ── Canonical comparisons (P1) ────────────────────────────────────────

        [TestCase("a b", "a\u00A0b", TestName = "Canonical_Same_SpaceVsNoBreakSpace")]
        [TestCase("x->y", "x\u2192y", TestName = "Canonical_Same_Arrow")]
        [TestCase("-", "\u2014", TestName = "Canonical_Same_EmDash")]
        [TestCase("\"q\"", "\u201Cq\u201D", TestName = "Canonical_Same_TypographicQuotes")]
        [TestCase("X ", "X\u00A0", TestName = "Canonical_Same_TrailingNoBreakSpace")]
        public void LinesEqualCanonical_KeepsTrimEndSemantics(string left, string right)
        {
            Assert.That(LineMatcher.LinesEqualCanonical(left, right), Is.True);
        }

        [Test]
        public void LinesEqualCanonical_ArrowKeepsSurroundingSpacesAsSent()
        {
            // The arrow maps to "->" and nothing else, so a file line "A→B" equals the model's "A->B" but NOT
            // "A -> B": the table replaces the character, it does not invent spaces around it.
            Assert.That(LineMatcher.LinesEqualCanonical("A->B", "A\u2192B"), Is.True);
            Assert.That(LineMatcher.LinesEqualCanonical("A -> B", "A\u2192B"), Is.False);
            Assert.That(LineMatcher.LinesEqualCanonical("A -> B", "A \u2192 B"), Is.True);
        }

        [Test]
        public void LinesEqualCanonical_IsNeverStricterThanStrict()
        {
            // The canonical pass must tolerate everything the strict pass tolerates (hence TrimEnd there too).
            Assert.That(LineMatcher.LinesEqual("x ", "x   "), Is.True);
            Assert.That(LineMatcher.LinesEqualCanonical("x ", "x   "), Is.True);

            // And it adds exactly the classes the tokenizer destroys.
            Assert.That(LineMatcher.LinesEqual("ab", "a\u200Bb"), Is.False);
            Assert.That(LineMatcher.LinesEqualCanonical("ab", "a\u200Bb"), Is.True);
        }

        [Test]
        public void LinesEqualCanonicalExact_DoesNotTrimTrailingWhitespace()
        {
            // Restore only: trimming here would revert an intentional trailing-whitespace edit.
            Assert.That(LineMatcher.LinesEqualCanonicalExact("int a = 1;", "int a = 1;   "), Is.False);
            Assert.That(LineMatcher.LinesEqualCanonicalExact("a\u2192b", "a->b"), Is.True);
            Assert.That(LineMatcher.LinesEqualCanonicalExact("a\u2192b", "a\u2014b"), Is.False);
        }

        [Test]
        public void LinesEqualCanonicalIgnoringLeadingWhitespace_TrimsBothSides()
        {
            Assert.That(LineMatcher.LinesEqualCanonicalIgnoringLeadingWhitespace("    /// x\u2192", "/// x->"), Is.True);
            Assert.That(LineMatcher.LinesEqualCanonicalIgnoringLeadingWhitespace("    /// x", "/// y"), Is.False);
        }

        [Test]
        public void FindMatchesCanonical_FindsNormalizedLineOnly()
        {
            var lines = new List<string> { "a", "label\u00A0x", "c" };
            Assert.That(LineMatcher.FindMatchesCanonical(lines, "label x", 3), Is.EqualTo(new List<int> { 2 }));
            Assert.That(LineMatcher.FindMatchesCanonical(lines, "label y", 3), Is.Empty);
        }
    }
}
