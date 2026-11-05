using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common
{
    [TestFixture]
    public class JsonEscapesTests
    {
        [TestCase("say \\\"hi\\\"", "say \"hi\"")]
        [TestCase("C:\\\\tmp", "C:\\tmp")]
        [TestCase("a\\nb", "a\nb")]
        [TestCase("a\\r\\nb", "a\r\nb")]
        [TestCase("col1\\tcol2", "col1\tcol2")]
        [TestCase("a\\/b", "a/b")]
        [TestCase("\\\\\\\"", "\\\"")]  // \\\" -> \"  (single left-to-right pass)
        [TestCase("\\\\n", "\\n")]      // \\n  -> \n literal, not a newline
        public void TryUnescape_RemovesOneLevel(string input, string expected)
        {
            Assert.That(JsonEscapes.TryUnescape(input, out var result), Is.True);
            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase("plain text")]
        [TestCase("regex \\d+ \\w")]
        [TestCase("trailing \\")]
        [TestCase("")]
        [TestCase(null)]
        public void TryUnescape_NothingToUnescape_ReturnsFalseAndKeepsText(string input)
        {
            Assert.That(JsonEscapes.TryUnescape(input, out var result), Is.False);
            Assert.That(result, Is.EqualTo(input));
        }
    }
}
