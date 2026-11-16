using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Syntax;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    [TestFixture]
    public class CreateFileTests
    {
        private Mock<IVsDependencies> _vsMock;
        private Mock<IPathResolver> _pathResolverMock;
        private Mock<IFileSystem> _fileSystemMock;
        private Mock<ISnapshotManager> _snapshotMock;
        private Mock<ISyntaxChecker> _syntaxMock;
        private Mock<ISyntaxCheckerFactory> _syntaxFactoryMock;
        private CreateFile _tool;

        private const string SolutionDir = @"C:\solution";
        private const string FilePath = "test.cs";
        private const string AbsolutePath = @"C:\solution\test.cs";
        private const string FileContent = "public class Test {}";
        private const string SourcePath = "source.cs";
        private const string AbsoluteSourcePath = @"C:\solution\source.cs";

        [SetUp]
        public void SetUp()
        {
            _vsMock = new Mock<IVsDependencies>();
            _pathResolverMock = new Mock<IPathResolver>();
            _fileSystemMock = new Mock<IFileSystem>();
            _snapshotMock = new Mock<ISnapshotManager>();
            _syntaxMock = new Mock<ISyntaxChecker>();
            _syntaxFactoryMock = new Mock<ISyntaxCheckerFactory>();

            _vsMock.Setup(v => v.IsSolutionOpen).Returns(true);
            _vsMock.Setup(v => v.GetSolutionDirectory()).Returns(SolutionDir);

            string resolvedPath = AbsolutePath;
            _pathResolverMock
                .Setup(p => p.TryResolveFilePath(It.IsAny<string>(), It.IsAny<string>(), out resolvedPath))
                .Returns(true);
            _pathResolverMock
                .Setup(p => p.IsPathInsideDirectory(AbsolutePath, SolutionDir))
                .Returns(true);
            string relativePath = FilePath;
            _pathResolverMock
                .Setup(p => p.TryGetRelativePath(AbsolutePath, SolutionDir, out relativePath))
                .Returns(true);

            _fileSystemMock.Setup(f => f.FileExists(AbsolutePath)).Returns(false);
            _fileSystemMock.Setup(f => f.ValidateFilePath(AbsolutePath));
            _syntaxFactoryMock.Setup(f => f.GetChecker(It.IsAny<string>())).Returns((ISyntaxChecker)null);

            _tool = new CreateFile(
                _vsMock.Object, _pathResolverMock.Object, _fileSystemMock.Object,
                _snapshotMock.Object, _syntaxFactoryMock.Object);
        }

        private static Dictionary<string, object> CreateParams(
            string filePath = FilePath, string content = FileContent)
        {
            return new Dictionary<string, object>
            {
                ["file_path"] = filePath,
                ["content"] = content
            };
        }

        private void SetupCopySource(bool exists = true, bool insideSolution = true)
        {
            string resolvedSource = AbsoluteSourcePath;
            _pathResolverMock
                .Setup(p => p.TryResolveFilePath(SourcePath, SolutionDir, out resolvedSource))
                .Returns(true);
            _pathResolverMock
                .Setup(p => p.IsPathInsideDirectory(AbsoluteSourcePath, SolutionDir))
                .Returns(insideSolution);
            string relativeSource = SourcePath;
            _pathResolverMock
                .Setup(p => p.TryGetRelativePath(AbsoluteSourcePath, SolutionDir, out relativeSource))
                .Returns(true);
            _fileSystemMock.Setup(f => f.FileExists(AbsoluteSourcePath)).Returns(exists);
        }

        private static Dictionary<string, object> CreateCopyParams(
            string filePath = FilePath, string copyFrom = SourcePath)
        {
            return new Dictionary<string, object>
            {
                ["file_path"] = filePath,
                ["copy_from"] = copyFrom
            };
        }

        private static byte[] BuildUtf8BomCrlfBytes(string text)
        {
            byte[] preamble = Encoding.UTF8.GetPreamble();
            byte[] body = Encoding.UTF8.GetBytes(text);
            byte[] bytes = new byte[preamble.Length + body.Length];
            Array.Copy(preamble, 0, bytes, 0, preamble.Length);
            Array.Copy(body, 0, bytes, preamble.Length, body.Length);
            return bytes;
        }

        private static CreateFile CreateToolWithRealFileSystem(string solutionDir)
        {
            var vsMock = new Mock<IVsDependencies>();
            vsMock.Setup(v => v.IsSolutionOpen).Returns(true);
            vsMock.Setup(v => v.GetSolutionDirectory()).Returns(solutionDir);

            var snapshotMock = new Mock<ISnapshotManager>();
            var syntaxFactoryMock = new Mock<ISyntaxCheckerFactory>();
            syntaxFactoryMock.Setup(f => f.GetChecker(It.IsAny<string>())).Returns((ISyntaxChecker)null);

            return new CreateFile(vsMock.Object, new PathResolver(), new DefaultFileSystem(),
                snapshotMock.Object, syntaxFactoryMock.Object);
        }

        [Test]
        public async Task ExecuteAsync_NullParameters_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(null);

            var resp = result as CreateFileResponse;
            Assert.That(resp, Is.Not.Null);
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Is.EqualTo("Parameters cannot be null."));
        }

        [Test]
        public async Task ExecuteAsync_MissingFilePath_ReturnsError()
        {
            var parameters = new Dictionary<string, object> { ["content"] = FileContent };

            var result = await _tool.ExecuteAsync(parameters);

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("file_path"));
        }

        [Test]
        public async Task ExecuteAsync_NeitherContentNorCopyFrom_ReturnsError()
        {
            var parameters = new Dictionary<string, object> { ["file_path"] = FilePath };

            var result = await _tool.ExecuteAsync(parameters);

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("content"));
            Assert.That(resp.ErrorMessage, Does.Contain("copy_from"));
        }

        [Test]
        public async Task ExecuteAsync_SolutionNotOpen_ReturnsError()
        {
            _vsMock.Setup(v => v.IsSolutionOpen).Returns(false);

            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Is.EqualTo("No solution is currently open."));
        }

        [Test]
        public async Task ExecuteAsync_FileAlreadyExists_ReturnsError()
        {
            _fileSystemMock.Setup(f => f.FileExists(AbsolutePath)).Returns(true);

            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("already exists"));
        }

        [Test]
        public async Task ExecuteAsync_FileOutsideSolution_ReturnsError()
        {
            _pathResolverMock
                .Setup(p => p.IsPathInsideDirectory(AbsolutePath, SolutionDir))
                .Returns(false);

            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("outside the solution directory"));
        }

        [Test]
        public async Task ExecuteAsync_InvalidFilePath_ReturnsError()
        {
            _fileSystemMock
                .Setup(f => f.ValidateFilePath(AbsolutePath))
                .Throws(new ArgumentException("Invalid path characters"));

            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("Invalid file path"));
        }

        [Test]
        public async Task ExecuteAsync_Success_CreatesFileWithSnapshot()
        {
            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.CreatedSuccessfully, Is.True);
            Assert.That(resp.FilePath, Is.EqualTo(FilePath));

            _snapshotMock.Verify(s =>
                s.SnapshotFileAsync(AbsolutePath, SnapshotChangeStatus.BeforeCreate,
                    It.IsAny<CancellationToken>()));
            _fileSystemMock.Verify(f => f.EnsureDirectoryExistsForFile(AbsolutePath));
            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath, FileContent, Encoding.UTF8, true,
                    It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_Success_CreatesRelativePathWhenGetRelativeFails()
        {
            string nullRelative = null;
            _pathResolverMock
                .Setup(p => p.TryGetRelativePath(AbsolutePath, SolutionDir, out nullRelative))
                .Returns(false);

            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.FilePath, Is.EqualTo(AbsolutePath));
        }

        [Test]
        public async Task ExecuteAsync_Cancellation_ReturnsError()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = await _tool.ExecuteAsync(CreateParams(), cts.Token);

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_NormalizesLineEndings_ContentWithLfBecomesCrlf()
        {
            const string contentWithLf = "public class NewClass {}\npublic class AlsoNew {}";
            const string expectedContent = "public class NewClass {}\r\npublic class AlsoNew {}";

            var result = await _tool.ExecuteAsync(CreateParams(content: contentWithLf));

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.True);

            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath, expectedContent, Encoding.UTF8, true,
                    It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_CopyFrom_CopiesSourceToTarget()
        {
            SetupCopySource();

            var result = await _tool.ExecuteAsync(CreateCopyParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.True, resp.ErrorMessage);
            Assert.That(resp.CreatedSuccessfully, Is.True);
            Assert.That(resp.FilePath, Is.EqualTo(FilePath));
            Assert.That(resp.CopiedFrom, Is.EqualTo(SourcePath));
            Assert.That(resp.SyntaxErrors, Is.Null);

            _fileSystemMock.Verify(f => f.CopyFileAsync(AbsoluteSourcePath, AbsolutePath, It.IsAny<CancellationToken>()), Times.Once);
            _fileSystemMock.Verify(f => f.EnsureDirectoryExistsForFile(AbsolutePath), Times.Once);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Encoding>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            _syntaxFactoryMock.Verify(f => f.GetChecker(It.IsAny<string>()), Times.Never);
            _snapshotMock.Verify(s => s.SnapshotFileAsync(AbsolutePath, SnapshotChangeStatus.BeforeCreate, It.IsAny<CancellationToken>()), Times.Once);
            _snapshotMock.Verify(s => s.SnapshotFileAsync(AbsoluteSourcePath, It.IsAny<SnapshotChangeStatus>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_ContentAndCopyFrom_ReturnsError()
        {
            SetupCopySource();

            var parameters = CreateCopyParams();
            parameters["content"] = FileContent;

            var result = await _tool.ExecuteAsync(parameters);

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("not both"));

            _fileSystemMock.Verify(f => f.CopyFileAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestCase("")]
        [TestCase("   ")]
        public async Task ExecuteAsync_ContentWithEmptyCopyFrom_CreatesFromContent(string emptyCopyFrom)
        {
            var parameters = CreateParams();
            parameters["copy_from"] = emptyCopyFrom;

            var result = await _tool.ExecuteAsync(parameters);

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.True, resp.ErrorMessage);
            Assert.That(resp.CopiedFrom, Is.Null);
            Assert.That(_tool.GetProcessingMessage(parameters), Is.EqualTo($"Creating file '{FilePath}'... "));

            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath, FileContent, Encoding.UTF8, true,
                    It.IsAny<CancellationToken>()), Times.Once);
            _fileSystemMock.Verify(f => f.CopyFileAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_CopyFromWithEmptyContent_Copies()
        {
            SetupCopySource();

            var parameters = CreateCopyParams();
            parameters["content"] = "";

            var result = await _tool.ExecuteAsync(parameters);

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.True, resp.ErrorMessage);
            Assert.That(resp.CopiedFrom, Is.EqualTo(SourcePath));

            _fileSystemMock.Verify(f => f.CopyFileAsync(AbsoluteSourcePath, AbsolutePath, It.IsAny<CancellationToken>()), Times.Once);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Encoding>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_EmptyContentAndEmptyCopyFrom_CreatesEmptyFile()
        {
            var parameters = CreateParams(content: "");
            parameters["copy_from"] = "";

            var result = await _tool.ExecuteAsync(parameters);

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.True, resp.ErrorMessage);

            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath, "", Encoding.UTF8, true,
                    It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_CopyFromMissingSource_ReturnsError()
        {
            SetupCopySource(exists: false);

            var result = await _tool.ExecuteAsync(CreateCopyParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("Source file not found"));

            _fileSystemMock.Verify(f => f.CopyFileAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _snapshotMock.Verify(s => s.SnapshotFileAsync(
                It.IsAny<string>(), It.IsAny<SnapshotChangeStatus>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_CopyFromSourceOutsideSolution_ReturnsError()
        {
            SetupCopySource(insideSolution: false);

            var result = await _tool.ExecuteAsync(CreateCopyParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("outside the solution directory"));
        }

        [Test]
        public async Task ExecuteAsync_CopyFromSourceIsDirectory_ReturnsError()
        {
            SetupCopySource();
            _fileSystemMock.Setup(f => f.DirectoryExists(AbsoluteSourcePath)).Returns(true);

            var result = await _tool.ExecuteAsync(CreateCopyParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("directory"));
        }

        [Test]
        public async Task ExecuteAsync_CopyFromTargetExists_ReturnsError()
        {
            SetupCopySource();
            _fileSystemMock.Setup(f => f.FileExists(AbsolutePath)).Returns(true);

            var result = await _tool.ExecuteAsync(CreateCopyParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("already exists"));
        }

        [Test]
        public async Task ExecuteAsync_CopyFromIntoSelf_ReturnsError()
        {
            // The generic resolver setup maps every path to AbsolutePath, so source == target.
            var result = await _tool.ExecuteAsync(CreateCopyParams(copyFrom: FilePath));

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("itself"),
                "self-copy must be reported as such, not as 'File already exists'");

            _fileSystemMock.Verify(f => f.CopyFileAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ExecuteAsync_CopyFromCopyFails_DeletesPartialTargetAndReturnsError()
        {
            SetupCopySource();

            int targetExistsCalls = 0;
            _fileSystemMock.Setup(f => f.FileExists(AbsolutePath)).Returns(() => ++targetExistsCalls > 1);

            _fileSystemMock
                .Setup(f => f.CopyFileAsync(AbsoluteSourcePath, AbsolutePath, It.IsAny<CancellationToken>()))
                .Returns(Task.FromException(new System.IO.IOException("disk full")));

            var result = await _tool.ExecuteAsync(CreateCopyParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("Failed to copy"));

            _fileSystemMock.Verify(f => f.Delete(AbsolutePath), Times.Once,
                "a truncated copy must not be left on disk");
        }

        [Test]
        public async Task ExecuteAsync_CopyFromCancelledDuringCopy_ReturnsError()
        {
            SetupCopySource();

            _fileSystemMock
                .Setup(f => f.CopyFileAsync(AbsoluteSourcePath, AbsolutePath, It.IsAny<CancellationToken>()))
                .Returns(Task.FromException(new OperationCanceledException()));

            var result = await _tool.ExecuteAsync(CreateCopyParams());

            var resp = result as CreateFileResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("cancelled"));
        }

        [Test]
        public async Task Messages_CopyFrom_UseCopyWording()
        {
            SetupCopySource();

            Assert.That(_tool.GetProcessingMessage(CreateCopyParams()),
                Is.EqualTo($"Copying '{SourcePath}' -> '{FilePath}'... "));

            var result = await _tool.ExecuteAsync(CreateCopyParams());

            Assert.That(_tool.GetCompletionMessage(result), Is.EqualTo("File copied."));
        }

        [Test]
        public async Task Messages_Create_KeepCreateWording()
        {
            Assert.That(_tool.GetProcessingMessage(CreateParams()), Is.EqualTo($"Creating file '{FilePath}'... "));

            var result = await _tool.ExecuteAsync(CreateParams());

            Assert.That(_tool.GetCompletionMessage(result), Is.EqualTo("File created."));
        }

        [Test]
        public async Task ExecuteAsync_CopyFrom_RealFileSystem_PreservesEncodingBomAndLineEndings()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "LMLocalCopyTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                var tool = CreateToolWithRealFileSystem(tempDir);

                // UTF-8 with BOM + CRLF: content mode would rewrite this as UTF-8+BOM with normalized EOL,
                // a copy must not touch a single byte.
                byte[] utf8Source = BuildUtf8BomCrlfBytes("namespace N\r\n{\r\n    class A { }\r\n}\r\n");
                string utf8SourcePath = Path.Combine(tempDir, "source.cs");
                File.WriteAllBytes(utf8SourcePath, utf8Source);

                var first = await tool.ExecuteAsync(new Dictionary<string, object>
                {
                    ["file_path"] = "copy.cs",
                    ["copy_from"] = "source.cs"
                });

                var firstResp = first as CreateFileResponse;
                Assert.That(firstResp.Success, Is.True, firstResp.ErrorMessage);
                Assert.That(firstResp.CopiedFrom, Is.EqualTo("source.cs"));
                Assert.That(File.ReadAllBytes(Path.Combine(tempDir, "copy.cs")), Is.EqualTo(utf8Source),
                    "the copy must be byte-for-byte identical to the source");

                // UTF-16 with BOM: a text round-trip would re-encode it.
                string utf16SourcePath = Path.Combine(tempDir, "utf16.cs");
                File.WriteAllText(utf16SourcePath, "namespace N\r\n{\r\n    class B { }\r\n}\r\n",
                    new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

                var second = await tool.ExecuteAsync(new Dictionary<string, object>
                {
                    ["file_path"] = "copy16.cs",
                    ["copy_from"] = "utf16.cs"
                });

                Assert.That((second as CreateFileResponse).Success, Is.True);
                Assert.That(File.ReadAllBytes(Path.Combine(tempDir, "copy16.cs")),
                    Is.EqualTo(File.ReadAllBytes(utf16SourcePath)));
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
