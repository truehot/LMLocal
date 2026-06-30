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
    public class ReplaceFileLinesTests
    {
        private Mock<IVsDependencies> _vsMock;
        private Mock<IPathResolver> _pathResolverMock;
        private Mock<ISnapshotManager> _snapshotMock;
        private Mock<IFileSystem> _fileSystemMock;
        private Mock<ISyntaxChecker> _syntaxMock;
        private ReplaceFileLines _tool;

        private const string SolutionDir = @"C:\solution";
        private const string FilePath = "test.cs";
        private const string AbsolutePath = @"C:\solution\test.cs";

        [SetUp]
        public void SetUp()
        {
            _vsMock = new Mock<IVsDependencies>();
            _pathResolverMock = new Mock<IPathResolver>();
            _snapshotMock = new Mock<ISnapshotManager>();
            _fileSystemMock = new Mock<IFileSystem>();
            _syntaxMock = new Mock<ISyntaxChecker>();

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

            _syntaxMock.Setup(s => s.IsSupported(AbsolutePath)).Returns(false);

            _tool = new ReplaceFileLines(
                _vsMock.Object, _pathResolverMock.Object, _snapshotMock.Object,
                _fileSystemMock.Object, _syntaxMock.Object);
        }

        private static Dictionary<string, object> CreateParams(
            string filePath = FilePath,
            int startLine = 1,
            string oldLines = "line1",
            string newLines = "replacement")
        {
            return new Dictionary<string, object>
            {
                ["file_path"] = filePath,
                ["start_line"] = startLine,
                ["old_lines"] = oldLines,
                ["new_lines"] = newLines
            };
        }

        private void SetupFileContent(string content)
        {
            _fileSystemMock
                .Setup(f => f.ReadAllTextWithDetectedEncodingAsync(AbsolutePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync((content, Encoding.UTF8, false));
        }

        // ── Parameter validation ──────────────────────────────────────────────

        [Test]
        public async Task ExecuteAsync_NullParameters_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(null);

            var resp = result as ReplaceLinesResponse;
            Assert.That(resp, Is.Not.Null);
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Is.EqualTo("Parameters cannot be null."));
        }

        [Test]
        public async Task ExecuteAsync_MissingFilePath_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["start_line"] = 1, ["old_lines"] = "o", ["new_lines"] = "n"
            });
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("file_path"));
        }

        [Test]
        public async Task ExecuteAsync_MissingStartLine_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["file_path"] = FilePath, ["old_lines"] = "o", ["new_lines"] = "n"
            });
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_MissingOldLines_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["file_path"] = FilePath, ["start_line"] = 1, ["new_lines"] = "n"
            });
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_MissingNewLines_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["file_path"] = FilePath, ["start_line"] = 1, ["old_lines"] = "o"
            });
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
        }

        // ── Prerequisite checks ───────────────────────────────────────────────

        [Test]
        public async Task ExecuteAsync_SolutionNotOpen_ReturnsError()
        {
            _vsMock.Setup(v => v.IsSolutionOpen).Returns(false);

            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Is.EqualTo("No solution is currently open."));
        }

        [Test]
        public async Task ExecuteAsync_FileNotFound_ReturnsError()
        {
            _fileSystemMock.Setup(f => f.FileExists(AbsolutePath)).Returns(false);

            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as ReplaceLinesResponse;
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

            var resp = result as ReplaceLinesResponse;
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

            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("Invalid file path"));
        }

        // ── Old content mismatch ──────────────────────────────────────────────

        [Test]
        public async Task ExecuteAsync_OldContentMismatch_ReturnsError()
        {
            SetupFileContent("actualContent");

            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1, oldLines: "wrongOld", newLines: "replacement"));

            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("Old content mismatch"));
        }

        // ── Success cases ─────────────────────────────────────────────────────

        [Test]
        public async Task ExecuteAsync_Success_ReplacesLines()
        {
            SetupFileContent("line1\r\nline2\r\nline3");

            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2,
                oldLines: "line2",
                newLines: "modified2"));

            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);

            _snapshotMock.Verify(s =>
                s.SnapshotFileAsync(AbsolutePath, SnapshotChangeStatus.BeforeModify,
                    It.IsAny<CancellationToken>()));
            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath,
                    "line1\r\nmodified2\r\nline3",
                    Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_Success_DeletesLinesWhenNewLinesEmpty()
        {
            SetupFileContent("line1\r\nline2\r\nline3");

            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2,
                oldLines: "line2",
                newLines: ""));

            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);

            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath,
                    "line1\r\nline3",
                    Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_Success_ReplacesMultipleLines()
        {
            SetupFileContent("a\r\nb\r\nc\r\nd");

            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2,
                oldLines: "b\r\nc",
                newLines: "x\r\ny"));

            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);

            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath,
                    "a\r\nx\r\ny\r\nd",
                    Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_Success_WorksWithUnixLineEndings()
        {
            SetupFileContent("a\nb\nc");

            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2,
                oldLines: "b",
                newLines: "x"));

            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);

            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath,
                    "a\nx\nc",
                    Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_Cancellation_ReturnsError()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = await _tool.ExecuteAsync(CreateParams(), cts.Token);

            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
        }
    }
}
