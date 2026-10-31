using System;
using System.Collections.Generic;
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
    public class ReplaceFileContentTests
    {
        private Mock<IVsDependencies> _vsMock;
        private Mock<IPathResolver> _pathResolverMock;
        private Mock<ISnapshotManager> _snapshotMock;
        private Mock<IFileSystem> _fileSystemMock;
        private Mock<ISyntaxChecker> _syntaxMock;
        private Mock<ISyntaxCheckerFactory> _syntaxFactoryMock;
        private ReplaceFileContent _tool;

        private const string SolutionDir = @"C:\solution";
        private const string FilePath = "test.cs";
        private const string AbsolutePath = @"C:\solution\test.cs";
        private const string NewContent = "public class NewClass {}";

        [SetUp]
        public void SetUp()
        {
            _vsMock = new Mock<IVsDependencies>();
            _pathResolverMock = new Mock<IPathResolver>();
            _snapshotMock = new Mock<ISnapshotManager>();
            _fileSystemMock = new Mock<IFileSystem>();
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

            _fileSystemMock.Setup(f => f.FileExists(AbsolutePath)).Returns(true);
            _fileSystemMock.Setup(f => f.ValidateFilePath(AbsolutePath));
            _fileSystemMock
                .Setup(f => f.ReadAllTextWithDetectedEncodingAsync(AbsolutePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(("public class OldClass {}\r\n", Encoding.UTF8, false));

            _syntaxFactoryMock.Setup(f => f.GetChecker(It.IsAny<string>())).Returns((ISyntaxChecker)null);

            _tool = new ReplaceFileContent(
                _vsMock.Object, _pathResolverMock.Object, _snapshotMock.Object,
                _fileSystemMock.Object, _syntaxFactoryMock.Object);
        }

        private static Dictionary<string, object> CreateParams(
            string filePath = FilePath, string content = NewContent)
        {
            return new Dictionary<string, object>
            {
                ["file_path"] = filePath,
                ["content"] = content
            };
        }

        [Test]
        public async Task ExecuteAsync_NullParameters_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(null);

            var resp = result as ApplyCodeEditResponse;
            Assert.That(resp, Is.Not.Null);
            Assert.That(resp.Success, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_MissingFilePath_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(
                new Dictionary<string, object> { ["content"] = NewContent });

            var resp = result as ApplyCodeEditResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("file_path"));
        }

        [Test]
        public async Task ExecuteAsync_MissingContent_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(
                new Dictionary<string, object> { ["file_path"] = FilePath });

            var resp = result as ApplyCodeEditResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("content"));
        }

        [Test]
        public async Task ExecuteAsync_SolutionNotOpen_ReturnsError()
        {
            _vsMock.Setup(v => v.IsSolutionOpen).Returns(false);

            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as ApplyCodeEditResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Is.EqualTo("No solution is currently open."));
        }

        [Test]
        public async Task ExecuteAsync_FileNotFound_ReturnsError()
        {
            _fileSystemMock.Setup(f => f.FileExists(AbsolutePath)).Returns(false);

            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as ApplyCodeEditResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("not found"));
        }

        [Test]
        public async Task ExecuteAsync_FileOutsideSolution_ReturnsError()
        {
            _pathResolverMock
                .Setup(p => p.IsPathInsideDirectory(AbsolutePath, SolutionDir))
                .Returns(false);

            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as ApplyCodeEditResponse;
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

            var resp = result as ApplyCodeEditResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("Invalid file path"));
        }

        [Test]
        public async Task ExecuteAsync_Success_ReplacesContentWithSnapshot()
        {
            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as ApplyCodeEditResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.FilePath, Is.EqualTo(FilePath));

            _snapshotMock.Verify(s =>
                s.SnapshotFileAsync(AbsolutePath, SnapshotChangeStatus.BeforeModify,
                    It.IsAny<CancellationToken>()));
            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath, NewContent, Encoding.UTF8, false,
                    It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_NormalizesLineEndings_WhenContentHasLfButFileHasCrlf()
        {
            // File uses CRLF
            _fileSystemMock
                .Setup(f => f.ReadAllTextWithDetectedEncodingAsync(AbsolutePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(("public class OldClass {}\r\n", Encoding.UTF8, false));

            const string contentWithLf = "public class NewClass {}\npublic class AlsoNew {}";
            const string expectedContent = "public class NewClass {}\r\npublic class AlsoNew {}";

            var result = await _tool.ExecuteAsync(CreateParams(content: contentWithLf));

            var resp = result as ApplyCodeEditResponse;
            Assert.That(resp.Success, Is.True);

            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath, expectedContent, Encoding.UTF8, false,
                    It.IsAny<CancellationToken>()));
        }

        // ── No-op at the write point (P2-6, checks 11, 30, 38а/38б/38в) ──

        [Test]
        public async Task ReplaceFileContent_IdenticalContent_DoesNotWrite()
        {
            // Check 11: the snapshot was taken before the read and the file was rewritten even when the text was
            // identical. Now nothing happens: no snapshot, no write, no syntax check.
            const string content = "public class OldClass {}\r\n";
            _fileSystemMock
                .Setup(f => f.ReadAllTextWithDetectedEncodingAsync(AbsolutePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync((content, Encoding.UTF8, false));

            var result = await _tool.ExecuteAsync(CreateParams(content: content));

            var resp = result as ApplyCodeEditResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.IsNoOp, Is.True);

            _snapshotMock.Verify(s => s.SnapshotFileAsync(
                AbsolutePath, SnapshotChangeStatus.BeforeModify, It.IsAny<CancellationToken>()), Times.Never);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, It.IsAny<string>(), It.IsAny<Encoding>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ReplaceFileContent_NoOp_CompletionMessageSaysNoChangesNeeded()
        {
            // Check 30: the only no-op signal of this tool is the message — the response schema is not extended.
            const string content = "public class OldClass {}\r\n";
            _fileSystemMock
                .Setup(f => f.ReadAllTextWithDetectedEncodingAsync(AbsolutePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync((content, Encoding.UTF8, false));

            var result = await _tool.ExecuteAsync(CreateParams(content: content));

            Assert.That(_tool.GetCompletionMessage(result), Is.EqualTo("No changes needed."));
        }

        [Test]
        public async Task ReplaceFileContent_CrlfFile_LfFromModel_IsNoOp()
        {
            // Check 38а: the model almost always sends "\n", so comparing with the raw content would never
            // recognise this call and the file would be rewritten on every no-op.
            _fileSystemMock
                .Setup(f => f.ReadAllTextWithDetectedEncodingAsync(AbsolutePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(("a\r\nb", Encoding.UTF8, false));

            var result = await _tool.ExecuteAsync(CreateParams(content: "a\nb"));

            var resp = result as ApplyCodeEditResponse;
            Assert.That(resp.IsNoOp, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, It.IsAny<string>(), It.IsAny<Encoding>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ReplaceFileContent_MixedEol_UnchangedText_KeepsBytes()
        {
            // Check 38б: the EOL normalization does change a mixed file, so only a line comparison can tell that
            // the text did not change. Writing here would "unify" the untouched lines (E5) on a no-op call.
            _fileSystemMock
                .Setup(f => f.ReadAllTextWithDetectedEncodingAsync(AbsolutePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(("a\r\nb\nc\r\n", Encoding.UTF8, false));

            var result = await _tool.ExecuteAsync(CreateParams(content: "a\nb\nc\n"));

            var resp = result as ApplyCodeEditResponse;
            Assert.That(resp.IsNoOp, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, It.IsAny<string>(), It.IsAny<Encoding>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ReplaceFileContent_TrailingNewlineAdded_IsWritten()
        {
            // Check 38в: same lines, different trailing-EOL parity — adding the final break is a real edit and
            // must not be swallowed by the line comparison.
            _fileSystemMock
                .Setup(f => f.ReadAllTextWithDetectedEncodingAsync(AbsolutePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(("a", Encoding.UTF8, false));

            var result = await _tool.ExecuteAsync(CreateParams(content: "a\n"));

            var resp = result as ApplyCodeEditResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.IsNoOp, Is.False);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\n", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

    }
}
