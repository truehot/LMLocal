using System.Linq;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Pins the character table behind canonical matching: the model's tokenizer rewrites no-break and other
    /// typographic spaces to a plain space, typographic quotes and dashes to ASCII, drops zero-width characters and
    /// mixes NFC/NFD, so a line that exists in the file stops being found by the strict comparators.
    /// Canonicalization is used for COMPARISON only — a canonical form is never written to a file.
    /// </summary>
    [TestFixture]
    public class CharacterMapTests
    {
        // ── Table, category by category ───────────────────────────────────────

        [TestCase("a\u00A0b", "a b", TestName = "Canonicalize_NoBreakSpace_BecomesSpace")]
        [TestCase("a\u2009b", "a b", TestName = "Canonicalize_ThinSpace_BecomesSpace")]
        [TestCase("a\u200Ab", "a b", TestName = "Canonicalize_HairSpace_BecomesSpace")]
        [TestCase("a\u2003b", "a b", TestName = "Canonicalize_EmSpace_BecomesSpace")]
        [TestCase("a\u3000b", "a b", TestName = "Canonicalize_IdeographicSpace_BecomesSpace")]
        [TestCase("a\u2018b\u2019", "a'b'", TestName = "Canonicalize_SingleQuotes_BecomeApostrophes")]
        [TestCase("a\u201Ab\u201B", "a'b'", TestName = "Canonicalize_LowSingleQuotes_BecomeApostrophes")]
        [TestCase("a\u201Cb\u201D", "a\"b\"", TestName = "Canonicalize_DoubleQuotes_BecomeAscii")]
        [TestCase("a\u201Eb\u201E", "a\"b\"", TestName = "Canonicalize_LowDoubleQuotes_BecomeAscii")]
        [TestCase("a\u2012b", "a-b", TestName = "Canonicalize_FigureDash_BecomeHyphen")]
        [TestCase("a\u2013b", "a-b", TestName = "Canonicalize_EnDash_BecomeHyphen")]
        [TestCase("a\u2014b", "a-b", TestName = "Canonicalize_EmDash_BecomeHyphen")]
        [TestCase("a\u2015b", "a-b", TestName = "Canonicalize_HorizontalBar_BecomeHyphen")]
        [TestCase("wait\u2026", "wait...", TestName = "Canonicalize_Ellipsis_BecomeThreeDots")]
        [TestCase("x\u2192y", "x->y", TestName = "Canonicalize_RightArrow_BecomeAscii")]
        [TestCase("x\u2190y", "x<-y", TestName = "Canonicalize_LeftArrow_BecomeAscii")]
        [TestCase("a\u200Bb", "ab", TestName = "Canonicalize_ZeroWidthSpace_IsDeleted")]
        [TestCase("a\uFEFFb", "ab", TestName = "Canonicalize_ContentBom_IsDeleted")]
        [TestCase("a\u200Db", "a\u200Db", TestName = "Canonicalize_ZeroWidthJoiner_IsKept")]
        [TestCase("a\u200Cb", "a\u200Cb", TestName = "Canonicalize_ZeroWidthNonJoiner_IsKept")]
        [TestCase("a\u00ADb", "a\u00ADb", TestName = "Canonicalize_SoftHyphen_IsKept")]
        [TestCase("\u0438\u0306", "\u0439", TestName = "Canonicalize_NfdCyrillic_IsComposed")]
        [TestCase("a\U0001F41Bb", "a\U0001F41Bb", TestName = "Canonicalize_Emoji_IsKept")]
        public void Canonicalize_MapsTableCharacters(string input, string expected)
        {
            Assert.That(CharacterMap.Canonicalize(input), Is.EqualTo(expected));
        }

        [Test]
        public void Canonicalize_LineSeparatorAndNel_BecomeSpaceNotDeleted()
        {
            // U+2028/U+0085 are Unicode whitespace but not line breaks for StreamReader.ReadLine, so they occur
            // inside a line; the model replaces them with a space, not with nothing.
            Assert.That(CharacterMap.Canonicalize("word1\u2028word2"), Is.EqualTo("word1 word2"));
            Assert.That(CharacterMap.Canonicalize("word1\u0085word2"), Is.EqualTo("word1 word2"));
        }

        [Test]
        public void Canonicalize_AppliesDeletionsThenReplacementsThenNfc()
        {
            string input = "e\u0301\u00A0\u200Bx";   // combining accent + no-break space + zero-width space
            Assert.That(CharacterMap.Canonicalize(input), Is.EqualTo("\u00E9 x"));
        }

        // ── Invariant 1: idempotence ──────────────────────────────────────────

        [Test]
        public void Canonicalize_IsIdempotent()
        {
            var samples = new[]
            {
                "a\u00A0b", "\u201Cq\u201D", "a\u2014b", "wait\u2026", "x\u2192y", "a\u200Bb",
                "\u0438\u0306", "plain ascii", "\U0001F41B emoji", "word1\u2028word2"
            };

            foreach (var sample in samples)
            {
                string once = CharacterMap.Canonicalize(sample);
                Assert.That(CharacterMap.Canonicalize(once), Is.EqualTo(once), sample);
            }
        }

        // ── Invariant 4: no work on clean text ────────────────────────────────

        [Test]
        public void Canonicalize_PureAscii_ReturnsSameInstance()
        {
            string input = "var x = 1; // ascii only";
            Assert.That(CharacterMap.Canonicalize(input), Is.SameAs(input));
        }

        [Test]
        public void Canonicalize_AlreadyNfcCyrillic_ReturnsSameInstance()
        {
            // No table characters but non-ASCII: the IsNormalized branch must still avoid the allocation.
            string input = "\u041F\u0440\u0438\u0432\u0435\u0442";
            Assert.That(CharacterMap.Canonicalize(input), Is.SameAs(input));
        }

        [Test]
        public void Canonicalize_TextWithTableCharacter_IsAllocated()
        {
            string input = "a\u2014b";
            Assert.That(CharacterMap.Canonicalize(input), Is.Not.SameAs(input));
        }

        // ── Invariant 5: never throws ─────────────────────────────────────────

        [TestCase("\uD800", TestName = "Canonicalize_LoneHighSurrogateAtStart_DoesNotThrow")]
        [TestCase("abc\uD800", TestName = "Canonicalize_LoneHighSurrogateAtEnd_DoesNotThrow")]
        [TestCase("ab\uDC00c", TestName = "Canonicalize_LoneLowSurrogateInMiddle_DoesNotThrow")]
        [TestCase("a\u00A0b\uD800", TestName = "Canonicalize_LoneSurrogateWithMapping_DoesNotThrow")]
        public void Canonicalize_NeverThrowsOnLoneSurrogate(string input)
        {
            string result = null;
            Assert.DoesNotThrow(() => result = CharacterMap.Canonicalize(input));
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void Canonicalize_LoneSurrogate_SkipsNfcButKeepsReplacements()
        {
            Assert.That(CharacterMap.Canonicalize("a\u00A0b\uD800"), Is.EqualTo("a b\uD800"));

            string withoutMapping = "ab\uD800";
            Assert.That(CharacterMap.Canonicalize(withoutMapping), Is.SameAs(withoutMapping));
        }

        // ── Invariant 2: canonical form survives a write-side replacement ─────

        [Test]
        public void Fallback_IsConsistentWithCanonicalize()
        {
            foreach (var entry in CharacterMap.Entries)
            {
                string fallback = CharacterMap.Fallback(entry.Key);
                Assert.That(
                    CharacterMap.Canonicalize(fallback),
                    Is.EqualTo(CharacterMap.Canonicalize(entry.Key.ToString())),
                    $"U+{(int)entry.Key:X4}");
            }
        }

        [Test]
        public void Fallback_ReturnsAsciiOnly()
        {
            foreach (var entry in CharacterMap.Entries)
            {
                string fallback = CharacterMap.Fallback(entry.Key);
                Assert.That(fallback.All(c => c <= 0x7F), Is.True, $"U+{(int)entry.Key:X4}");
                Assert.That(fallback.Length, Is.LessThanOrEqualTo(CharacterMap.MaxFallbackLength), $"U+{(int)entry.Key:X4}");
            }
        }

        [Test]
        public void MaxFallbackLength_MatchesTable()
        {
            Assert.That(
                CharacterMap.MaxFallbackLength,
                Is.EqualTo(CharacterMap.Entries.Values.Max(v => v.Length)));
            Assert.That(CharacterMap.MaxFallbackLength, Is.EqualTo(3));
        }

        [Test]
        public void Fallback_UnknownCharacter_BecomesQuestionMark()
        {
            Assert.That(CharacterMap.Fallback('\u2212'), Is.EqualTo("?"));
            Assert.That(CharacterMap.Fallback('\uD83D'), Is.EqualTo("?"));
        }

        // ── Invariant 3: the deleted set stays deletable ──────────────────────

        [Test]
        public void Fallback_DeletedCharacters_AreRemoved_NotQuestionMarked()
        {
            Assert.That(CharacterMap.Fallback('\u200B'), Is.Empty);
            Assert.That(CharacterMap.Fallback('\uFEFF'), Is.Empty);
        }

        [Test]
        public void IsDeleted_CoversOnlyZeroWidthSpaceAndBom()
        {
            Assert.That(CharacterMap.IsDeleted('\u200B'), Is.True);
            Assert.That(CharacterMap.IsDeleted('\uFEFF'), Is.True);
            Assert.That(CharacterMap.IsDeleted('\u2028'), Is.False);
            Assert.That(CharacterMap.IsDeleted('\u0085'), Is.False);
            Assert.That(CharacterMap.IsDeleted('\u200C'), Is.False);
            Assert.That(CharacterMap.IsDeleted('\u200D'), Is.False);
            Assert.That(CharacterMap.IsDeleted(' '), Is.False);
            Assert.That(CharacterMap.IsDeleted('\u00A0'), Is.False);
        }
    }
}
