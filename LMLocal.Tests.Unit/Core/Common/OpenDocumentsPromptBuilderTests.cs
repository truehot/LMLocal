using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LMLocal.Core.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class OpenDocumentsPromptBuilderTests
    {
        // ────────────────────────────── helpers ──────────────────────────────

        private static OpenDocumentsPromptBuilder.DocumentItem Item(string path, string content)
        {
            return new OpenDocumentsPromptBuilder.DocumentItem(path, () => content);
        }

        private static int CountOccurrences(string text, string token)
        {
            return text.Split(new[] { token }, StringSplitOptions.None).Length - 1;
        }

        // ────────────────────────────── empty input ──────────────────────────────

        [Test]
        public void Build_NullItems_ReturnsNull()
        {
            Assert.That(OpenDocumentsPromptBuilder.Build(null), Is.Null);
        }

        [Test]
        public void Build_NoItems_ReturnsNull()
        {
            string result = OpenDocumentsPromptBuilder.Build(
                new List<OpenDocumentsPromptBuilder.DocumentItem>());

            Assert.That(result, Is.Null);
        }

        [Test]
        public void Build_OnlySkippedItems_ReturnsNull()
        {
            string result = OpenDocumentsPromptBuilder.Build(new[]
            {
                Item(null, "content"),
                Item(Path.Combine("src", "bin", "output.cs"), "content")
            });

            Assert.That(result, Is.Null);
        }

        // ────────────────────────────── fence language ──────────────────────────────

        [Test]
        public void Build_HtmlFile_UsesHtmlFenceAndHtmlComment()
        {
            string result = OpenDocumentsPromptBuilder.Build(new[]
            {
                Item("src/index.html", "<p>hi</p>")
            });

            Assert.That(result, Is.EqualTo("````html\n<!-- file: src/index.html -->\n<p>hi</p>\n````"));
        }

        [Test]
        public void Build_CssFile_UsesCssFenceAndCssComment()
        {
            string result = OpenDocumentsPromptBuilder.Build(new[]
            {
                Item("src/app.css", "body { margin: 0; }")
            });

            Assert.That(result, Is.EqualTo("````css\n/* file: src/app.css */\nbody { margin: 0; }\n````"));
        }

        [Test]
        public void Build_UnknownExtension_FallsBackToTextFence()
        {
            string result = OpenDocumentsPromptBuilder.Build(new[]
            {
                Item("LICENSE", "MIT")
            });

            Assert.That(result, Does.StartWith("````text\n"));
            Assert.That(result, Does.Contain("# file: LICENSE"));
        }

        // ────────────────────────────── joining ──────────────────────────────

        [Test]
        public void Build_MultipleFiles_AreSeparatedByBlankLine()
        {
            string result = OpenDocumentsPromptBuilder.Build(new[]
            {
                Item("a.cs", "class A {}"),
                Item("b.cs", "class B {}")
            });

            Assert.That(result, Does.Contain("class A {}"));
            Assert.That(result, Does.Contain("class B {}"));
            Assert.That(result, Does.Contain("````\n\n````csharp"));
        }

        [Test]
        public void Build_PreservesDocumentOrder()
        {
            string result = OpenDocumentsPromptBuilder.Build(new[]
            {
                Item("first.cs", "FIRST"),
                Item("second.cs", "SECOND")
            });

            Assert.That(result.IndexOf("FIRST", StringComparison.Ordinal),
                Is.LessThan(result.IndexOf("SECOND", StringComparison.Ordinal)));
        }

        // ────────────────────────────── skipping ──────────────────────────────

        [Test]
        public void Build_DocumentWithoutPath_IsSkipped()
        {
            string result = OpenDocumentsPromptBuilder.Build(new[]
            {
                Item(null, "unsaved content"),
                Item("kept.cs", "class Kept {}")
            });

            Assert.That(result, Does.Not.Contain("unsaved content"));
            Assert.That(result, Does.Contain("class Kept {}"));
        }

        [Test]
        public void Build_ExcludedPath_IsSkipped()
        {
            string result = OpenDocumentsPromptBuilder.Build(new[]
            {
                Item(Path.Combine("src", "bin", "output.cs"), "generated"),
                Item("kept.cs", "class Kept {}")
            });

            Assert.That(result, Does.Not.Contain("generated"));
            Assert.That(result, Does.Contain("class Kept {}"));
        }

        // ────────────────────────────── unreadable content ──────────────────────────────

        [Test]
        public void Build_UnavailableContent_ProducesTruncatedFence()
        {
            string result = OpenDocumentsPromptBuilder.Build(new[]
            {
                new OpenDocumentsPromptBuilder.DocumentItem("missing.cs", () => null)
            });

            Assert.That(result, Does.Contain("(content truncated, file too large)"));
            Assert.That(result, Does.Not.Contain("null"));
        }

        [Test]
        public void Build_ThrowingReader_ProducesTruncatedFenceInsteadOfThrowing()
        {
            string result = OpenDocumentsPromptBuilder.Build(new[]
            {
                new OpenDocumentsPromptBuilder.DocumentItem("broken.cs", () => throw new InvalidOperationException("boom"))
            });

            Assert.That(result, Does.Contain("(content truncated, file too large)"));
        }

        [Test]
        public void Build_WhitespaceContent_ProducesTruncatedFence()
        {
            string result = OpenDocumentsPromptBuilder.Build(new[]
            {
                Item("empty.cs", "   ")
            });

            Assert.That(result, Does.Contain("(content truncated, file too large)"));
        }

        // ────────────────────────────── limits ──────────────────────────────

        [Test]
        public void Build_MoreThanMaxFiles_SendsRemainderAsTruncatedMarkers()
        {
            var items = Enumerable.Range(1, PromptContentLimits.MaxFilesWithContent + 2)
                .Select(i => Item($"file{i}.cs", "class C" + i + " {}"))
                .ToArray();

            string result = OpenDocumentsPromptBuilder.Build(items);

            // Note: a truncated fence keeps the language tag too, so the number of "````csharp"
            // fences is NOT a usable signal — count the actual file contents instead.
            Assert.That(CountOccurrences(result, "class C"), Is.EqualTo(PromptContentLimits.MaxFilesWithContent));
            Assert.That(CountOccurrences(result, "(content truncated, file too large)"), Is.EqualTo(2));

            // The first ten documents are sent in full...
            Assert.That(result, Does.Contain($"class C{PromptContentLimits.MaxFilesWithContent} {{}}"));

            // ...and the last two are not, but they still leave a visible trace.
            Assert.That(result, Does.Not.Contain($"class C{PromptContentLimits.MaxFilesWithContent + 1} {{}}"));
            Assert.That(result, Does.Contain(
                $"file: file{PromptContentLimits.MaxFilesWithContent + 1}.cs (content truncated, file too large)"));
            Assert.That(result, Does.Contain(
                $"file: file{PromptContentLimits.MaxFilesWithContent + 2}.cs (content truncated, file too large)"));
        }

        [Test]
        public void Build_ExceedingTotalBytes_SendsRemainderAsTruncatedMarkers()
        {
            // Two documents of ~150 KB each: the second one pushes past the 200 KB budget.
            string first = new string('a', 150 * 1024) + "FIRST_END";
            string second = new string('b', 150 * 1024) + "SECOND_END";

            string result = OpenDocumentsPromptBuilder.Build(new[]
            {
                Item("first.cs", first),
                Item("second.cs", second)
            });

            Assert.That(result, Does.Contain("FIRST_END"));
            Assert.That(result, Does.Not.Contain("SECOND_END"));
            Assert.That(result, Does.Contain("file: second.cs (content truncated, file too large)"));
        }

        [Test]
        public void Build_ReadContentIsLazyForExcludedFiles()
        {
            bool readerCalled = false;

            var item = new OpenDocumentsPromptBuilder.DocumentItem(
                Path.Combine("src", "obj", "generated.cs"),
                () =>
                {
                    readerCalled = true;
                    return "generated";
                });

            OpenDocumentsPromptBuilder.Build(new[] { item });

            Assert.That(readerCalled, Is.False, "excluded files must not be read at all");
        }
    }
}
