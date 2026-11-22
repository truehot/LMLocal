using System;
using LMLocal.Core.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Core
{
    /// <summary>
    /// Truncation rules shared by Application (session summaries) and Infrastructure (search results).
    /// </summary>
    [TestFixture]
    public class PromptTextTests
    {
        [Test]
        public void Truncate_AboveLimit_TruncatesWithEllipsis()
        {
            var content = new string('x', 10);

            var result = PromptText.Truncate(content, 4);

            Assert.That(result, Is.EqualTo("xxxx..."));
        }

        [Test]
        public void Truncate_AtOrBelowLimit_ReturnsAsIs()
        {
            Assert.That(PromptText.Truncate("abcd", 4), Is.EqualTo("abcd"));
            Assert.That(PromptText.Truncate("abc", 4), Is.EqualTo("abc"));
        }

        [TestCase(null)]
        [TestCase("")]
        public void Truncate_NullOrEmpty_ReturnsEmpty(string content)
        {
            Assert.That(PromptText.Truncate(content, 5), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Truncate_NegativeLimit_Throws()
        {
            Assert.That(() => PromptText.Truncate("abc", -1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void TruncatePrompt_UsesTheDialogLimit()
        {
            var result = PromptText.TruncatePrompt(new string('x', PromptText.MaxPromptLength + 1));

            Assert.That(result.Length, Is.EqualTo(PromptText.MaxPromptLength + 3));
            Assert.That(result.EndsWith("..."), Is.True);
        }

        [TestCase(PromptText.MaxPromptLength - 1)]
        [TestCase(PromptText.MaxPromptLength)]
        public void TruncatePrompt_AtOrBelowTheLimit_IsNotTruncated(int length)
        {
            var content = new string('x', length);

            Assert.That(PromptText.TruncatePrompt(content), Is.EqualTo(content));
        }
    }
}
