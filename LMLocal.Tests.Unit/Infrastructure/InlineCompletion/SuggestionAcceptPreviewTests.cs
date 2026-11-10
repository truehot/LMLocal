using LMLocal.Infrastructure.Autocompletions.InlineCompletion;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.InlineCompletion
{
    [TestFixture]
    public class SuggestionAcceptPreviewTests
    {
        // ResolveLineEnding: перенос в позиции вставки → перенос первой строки → опция редактора.

        [Test]
        public void ResolveLineEnding_CaretLineBreak_Wins()
        {
            Assert.That(SuggestionTagger.ResolveLineEnding("\r\n", "\n", "\r\n"), Is.EqualTo("\r\n"));
        }

        [Test]
        public void ResolveLineEnding_FallsBackToFirstLineBreak()
        {
            Assert.That(SuggestionTagger.ResolveLineEnding("", "\n", "\r\n"), Is.EqualTo("\n"));
            Assert.That(SuggestionTagger.ResolveLineEnding(null, "\r\n", "\n"), Is.EqualTo("\r\n"));
        }

        [Test]
        public void ResolveLineEnding_FallsBackToEditorOption()
        {
            Assert.That(SuggestionTagger.ResolveLineEnding("", "", "\r\n"), Is.EqualTo("\r\n"));
            Assert.That(SuggestionTagger.ResolveLineEnding(null, null, "\n"), Is.EqualTo("\n"));
        }

        // PrepareInsertText: '\n' подсказки → окончания строк буфера.

        [Test]
        public void PrepareInsertText_Multiline_UsesBufferLineEnding()
        {
            Assert.That(SuggestionTagger.PrepareInsertText("a\nb\nc", "\r\n"), Is.EqualTo("a\r\nb\r\nc"));
        }

        [Test]
        public void PrepareInsertText_SingleLine_Unchanged()
        {
            Assert.That(SuggestionTagger.PrepareInsertText("abc", "\r\n"), Is.EqualTo("abc"));
        }

        [Test]
        public void PrepareInsertText_Null_ReturnsNull()
        {
            Assert.That(SuggestionTagger.PrepareInsertText(null, "\r\n"), Is.Null);
        }

        [Test]
        public void CollapsedPreview_Multiline_FirstLinePlusMarker()
        {
            Assert.That(SuggestionTagger.BuildCollapsedPreview("abc\ndef\nghi"), Is.EqualTo("abc  …+2"));
        }

        [Test]
        public void CollapsedPreview_EmptyFirstLine_MarkerOnly()
        {
            Assert.That(SuggestionTagger.BuildCollapsedPreview("\ndef"), Is.EqualTo("  …+1"));
        }

        [Test]
        public void CollapsedPreview_SingleLine_NoMarker()
        {
            Assert.That(SuggestionTagger.BuildCollapsedPreview("abc"), Is.EqualTo("abc"));
        }

        [Test]
        public void CollapsedPreview_TrailingNewline_CountsOneRestLine()
        {
            Assert.That(SuggestionTagger.BuildCollapsedPreview("abc\n"), Is.EqualTo("abc  …+1"));
        }
    }
}
