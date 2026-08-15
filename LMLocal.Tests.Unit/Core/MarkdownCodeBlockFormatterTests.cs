using LMLocal.Core.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class MarkdownCodeBlockFormatterTests
    {
        // ────────────────────────────── BuildFence ──────────────────────────────

        [Test]
        public void BuildFence_NullCode_ReturnsNull()
        {
            Assert.That(MarkdownCodeBlockFormatter.BuildFence(null, "cs"), Is.Null);
        }

        [Test]
        public void BuildFence_WhitespaceCode_ReturnsNull()
        {
            Assert.That(MarkdownCodeBlockFormatter.BuildFence("   ", "cs"), Is.Null);
        }

        [Test]
        public void BuildFence_WithLanguageAndComment_ProducesFencedBlock()
        {
            string result = MarkdownCodeBlockFormatter.BuildFence("code", "py", "// file: main.py");
            Assert.That(result, Is.EqualTo("````py\n// file: main.py\ncode\n````"));
        }

        [Test]
        public void BuildFence_EmptyLanguage_UsesEmptyTag()
        {
            string result = MarkdownCodeBlockFormatter.BuildFence("plain text", "");
            Assert.That(result, Is.EqualTo("````\nplain text\n````"));
        }

        [Test]
        public void BuildFence_NoComment_OmitsCommentLine()
        {
            string result = MarkdownCodeBlockFormatter.BuildFence("var x = 1;", "cs");
            Assert.That(result, Is.EqualTo("````cs\nvar x = 1;\n````"));
        }

        [Test]
        public void BuildFence_ContentWithTripleBackticks_DoesNotBreakFence()
        {
            // 4-backtick fences allow content containing triple backticks (e.g. embedded markdown).
            string result = MarkdownCodeBlockFormatter.BuildFence("```js\nconst x = 1;\n```", "md");

            Assert.That(result, Does.StartWith("````md"));
            Assert.That(result, Does.EndWith("````"));
            Assert.That(result, Does.Contain("```js\nconst x = 1;\n```"));
        }

        // ────────────────────────────── FormatFileAsMarkdown ──────────────────────────────

        [Test]
        public void FormatFileAsMarkdown_DerivesLanguageFromExtension()
        {
            string result = MarkdownCodeBlockFormatter.FormatFileAsMarkdown("using System;", "src/Program.cs", "src/Program.cs");
            Assert.That(result, Is.EqualTo("````csharp\n// file: src/Program.cs\nusing System;\n````"));
        }

        [Test]
        public void FormatFileAsMarkdown_PrefersDisplayPathOverFilePath()
        {
            string result = MarkdownCodeBlockFormatter.FormatFileAsMarkdown("x = 1;", "C:\\repo\\src\\App.cs", "src/App.cs");
            Assert.That(result, Does.Contain("// file: src/App.cs"));
            Assert.That(result, Does.Not.Contain("C:\\repo"));
        }

        [Test]
        public void FormatFileAsMarkdown_FallsBackToFilePath_WhenDisplayPathMissing()
        {
            string result = MarkdownCodeBlockFormatter.FormatFileAsMarkdown("x = 1;", "src/App.cs");
            Assert.That(result, Does.Contain("// file: src/App.cs"));
        }

        [Test]
        public void FormatFileAsMarkdown_NoPath_OmitsComment()
        {
            string result = MarkdownCodeBlockFormatter.FormatFileAsMarkdown("plain", null);
            Assert.That(result, Is.EqualTo("````\nplain\n````"));
        }

        [Test]
        public void FormatFileAsMarkdown_NullContent_ReturnsNull()
        {
            Assert.That(MarkdownCodeBlockFormatter.FormatFileAsMarkdown(null, "a.cs"), Is.Null);
        }

        [Test]
        public void FormatFileAsMarkdown_WhitespaceContent_ReturnsNull()
        {
            Assert.That(MarkdownCodeBlockFormatter.FormatFileAsMarkdown("   ", "a.cs"), Is.Null);
        }

        [Test]
        public void FormatFileAsMarkdown_Html_UsesHtmlCommentSyntax()
        {
            string result = MarkdownCodeBlockFormatter.FormatFileAsMarkdown("<p>hi</p>", "src/index.html", "src/index.html");
            Assert.That(result, Is.EqualTo("````html\n<!-- file: src/index.html -->\n<p>hi</p>\n````"));
        }

        [Test]
        public void FormatFileAsMarkdown_Python_UsesHashCommentSyntax()
        {
            string result = MarkdownCodeBlockFormatter.FormatFileAsMarkdown("print('hi')", "src/main.py", "src/main.py");
            Assert.That(result, Is.EqualTo("````python\n# file: src/main.py\nprint('hi')\n````"));
        }

        [Test]
        public void FormatFileAsMarkdown_Sql_UsesDoubleDashCommentSyntax()
        {
            string result = MarkdownCodeBlockFormatter.FormatFileAsMarkdown("SELECT 1;", "src/query.sql", "src/query.sql");
            Assert.That(result, Is.EqualTo("````sql\n-- file: src/query.sql\nSELECT 1;\n````"));
        }

        // ────────────────────────────── BuildTruncatedFileFence ──────────────────────────────

        [Test]
        public void BuildTruncatedFileFence_AddsTruncationNote()
        {
            string result = MarkdownCodeBlockFormatter.BuildTruncatedFileFence("huge.log");
            Assert.That(result, Does.Contain("````"));
            Assert.That(result, Does.Contain("# file: huge.log (content truncated, file too large)"));
            Assert.That(result, Does.Not.Contain("null"));
        }

        [Test]
        public void BuildTruncatedFileFence_Html_UsesHtmlCommentSyntax()
        {
            string result = MarkdownCodeBlockFormatter.BuildTruncatedFileFence("page.html");
            Assert.That(result, Is.EqualTo("````html\n<!-- file: page.html (content truncated, file too large) -->\n````"));
        }
    }
}
