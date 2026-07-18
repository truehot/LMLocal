using LMLocal.Infrastructure.Autocompletions.InlineCompletion;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.InlineCompletion
{
    /// <summary>
    /// Unit tests for <see cref="SuggestionPostProcessor.Process"/>.
    /// Current implementation: trims whitespace, splits by '\n', caps at maxLines.
    /// </summary>
    [TestFixture]
    public class SuggestionPostProcessTests
    {
        private const int DefaultMaxLines = 5;

        // =========================================================================
        // Null / Empty / Whitespace
        // =========================================================================

        [Test]
        public void NullInput_ReturnsNull()
        {
            var result = SuggestionPostProcessor.Process(null, null, null, DefaultMaxLines);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void EmptyInput_ReturnsNull()
        {
            var result = SuggestionPostProcessor.Process("", null, null, DefaultMaxLines);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void WhitespaceOnly_ReturnsNull()
        {
            var result = SuggestionPostProcessor.Process("   \r\n  \t  ", null, null, DefaultMaxLines);
            Assert.That(result, Is.Null);
        }

        // =========================================================================
        // Trimming
        // =========================================================================

        [Test]
        public void TrimsLeadingWhitespace()
        {
            var result = SuggestionPostProcessor.Process("\r\n  \thello", null, null, DefaultMaxLines);
            Assert.That(result, Is.EqualTo(new[] { "hello" }));
        }

        [Test]
        public void TrimsTrailingWhitespace()
        {
            var result = SuggestionPostProcessor.Process("hello\r\n  ", null, null, DefaultMaxLines);
            Assert.That(result, Is.EqualTo(new[] { "hello" }));
        }

        [Test]
        public void TrimsBothSides()
        {
            var result = SuggestionPostProcessor.Process("  \r\nhello\r\n  ", null, null, DefaultMaxLines);
            Assert.That(result, Is.EqualTo(new[] { "hello" }));
        }

        [Test]
        public void TrimsTabsAndSpaces()
        {
            var result = SuggestionPostProcessor.Process("\t\t  foo()  \t", null, null, DefaultMaxLines);
            Assert.That(result, Is.EqualTo(new[] { "foo()" }));
        }

        // =========================================================================
        // Single line
        // =========================================================================

        [Test]
        public void SimpleText_ReturnsSingleElementArray()
        {
            var result = SuggestionPostProcessor.Process("return result;", null, null, DefaultMaxLines);
            Assert.That(result, Is.EqualTo(new[] { "return result;" }));
        }

        [Test]
        public void NoTrailingNewline_Works()
        {
            var result = SuggestionPostProcessor.Process("var x = 1;", null, null, DefaultMaxLines);
            Assert.That(result, Is.EqualTo(new[] { "var x = 1;" }));
        }

        // =========================================================================
        // Multiline
        // =========================================================================

        [Test]
        public void Multiline_ReturnsMultipleElements()
        {
            var result = SuggestionPostProcessor.Process(
                "var x = 1;\nvar y = 2;\nreturn x + y;",
                null, null, DefaultMaxLines);
            Assert.That(result, Is.EqualTo(new[] { "var x = 1;", "var y = 2;", "return x + y;" }));
        }

        [Test]
        public void Multiline_WithCarriageReturnNewline_SplitsByNewline()
        {
            var result = SuggestionPostProcessor.Process(
                "line1\r\nline2\r\nline3",
                null, null, DefaultMaxLines);
            // \r is kept at end of each line except last because Split('\n')
            Assert.That(result, Is.EqualTo(new[] { "line1\r", "line2\r", "line3" }));
        }

        // =========================================================================
        // MaxLines capping
        // =========================================================================

        [Test]
        public void LinesLessThanMaxLines_ReturnsAll()
        {
            var result = SuggestionPostProcessor.Process("a\nb\nc", null, null, 10);
            Assert.That(result, Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void LinesEqualToMaxLines_ReturnsAll()
        {
            var result = SuggestionPostProcessor.Process("a\nb\nc", null, null, 3);
            Assert.That(result, Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void LinesExceedMaxLines_Capped()
        {
            var result = SuggestionPostProcessor.Process("a\nb\nc\nd\ne\nf\ng", null, null, 3);
            Assert.That(result, Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void MaxLinesZero_ReturnsEmptyArray()
        {
            var result = SuggestionPostProcessor.Process("hello", null, null, 0);
            Assert.That(result, Is.EqualTo(System.Array.Empty<string>()));
        }

        [Test]
        public void MaxLinesOne_ReturnsFirstLineOnly()
        {
            var result = SuggestionPostProcessor.Process("first\nsecond\nthird", null, null, 1);
            Assert.That(result, Is.EqualTo(new[] { "first" }));
        }

        // =========================================================================
        // Prefix and suffix parameters (currently unused — verify passthrough)
        // =========================================================================

        [Test]
        public void PrefixAndSuffixParams_DoNotAffectOutput()
        {
            // Current implementation ignores prefix/suffix parameters.
            var without = SuggestionPostProcessor.Process("hello world", null, null, DefaultMaxLines);
            var with = SuggestionPostProcessor.Process("hello world", "some prefix", "some suffix", DefaultMaxLines);
            Assert.That(with, Is.EqualTo(without));
        }
    }
}
