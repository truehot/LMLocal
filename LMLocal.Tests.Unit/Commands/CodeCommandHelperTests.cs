using LMLocal.Commands;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Commands
{
    [TestFixture]
    public class CodeCommandHelperTests
    {
        // ────────────────────────────── WrapInCodeFence ──────────────────────────────

        [Test]
        public void WrapInCodeFence_ReturnsNull_ForNullCode()
        {
            Assert.That(CodeCommandHelper.WrapInCodeFence(null, "cs"), Is.Null);
        }

        [Test]
        public void WrapInCodeFence_ReturnsNull_ForWhitespaceCode()
        {
            Assert.That(CodeCommandHelper.WrapInCodeFence("   ", "cs"), Is.Null);
        }

        [Test]
        public void WrapInCodeFence_WithLanguage_CreatesFencedBlock()
        {
            string result = CodeCommandHelper.WrapInCodeFence("var x = 1;", "cs");

            Assert.That(result, Is.EqualTo("```cs\nvar x = 1;\n```"));
        }

        [Test]
        public void WrapInCodeFence_WithoutLanguage_UsesEmptyTag()
        {
            string result = CodeCommandHelper.WrapInCodeFence("plain text", null);

            Assert.That(result, Is.EqualTo("```\nplain text\n```"));
        }

        [Test]
        public void WrapInCodeFence_WithEmptyLanguage_UsesEmptyTag()
        {
            string result = CodeCommandHelper.WrapInCodeFence("plain text", "");

            Assert.That(result, Is.EqualTo("```\nplain text\n```"));
        }

        [Test]
        public void WrapInCodeFence_WithFileComment_IncludesCommentLine()
        {
            string result = CodeCommandHelper.WrapInCodeFence("code", "py", "// file: main.py");

            Assert.That(result, Is.EqualTo("```py\n// file: main.py\ncode\n```"));
        }

        [Test]
        public void WrapInCodeFence_MultilineCode_PreservesNewlines()
        {
            string code = "line1\nline2\nline3";
            string result = CodeCommandHelper.WrapInCodeFence(code, "js");

            Assert.That(result, Is.EqualTo("```js\nline1\nline2\nline3\n```"));
        }

        [Test]
        public void WrapInCodeFence_CodeWithBackticks_NotEscaped()
        {
            // The method doesn't escape backticks — this documents current behaviour
            string result = CodeCommandHelper.WrapInCodeFence("text with `backticks`", "md");

            Assert.That(result, Does.Contain("text with `backticks`"));
        }

        [Test]
        public void WrapInCodeFence_WithFileCommentAndLanguage_ProducesCorrectOrder()
        {
            string result = CodeCommandHelper.WrapInCodeFence(
                "using System;",
                "cs",
                "// file: src/Program.cs");

            Assert.That(result, Is.EqualTo("```cs\n// file: src/Program.cs\nusing System;\n```"));
        }
    }
}
