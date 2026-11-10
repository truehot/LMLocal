using LMLocal.Infrastructure.Autocompletions.InlineCompletion;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.InlineCompletion
{
    [TestFixture]
    public class SuggestionPostProcessMultilineTests
    {
        [Test]
        public void LeadingLf_KeptWhenCaretLineHasCode()
        {
            var r = SuggestionPostProcessor.ProcessMultiline("\n{\n    return;\n}", 10, "", caretLineIsBlank: false);
            Assert.That(r, Is.EqualTo("\n{\n    return;\n}"));
        }

        [Test]
        public void LeadingLf_DroppedWhenCaretLineBlank()
        {
            var r = SuggestionPostProcessor.ProcessMultiline("\nreturn x;", 10, "", caretLineIsBlank: true);
            Assert.That(r, Is.EqualTo("return x;"));
        }

        [Test]
        public void Crlf_NormalizedToLf()
        {
            var r = SuggestionPostProcessor.ProcessMultiline("a\r\nb", 10, "", false);
            Assert.That(r, Is.EqualTo("a\nb"));
        }

        [Test]
        public void TrailingBlankLines_Trimmed()
        {
            var r = SuggestionPostProcessor.ProcessMultiline("a\nb\n\n", 10, "", false);
            Assert.That(r, Is.EqualTo("a\nb"));
        }

        [Test]
        public void SuffixOverlap_ClosingBraceRemoved()
        {
            var r = SuggestionPostProcessor.ProcessMultiline("    return;\n}", 10, "\r\n}\r\n", false);
            Assert.That(r, Is.EqualTo("    return;"));
        }

        [Test]
        public void SuffixOverlap_FirstLineNeverRemoved()
        {
            var r = SuggestionPostProcessor.ProcessMultiline("}", 10, "}", false);
            Assert.That(r, Is.EqualTo("}"));
        }

        [Test]
        public void MaxLines_Applied()
        {
            var r = SuggestionPostProcessor.ProcessMultiline("1\n2\n3\n4", 2, "", false);
            Assert.That(r, Is.EqualTo("1\n2"));
        }

        [Test]
        public void NullOrEmpty_ReturnsNull()
        {
            Assert.That(SuggestionPostProcessor.ProcessMultiline(null, 10, "", false), Is.Null);
            Assert.That(SuggestionPostProcessor.ProcessMultiline("", 10, "", false), Is.Null);
        }

        [Test]
        public void WhitespaceOnly_ReturnsNull()
        {
            Assert.That(SuggestionPostProcessor.ProcessMultiline("  \n\t\n", 10, "", false), Is.Null);
        }
    }
}
