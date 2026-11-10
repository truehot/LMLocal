using LMLocal.Core.Models;
using LMLocal.Infrastructure.Autocompletions.InlineCompletion;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.InlineCompletion
{
    /// <summary>
    /// Helpers shared by the legacy tagger and the native source: closing-character handling,
    /// post-processing with text after the caret, and the backend switch.
    /// </summary>
    [TestFixture]
    public class SuggestionSharedHelpersTests
    {
        // ---- IsOnlyClosingChars ----

        [TestCase("")]
        [TestCase(null)]
        [TestCase("   ")]
        [TestCase(")")]
        [TestCase(");")]
        [TestCase(" )] ")]
        [TestCase("\"")]
        public void IsOnlyClosingChars_True(string after)
        {
            Assert.That(SuggestionTagger.IsOnlyClosingChars(after), Is.True);
        }

        [TestCase("x")]
        [TestCase(") + 1")]
        [TestCase("(")]
        public void IsOnlyClosingChars_False(string after)
        {
            Assert.That(SuggestionTagger.IsOnlyClosingChars(after), Is.False);
        }

        // ---- TrimClosingOverlap ----

        [Test]
        public void TrimClosingOverlap_RemovesRepeatedCloser()
        {
            Assert.That(SuggestionTagger.TrimClosingOverlap("bar)", ")"), Is.EqualTo("bar"));
        }

        [Test]
        public void TrimClosingOverlap_RemovesLongestRepeatedPrefixOfTail()
        {
            Assert.That(SuggestionTagger.TrimClosingOverlap("bar);", ");"), Is.EqualTo("bar"));
            Assert.That(SuggestionTagger.TrimClosingOverlap("bar)", ");"), Is.EqualTo("bar"));
        }

        [Test]
        public void TrimClosingOverlap_NoOverlap_Unchanged()
        {
            Assert.That(SuggestionTagger.TrimClosingOverlap("bar", ")"), Is.EqualTo("bar"));
        }

        [Test]
        public void TrimClosingOverlap_IgnoresWhitespaceAfterCaret()
        {
            Assert.That(SuggestionTagger.TrimClosingOverlap("bar) ", " )  "), Is.EqualTo("bar"));
        }

        [Test]
        public void TrimClosingOverlap_EmptyInputs_Unchanged()
        {
            Assert.That(SuggestionTagger.TrimClosingOverlap("bar", ""), Is.EqualTo("bar"));
            Assert.That(SuggestionTagger.TrimClosingOverlap(null, ")"), Is.Null);
        }

        // ---- PostProcessSuggestion ----

        [Test]
        public void PostProcess_NothingAfterCaret_KeepsMultiline()
        {
            var r = SuggestionTagger.PostProcessSuggestion("a\nb", "", caretLineIsBlank: false, textAfterCaret: "");
            Assert.That(r, Is.EqualTo("a\nb"));
        }

        [Test]
        public void PostProcess_TextAfterCaret_FirstLineWithoutRepeatedCloser()
        {
            var r = SuggestionTagger.PostProcessSuggestion("bar)\nnext();", ")", caretLineIsBlank: false, textAfterCaret: ")");
            Assert.That(r, Is.EqualTo("bar"));
        }

        [Test]
        public void PostProcess_OnlyCloserSuggested_ReturnsNull()
        {
            var r = SuggestionTagger.PostProcessSuggestion(")", ")", caretLineIsBlank: false, textAfterCaret: ")");
            Assert.That(r, Is.Null);
        }

        // ---- TryConsume at line indentation (typing in virtual space after Enter) ----

        [TestCase("        f", "foo();", "oo();")]
        [TestCase("    f", "        foo();", "oo();")]
        [TestCase("\tF", "foo();", "oo();")]
        [TestCase("        foo", "foo();", "();")]
        public void TryConsume_AtIndentation_IgnoresLeadingWhitespace(string inserted, string suggestion, string expected)
        {
            Assert.That(SuggestionTagger.TryConsume(suggestion, inserted, atLineIndentation: true, out string remainder), Is.True);
            Assert.That(remainder, Is.EqualTo(expected));
        }

        [Test]
        public void TryConsume_AtIndentation_Mismatch_ReturnsFalse()
        {
            Assert.That(SuggestionTagger.TryConsume("foo();", "        x", atLineIndentation: true, out _), Is.False);
        }

        [Test]
        public void TryConsume_NotAtIndentation_LeadingWhitespaceMustMatch()
        {
            Assert.That(SuggestionTagger.TryConsume("foo();", "        f", atLineIndentation: false, out _), Is.False);
        }

        [Test]
        public void TryConsume_AtIndentation_WhitespaceOnly_UsesRegularRule()
        {
            Assert.That(SuggestionTagger.TryConsume("    foo();", "    ", atLineIndentation: true, out string remainder), Is.True);
            Assert.That(remainder, Is.EqualTo("foo();"));
        }

        // ---- Duplicate of the following code on a blank caret line ----

        [Test]
        public void ProcessMultiline_BlankCaretLine_SuggestionRepeatsNextLine_IsDropped()
        {
            // Enter after a method's "}" with the class's "}" below: the model suggests "}" again.
            var r = SuggestionPostProcessor.ProcessMultiline("}", 10, "\r\n\r\n\r\n}\r\n", caretLineIsBlank: true);
            Assert.That(r, Is.Null);
        }

        [Test]
        public void ProcessMultiline_BlankCaretLine_NewCodeThenRepeat_KeepsNewCode()
        {
            var r = SuggestionPostProcessor.ProcessMultiline("[Test]\n}", 10, "\r\n}\r\n", caretLineIsBlank: true);
            Assert.That(r, Is.EqualTo("[Test]"));
        }

        [Test]
        public void ProcessMultiline_CodeOnCaretLine_FirstLineKept()
        {
            // The first line continues the caret line, so it is never treated as a duplicate.
            var r = SuggestionPostProcessor.ProcessMultiline("}", 10, "\r\n}\r\n", caretLineIsBlank: false);
            Assert.That(r, Is.EqualTo("}"));
        }

        // ---- NormalizeNewLines ----

        [Test]
        public void NormalizeNewLines_CrLfAndCr()
        {
            Assert.That(SuggestionTagger.NormalizeNewLines("a\r\nb\rc\n"), Is.EqualTo("a\nb\nc\n"));
        }

        // ---- InlineCompletionBackend ----

        [TestCase("native", true)]
        [TestCase("Native", true)]
        [TestCase(" native ", true)]
        [TestCase("legacy", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsNativeBackend(string backend, bool expected)
        {
            Assert.That(InlineCompletionBackend.IsNativeBackend(backend), Is.EqualTo(expected));
        }

        [Test]
        public void Apply_SetsIsNative()
        {
            try
            {
                InlineCompletionBackend.Apply(new AutocompletionsConfig { Backend = "native" });
                Assert.That(InlineCompletionBackend.IsNative, Is.True);

                InlineCompletionBackend.Apply(new AutocompletionsConfig { Backend = "legacy" });
                Assert.That(InlineCompletionBackend.IsNative, Is.False);

                InlineCompletionBackend.Apply(null);
                Assert.That(InlineCompletionBackend.IsNative, Is.False);
            }
            finally
            {
                InlineCompletionBackend.Apply(new AutocompletionsConfig());
            }
        }

        [Test]
        public void ConfigDefaults_LegacyBackend()
        {
            var config = new AutocompletionsConfig();
            Assert.That(config.Backend, Is.EqualTo("legacy"));
            Assert.That(InlineCompletionBackend.IsNativeBackend(config.Backend), Is.False);
        }
    }
}
