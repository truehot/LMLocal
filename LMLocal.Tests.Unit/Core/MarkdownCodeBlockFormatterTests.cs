using System.Collections.Generic;
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
        public void BuildFence_EmptyLanguage_FallsBackToTextTag()
        {
            string result = MarkdownCodeBlockFormatter.BuildFence("plain text", "");
            Assert.That(result, Is.EqualTo("````text\nplain text\n````"));
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
            // No file path means no recognised language, so the fence falls back to the "text" tag.
            Assert.That(result, Is.EqualTo("````text\nplain\n````"));
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

        [Test]
        public void BuildTruncatedFileFence_UnknownExtension_FallsBackToTextTag()
        {
            string result = MarkdownCodeBlockFormatter.BuildTruncatedFileFence("notes.txt");
            Assert.That(result, Is.EqualTo("````text\n# file: notes.txt (content truncated, file too large)\n````"));
        }

        // ────────────────────── Parse side (fences are read back) ──────────────────────

        [Test]
        public void ParseFileComment_ReturnsPathForEveryBuiltCommentSyntax()
        {
            Assert.That(MarkdownCodeBlockFormatter.ParseFileComment("// file: src/App.cs"), Is.EqualTo("src/App.cs"));
            Assert.That(MarkdownCodeBlockFormatter.ParseFileComment("# file: Docs/plan.md"), Is.EqualTo("Docs/plan.md"));
            Assert.That(MarkdownCodeBlockFormatter.ParseFileComment("-- file: q.sql"), Is.EqualTo("q.sql"));
            Assert.That(MarkdownCodeBlockFormatter.ParseFileComment("/* file: a.css */"), Is.EqualTo("a.css"));
            Assert.That(MarkdownCodeBlockFormatter.ParseFileComment("<!-- file: a.html -->"), Is.EqualTo("a.html"));
        }

        [Test]
        public void ParseFileComment_StripsTheTruncationSuffix()
        {
            string fence = MarkdownCodeBlockFormatter.BuildTruncatedFileFence("page.html");

            Assert.That(MarkdownCodeBlockFormatter.ParseFileComment(fence.Split('\n')[1]), Is.EqualTo("page.html"));
        }

        [Test]
        public void ParseFileComment_OrdinaryLine_ReturnsNull()
        {
            Assert.That(MarkdownCodeBlockFormatter.ParseFileComment("            var value = 1;"), Is.Null);
            Assert.That(MarkdownCodeBlockFormatter.ParseFileComment(null), Is.Null);
            Assert.That(MarkdownCodeBlockFormatter.ParseFileComment("# file:"), Is.Null);
        }

        [Test]
        public void RoundTrip_FormatFileAsMarkdown_IsParsedBackToTheSamePath()
        {
            string fence = MarkdownCodeBlockFormatter.FormatFileAsMarkdown("class A { }", "src/App.cs");

            Assert.That(MarkdownCodeBlockFormatter.ParseFileComment(fence.Split('\n')[1]), Is.EqualTo("src/App.cs"));
        }

        [Test]
        public void RemoveCodeBlocks_RemovesEveryFence_CollectsOnlyFilePaths()
        {
            string text = "look at this\n\n"
                + MarkdownCodeBlockFormatter.FormatFileAsMarkdown("class A { }", "src/App.cs")
                + "\n\n```csharp\nvar x = 1;\n```";

            var paths = new List<string>();
            string result = MarkdownCodeBlockFormatter.RemoveCodeBlocks(text, paths);

            Assert.That(result, Does.Contain("look at this"));
            Assert.That(result, Does.Not.Contain("class A { }"));
            Assert.That(result, Does.Not.Contain("var x = 1;"), "a plain code block is removed too");
            Assert.That(paths, Is.EqualTo(new[] { "src/App.cs" }));
        }

        [Test]
        public void RemoveCodeBlocks_BlockAtTheStart_LeavesTheRest()
        {
            string text = MarkdownCodeBlockFormatter.FormatFileAsMarkdown("content", "a.md") + "\nafter";

            var paths = new List<string>();
            string result = MarkdownCodeBlockFormatter.RemoveCodeBlocks(text, paths);

            Assert.That(result.Trim(), Is.EqualTo("after"));
            Assert.That(paths, Is.EqualTo(new[] { "a.md" }));
        }

        [Test]
        public void RemoveCodeBlocks_UnclosedFence_RemovesToTheEnd()
        {
            string text = "before\n\n````csharp\n// file: src/App.cs\nclass A { }";

            var paths = new List<string>();
            string result = MarkdownCodeBlockFormatter.RemoveCodeBlocks(text, paths);

            Assert.That(result.Trim(), Is.EqualTo("before"));
            Assert.That(paths, Is.EqualTo(new[] { "src/App.cs" }));
        }

        [Test]
        public void RemoveCodeBlocks_LongerClosingFence_ClosesTheBlock()
        {
            string text = "````text\n# file: a.md\nbody\n`````\nafter";

            var paths = new List<string>();
            string result = MarkdownCodeBlockFormatter.RemoveCodeBlocks(text, paths);

            Assert.That(result.Trim(), Is.EqualTo("after"));
            Assert.That(paths, Is.EqualTo(new[] { "a.md" }));
        }

        [Test]
        public void RemoveCodeBlocks_IndentedFence_IsRecognised()
        {
            string text = "   ````csharp\n   // file: src/App.cs\n   code\n   ````\nafter";

            var paths = new List<string>();
            string result = MarkdownCodeBlockFormatter.RemoveCodeBlocks(text, paths);

            Assert.That(result.Trim(), Is.EqualTo("after"));
            Assert.That(paths, Is.EqualTo(new[] { "src/App.cs" }));
        }

        [Test]
        public void RemoveCodeBlocks_KeepsProseAndInlineCode()
        {
            string text = "done in `SearchChatHistory`\n\n````csharp\npublic void Search() { }\n````\n\nand more";

            string result = MarkdownCodeBlockFormatter.RemoveCodeBlocks(text);

            Assert.That(result, Does.Contain("`SearchChatHistory`"));
            Assert.That(result, Does.Not.Contain("public void Search()"));
            Assert.That(result, Does.Contain("and more"));
        }

        [Test]
        public void RemoveCodeBlocks_NoBlocks_ReturnsTextUnchanged()
        {
            string text = "plain prose with `inline` code";

            Assert.That(MarkdownCodeBlockFormatter.RemoveCodeBlocks(text), Is.EqualTo(text));
        }
    }
}
