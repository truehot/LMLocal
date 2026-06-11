using LMLocal.Core.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class MarkdownStripperTests
    {
        [Test]
        public void Strip_RemovesMarkdownSyntax_ReturnsPlainText()
        {
            var input = "# Header\n**bold** _italic_ [link](url)\n- item";
            var expected = "Header\nbold italic link\nitem";

            var result = MarkdownStripper.Strip(input);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void Strip_EmptyOrNull_ReturnsInput()
        {
            Assert.That(MarkdownStripper.Strip(null), Is.Null);
            Assert.That(MarkdownStripper.Strip(""), Is.EqualTo(""));
        }
    }
}
