using LMLocal.Infrastructure.Autocompletions.InlineCompletion;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.InlineCompletion
{
    /// <summary>
    /// Unit tests for <see cref="SuggestionPostProcessor.Process"/>.
    /// Current implementation: trims whitespace, caps at maxLines lines, returns a single string.
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
            var result = SuggestionPostProcessor.Process(null, DefaultMaxLines);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void EmptyInput_ReturnsNull()
        {
            var result = SuggestionPostProcessor.Process("", DefaultMaxLines);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void WhitespaceOnly_ReturnsNull()
        {
            var result = SuggestionPostProcessor.Process("   \r\n  \t  ", DefaultMaxLines);
            Assert.That(result, Is.Null);
        }

        // =========================================================================
        // Trimming
        // =========================================================================

        [Test]
        public void TrimsLeadingWhitespace()
        {
            var result = SuggestionPostProcessor.Process("\r\n  \thello", DefaultMaxLines);
            Assert.That(result, Is.EqualTo("hello"));
        }

        [Test]
        public void TrimsTrailingWhitespace()
        {
            var result = SuggestionPostProcessor.Process("hello\r\n  ", DefaultMaxLines);
            Assert.That(result, Is.EqualTo("hello"));
        }

        [Test]
        public void TrimsBothSides()
        {
            var result = SuggestionPostProcessor.Process("  \r\nhello\r\n  ", DefaultMaxLines);
            Assert.That(result, Is.EqualTo("hello"));
        }

        [Test]
        public void TrimsTabsAndSpaces()
        {
            var result = SuggestionPostProcessor.Process("\t\t  foo()  \t", DefaultMaxLines);
            Assert.That(result, Is.EqualTo("foo()"));
        }

        // =========================================================================
        // Single line
        // =========================================================================

        [Test]
        public void SimpleText_ReturnsSingleElementArray()
        {
            var result = SuggestionPostProcessor.Process("return result;", DefaultMaxLines);
            Assert.That(result, Is.EqualTo("return result;"));
        }

        [Test]
        public void NoTrailingNewline_Works()
        {
            var result = SuggestionPostProcessor.Process("var x = 1;", DefaultMaxLines);
            Assert.That(result, Is.EqualTo("var x = 1;"));
        }

        // =========================================================================
        // Multiline
        // =========================================================================

        [Test]
        public void Multiline_ReturnsMultipleElements()
        {
            var result = SuggestionPostProcessor.Process(
                "var x = 1;\nvar y = 2;\nreturn x + y;", DefaultMaxLines);
            Assert.That(result, Is.EqualTo("var x = 1;\nvar y = 2;\nreturn x + y;"));
        }

        [Test]
        public void Multiline_WithCarriageReturnNewline_SplitsByNewline()
        {
            var result = SuggestionPostProcessor.Process(
                "line1\r\nline2\r\nline3", DefaultMaxLines);
            // \r is preserved inside the text; clipped by \n only
            Assert.That(result, Is.EqualTo("line1\r\nline2\r\nline3"));
        }

        // =========================================================================
        // MaxLines capping
        // =========================================================================

        [Test]
        public void LinesLessThanMaxLines_ReturnsAll()
        {
            var result = SuggestionPostProcessor.Process("a\nb\nc", 10);
            Assert.That(result, Is.EqualTo("a\nb\nc"));
        }

        [Test]
        public void LinesEqualToMaxLines_ReturnsAll()
        {
            var result = SuggestionPostProcessor.Process("a\nb\nc", 3);
            Assert.That(result, Is.EqualTo("a\nb\nc"));
        }

        [Test]
        public void LinesExceedMaxLines_Capped()
        {
            var result = SuggestionPostProcessor.Process("a\nb\nc\nd\ne\nf\ng", 3);
            Assert.That(result, Is.EqualTo("a\nb\nc"));
        }

        [Test]
        public void MaxLinesZero_ReturnsEmptyString()
        {
            var result = SuggestionPostProcessor.Process("hello", 0);
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        [Test]
        public void MaxLinesOne_ReturnsFirstLineOnly()
        {
            var result = SuggestionPostProcessor.Process("first\nsecond\nthird", 1);
            Assert.That(result, Is.EqualTo("first"));
        }

        // =========================================================================
        // Prefix and suffix parameters (currently unused — verify passthrough)
        // =========================================================================

    }
}
