using LMLocal.Core.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Internal
{
    /// <summary>
    /// The image placeholder is the only trace of an image message in the log, so it must be recognised exactly -
    /// including the form where the log writes the prompt in front of it.
    /// </summary>
    [TestFixture]
    public class ChatMessagePlaceholdersTests
    {
        [TestCase(1, "[1 image attached - not available in this session]")]
        [TestCase(2, "[2 images attached - not available in this session]")]
        [TestCase(10, "[10 images attached - not available in this session]")]
        public void FormatImagePlaceholder_UsesSingularAndPlural(int count, string expected)
        {
            Assert.That(ChatMessagePlaceholders.FormatImagePlaceholder(count), Is.EqualTo(expected));
        }

        [TestCase("[1 image attached - not available in this session]")]
        [TestCase("[2 images attached - not available in this session]")]
        [TestCase("look at this\n\n[2 images attached - not available in this session]")]
        [TestCase("look at this\r\n\r\n[2 images attached - not available in this session]")]
        public void IsImagePlaceholder_True_WhenTheLastLineIsAPlaceholder(string text)
        {
            Assert.That(ChatMessagePlaceholders.IsImagePlaceholder(text), Is.True);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("plain prompt")]
        [TestCase("[2 images attached - not available in this session]\n\nand then a question")]
        [TestCase("[no images attached - not available in this session]")]
        [TestCase("[0 images attached - not available in this session]")]
        [TestCase("[2 images attached - something else]")]
        public void IsImagePlaceholder_False_ForAnythingElse(string text)
        {
            Assert.That(ChatMessagePlaceholders.IsImagePlaceholder(text), Is.False);
        }
    }
}
