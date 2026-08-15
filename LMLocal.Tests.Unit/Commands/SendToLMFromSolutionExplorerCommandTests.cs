using System.Collections.Generic;
using System.IO;
using LMLocal.Commands;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Commands
{
    [TestFixture]
    public class SendToLMFromSolutionExplorerCommandTests
    {
        // ────────────────────────────── ShouldExclude ──────────────────────────────

        [Test]
        public void ShouldExclude_ReturnsTrue_ForBinDirectory()
        {
            bool result = SendToLMFromSolutionExplorerCommand.ShouldExclude(
                Path.Combine("src", "bin", "output.dll"));
            Assert.That(result, Is.True);
        }

        [Test]
        public void ShouldExclude_ReturnsTrue_ForObjDirectory()
        {
            bool result = SendToLMFromSolutionExplorerCommand.ShouldExclude(
                Path.Combine("src", "obj", "Debug", "file.obj"));
            Assert.That(result, Is.True);
        }

        [Test]
        public void ShouldExclude_ReturnsTrue_ForVsDirectory()
        {
            bool result = SendToLMFromSolutionExplorerCommand.ShouldExclude(
                Path.Combine(".vs", "config"));
            Assert.That(result, Is.True);
        }

        [Test]
        public void ShouldExclude_ReturnsTrue_ForGitDirectory()
        {
            bool result = SendToLMFromSolutionExplorerCommand.ShouldExclude(
                Path.Combine(".git", "HEAD"));
            Assert.That(result, Is.True);
        }

        [Test]
        public void ShouldExclude_ReturnsTrue_ForNodeModulesDirectory()
        {
            bool result = SendToLMFromSolutionExplorerCommand.ShouldExclude(
                Path.Combine("project", "node_modules", "package.json"));
            Assert.That(result, Is.True);
        }

        [Test]
        public void ShouldExclude_ReturnsTrue_ForPackagesDirectory()
        {
            bool result = SendToLMFromSolutionExplorerCommand.ShouldExclude(
                Path.Combine("project", "packages", "some.nupkg"));
            Assert.That(result, Is.True);
        }

        [Test]
        public void ShouldExclude_ReturnsTrue_ForCopilotBaselineDirectory()
        {
            bool result = SendToLMFromSolutionExplorerCommand.ShouldExclude(
                Path.Combine("src", "CopilotBaseline", "test.txt"));
            Assert.That(result, Is.True);
        }

        [Test]
        public void ShouldExclude_ReturnsFalse_ForBinSimilarFolderName()
        {
            // "binary" is not "bin" — should not be excluded
            bool result = SendToLMFromSolutionExplorerCommand.ShouldExclude(
                Path.Combine("src", "binary", "file.cs"));
            Assert.That(result, Is.False);
        }

        [Test]
        public void ShouldExclude_ReturnsFalse_ForRegularSourceFile()
        {
            bool result = SendToLMFromSolutionExplorerCommand.ShouldExclude(
                Path.Combine("src", "Services", "OrderService.cs"));
            Assert.That(result, Is.False);
        }

        [Test]
        public void ShouldExclude_ReturnsFalse_ForNormalFolder()
        {
            bool result = SendToLMFromSolutionExplorerCommand.ShouldExclude(
                Path.Combine("src", "Models", "User.cs"));
            Assert.That(result, Is.False);
        }

        [Test]
        public void ShouldExclude_ReturnsTrue_ForExeExtension()
        {
            bool result = SendToLMFromSolutionExplorerCommand.ShouldExclude("app.exe");
            Assert.That(result, Is.True);
        }

        [Test]
        public void ShouldExclude_ReturnsTrue_ForDllExtension()
        {
            bool result = SendToLMFromSolutionExplorerCommand.ShouldExclude("lib.dll");
            Assert.That(result, Is.True);
        }

        [Test]
        public void ShouldExclude_ReturnsTrue_ForPdbExtension()
        {
            bool result = SendToLMFromSolutionExplorerCommand.ShouldExclude("symbols.pdb");
            Assert.That(result, Is.True);
        }

        [Test]
        public void ShouldExclude_ReturnsTrue_ForImageExtensions()
        {
            Assert.That(SendToLMFromSolutionExplorerCommand.ShouldExclude("img.png"), Is.True);
            Assert.That(SendToLMFromSolutionExplorerCommand.ShouldExclude("img.jpg"), Is.True);
            Assert.That(SendToLMFromSolutionExplorerCommand.ShouldExclude("img.jpeg"), Is.True);
            Assert.That(SendToLMFromSolutionExplorerCommand.ShouldExclude("img.gif"), Is.True);
            Assert.That(SendToLMFromSolutionExplorerCommand.ShouldExclude("img.bmp"), Is.True);
            Assert.That(SendToLMFromSolutionExplorerCommand.ShouldExclude("img.ico"), Is.True);
            Assert.That(SendToLMFromSolutionExplorerCommand.ShouldExclude("img.svg"), Is.True);
        }

        [Test]
        public void ShouldExclude_ReturnsFalse_ForUnknownExtension()
        {
            bool result = SendToLMFromSolutionExplorerCommand.ShouldExclude("readme.md");
            Assert.That(result, Is.False);
        }

        [Test]
        public void ShouldExclude_ReturnsFalse_ForEmptyPath()
        {
            bool result = SendToLMFromSolutionExplorerCommand.ShouldExclude("");
            Assert.That(result, Is.False);
        }

        // ────────────────────────────── BuildMultiFileMarkdown ──────────────────────────────

        [Test]
        public void BuildMultiFileMarkdown_ReturnsEmptyString_ForEmptyList()
        {
            var result = SendToLMFromSolutionExplorerCommand.BuildMultiFileMarkdown(
                new List<SendToLMFromSolutionExplorerCommand.FileEntry>());
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void BuildMultiFileMarkdown_FormatsSingleFileCorrectly()
        {
            var entries = new List<SendToLMFromSolutionExplorerCommand.FileEntry>
            {
                new SendToLMFromSolutionExplorerCommand.FileEntry
                {
                    Path = "/project/src/Program.cs",
                    Content = "Console.WriteLine(\"Hello\");",
                    IsTruncated = false
                }
            };

            string result = SendToLMFromSolutionExplorerCommand.BuildMultiFileMarkdown(entries);

            Assert.That(result, Does.StartWith("````csharp"));
            Assert.That(result, Does.Contain("// file: /project/src/Program.cs"));
            Assert.That(result, Does.Contain("Console.WriteLine(\"Hello\");"));
            Assert.That(result, Does.EndWith("````"));
        }

        [Test]
        public void BuildMultiFileMarkdown_FormatsMultipleFilesWithSeparator()
        {
            var entries = new List<SendToLMFromSolutionExplorerCommand.FileEntry>
            {
                new SendToLMFromSolutionExplorerCommand.FileEntry
                {
                    Path = "file1.cs",
                    Content = "class A {}",
                    IsTruncated = false
                },
                new SendToLMFromSolutionExplorerCommand.FileEntry
                {
                    Path = "file2.cs",
                    Content = "class B {}",
                    IsTruncated = false
                }
            };

            string result = SendToLMFromSolutionExplorerCommand.BuildMultiFileMarkdown(entries);

            Assert.That(result, Does.Contain("class A {}"));
            Assert.That(result, Does.Contain("class B {}"));
            // Files should be separated by a blank line between code blocks
            Assert.That(result, Does.Contain("````\n\n````"));
        }

        [Test]
        public void BuildMultiFileMarkdown_HandlesTruncatedEntry()
        {
            var entries = new List<SendToLMFromSolutionExplorerCommand.FileEntry>
            {
                new SendToLMFromSolutionExplorerCommand.FileEntry
                {
                    Path = "huge.log",
                    Content = null,
                    IsTruncated = true
                }
            };

            string result = SendToLMFromSolutionExplorerCommand.BuildMultiFileMarkdown(entries);

            Assert.That(result, Does.Contain("(content truncated, file too large)"));
            Assert.That(result, Does.Not.Contain("null"));
        }

        [Test]
        public void BuildMultiFileMarkdown_HandlesTreeEntry()
        {
            var entries = new List<SendToLMFromSolutionExplorerCommand.FileEntry>
            {
                new SendToLMFromSolutionExplorerCommand.FileEntry
                {
                    Path = "/project",
                    IsTree = true,
                    TreeText = "// Project: MyApp\n//   Program.cs"
                }
            };

            string result = SendToLMFromSolutionExplorerCommand.BuildMultiFileMarkdown(entries);

            Assert.That(result, Is.EqualTo("// Project: MyApp\n//   Program.cs"));
        }

        [Test]
        public void BuildMultiFileMarkdown_MixedEntries_AllPresent()
        {
            var entries = new List<SendToLMFromSolutionExplorerCommand.FileEntry>
            {
                new SendToLMFromSolutionExplorerCommand.FileEntry
                {
                    Path = "file.cs",
                    Content = "code",
                    IsTruncated = false
                },
                new SendToLMFromSolutionExplorerCommand.FileEntry
                {
                    Path = "folder",
                    IsTree = true,
                    TreeText = "// Folder: src"
                },
                new SendToLMFromSolutionExplorerCommand.FileEntry
                {
                    Path = "truncated.txt",
                    Content = null,
                    IsTruncated = true
                }
            };

            string result = SendToLMFromSolutionExplorerCommand.BuildMultiFileMarkdown(entries);

            Assert.That(result, Does.Contain("code"));
            Assert.That(result, Does.Contain("// Folder: src"));
            Assert.That(result, Does.Contain("(content truncated, file too large)"));
        }
    }
}
