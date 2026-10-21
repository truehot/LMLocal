using System.Collections.Generic;
using System.Text;
using LMLocal.Application.SubAgents.ReadRefs;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    /// <summary>
    /// Direct coverage for the single rendering source of truth: numbered blocks (tool markdown in chat history)
    /// and verbatim fenced blocks with a header (ref expansion, §14.1).
    /// </summary>
    [TestFixture]
    public class FileContentRendererTests
    {
        private static List<NumberedLine> Lines(params (int Number, string Text)[] items)
        {
            var list = new List<NumberedLine>(items.Length);
            foreach (var (Number, Text) in items)
                list.Add(new NumberedLine(Number, Text));
            return list;
        }

        // ---------------------------------------------------------------------
        // AppendNumberedLines / AppendVerbatimLines (allocation-free direct append)
        // ---------------------------------------------------------------------

        [Test]
        public void AppendNumberedLines_PrefixesNumberedLines_AndEmitsNonPositiveVerbatim()
        {
            var sb = new StringBuilder();

            FileContentRenderer.AppendNumberedLines(sb, Lines((1, "alpha"), (2, "beta"), (0, "raw")));

            Assert.That(sb.ToString(), Is.EqualTo("1: alpha\n2: beta\nraw"));
        }

        [Test]
        public void AppendNumberedLines_NullText_IsAppendedAsEmpty()
        {
            var sb = new StringBuilder();

            FileContentRenderer.AppendNumberedLines(sb, Lines((1, null)));

            Assert.That(sb.ToString(), Is.EqualTo("1: "));
        }

        [Test]
        public void AppendNumberedLines_Null_AppendsNothing()
        {
            var sb = new StringBuilder();
            sb.Append("x");

            FileContentRenderer.AppendNumberedLines(sb, null);

            Assert.That(sb.ToString(), Is.EqualTo("x"));
        }

        [Test]
        public void AppendVerbatimLines_DoesNotAddLineNumbers()
        {
            var sb = new StringBuilder();

            FileContentRenderer.AppendVerbatimLines(sb, Lines((1, "alpha"), (2, "beta")));

            Assert.That(sb.ToString(), Is.EqualTo("alpha\nbeta"));
        }

        [Test]
        public void AppendVerbatimLines_Null_AppendsNothing()
        {
            var sb = new StringBuilder();
            sb.Append("x");

            FileContentRenderer.AppendVerbatimLines(sb, null);

            Assert.That(sb.ToString(), Is.EqualTo("x"));
        }


        // ---------------------------------------------------------------------
        // AppendFileBlock (tool markdown in chat history): bold header + numbered fence
        // ---------------------------------------------------------------------

        [Test]
        public void AppendFileBlock_RendersBoldHeaderAndNumberedFence()
        {
            var sb = new StringBuilder();

            FileContentRenderer.AppendFileBlock(sb, @"LMLocal\Core\StreamChunk.cs", Lines((1, "a"), (2, "b")), hasMore: false);

            var text = sb.ToString().Replace("\r\n", "\n");
            Assert.That(text, Does.Contain("**`LMLocal\\Core\\StreamChunk.cs`**"));
            Assert.That(text, Does.Contain("````csharp"));
            Assert.That(text, Does.Contain("1: a\n2: b"));
            Assert.That(text, Does.Not.Contain("truncated"));
        }

        [Test]
        public void AppendFileBlock_HasMore_AddsTruncatedMarker_AndLanguageTag()
        {
            var sb = new StringBuilder();

            FileContentRenderer.AppendFileBlock(sb, "readme.md", Lines((1, "x")), hasMore: true);

            var text = sb.ToString().Replace("\r\n", "\n");
            Assert.That(text, Does.Contain("(truncated, more lines available)"));
            Assert.That(text, Does.Contain("````markdown"));
        }

        // ---------------------------------------------------------------------
        // AppendRefContentBlock (ref expansion, §14.1): File: header + verbatim fence, no trailing newline
        // ---------------------------------------------------------------------

        [Test]
        public void AppendRefContentBlock_RendersHeaderAndVerbatimFence_WithoutTrailingNewline()
        {
            var sb = new StringBuilder();

            FileContentRenderer.AppendRefContentBlock(
                sb, @"src\Program.cs", 30, 31, Lines((30, "line 30"), (31, "line 31")));

            Assert.That(
                sb.ToString(),
                Is.EqualTo("File: src\\Program.cs, lines 30-31\n````csharp\nline 30\nline 31\n````"));
        }

        [Test]
        public void AppendRefContentBlock_DoesNotAddLineNumberPrefixes()
        {
            var sb = new StringBuilder();

            FileContentRenderer.AppendRefContentBlock(sb, "a.cs", 1, 1, Lines((1, "return x;")));

            Assert.That(sb.ToString(), Does.Not.Contain("1: return x;"));
        }

        [Test]
        public void AppendRefContentBlock_UnknownExtension_UsesTextLanguage()
        {
            var sb = new StringBuilder();

            FileContentRenderer.AppendRefContentBlock(sb, "LICENSE", 1, 1, Lines((1, "hello")));

            Assert.That(sb.ToString(), Is.EqualTo("File: LICENSE, lines 1-1\n````text\nhello\n````"));
        }
    }
}
