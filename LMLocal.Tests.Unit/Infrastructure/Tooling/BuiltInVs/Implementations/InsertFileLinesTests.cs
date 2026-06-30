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
    public class InsertFileLinesTests
    {
        private Mock<IVsDependencies> _vsMock;
        private Mock<IPathResolver> _pathResolverMock;
        private Mock<ISnapshotManager> _snapshotMock;
        private Mock<IFileSystem> _fileSystemMock;
        private Mock<ISyntaxChecker> _syntaxMock;
        private InsertFileLines _tool;

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

            _tool = new InsertFileLines(
                _vsMock.Object, _pathResolverMock.Object, _snapshotMock.Object,
                _fileSystemMock.Object, _syntaxMock.Object);
        }

        private void SetupFileContent(string content)
        {
            _fileSystemMock
                .Setup(f => f.ReadAllTextWithDetectedEncodingAsync(AbsolutePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync((content, Encoding.UTF8, false));
        }

        private static Dictionary<string, object> CreateParams(
            string filePath = FilePath,
            int position = 1,
            string newLines = "inserted")
        {
            return new Dictionary<string, object>
            {
                ["file_path"] = filePath,
                ["position"] = position,
                ["new_lines"] = newLines
            };
        }

        // ── Parameter validation ──────────────────────────────────────────────

        [Test]
        public async Task ExecuteAsync_NullParameters_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(null);

            var resp = result as InsertLinesResponse;
            Assert.That(resp, Is.Not.Null);
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Is.EqualTo("Parameters cannot be null."));
        }

        [Test]
        public async Task ExecuteAsync_MissingFilePath_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(
                new Dictionary<string, object> { ["position"] = 1, ["new_lines"] = "x" });

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("file_path"));
        }

        [Test]
        public async Task ExecuteAsync_MissingPosition_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(
                new Dictionary<string, object> { ["file_path"] = FilePath, ["new_lines"] = "x" });

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_MissingNewLines_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(
                new Dictionary<string, object> { ["file_path"] = FilePath, ["position"] = 1 });

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_NegativePosition_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(CreateParams(position: -1));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_EmptyNewLines_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(CreateParams(newLines: ""));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("new_lines must not be empty"));
        }

        // ── Prerequisite checks ───────────────────────────────────────────────

        [Test]
        public async Task ExecuteAsync_SolutionNotOpen_ReturnsError()
        {
            _vsMock.Setup(v => v.IsSolutionOpen).Returns(false);

            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Is.EqualTo("No solution is currently open."));
        }

        [Test]
        public async Task ExecuteAsync_FileNotFound_ReturnsError()
        {
            _fileSystemMock.Setup(f => f.FileExists(AbsolutePath)).Returns(false);

            var result = await _tool.ExecuteAsync(CreateParams());

            var resp = result as InsertLinesResponse;
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

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("outside the solution directory"));
        }

        // ── Insert scenarios ──────────────────────────────────────────────────

        [Test]
        public async Task ExecuteAsync_Success_InsertsAtPositionZero()
        {
            SetupFileContent("line1\r\nline2");

            var result = await _tool.ExecuteAsync(CreateParams(position: 0, newLines: "header"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.LinesInserted, Is.EqualTo(1));

            _snapshotMock.Verify(s =>
                s.SnapshotFileAsync(AbsolutePath, SnapshotChangeStatus.BeforeModify,
                    It.IsAny<CancellationToken>()));
            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath,
                    "header\r\nline1\r\nline2",
                    Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_Success_InsertsInMiddle()
        {
            SetupFileContent("a\r\nb\r\nc");

            var result = await _tool.ExecuteAsync(CreateParams(position: 2, newLines: "x"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.LinesInserted, Is.EqualTo(1));

            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath,
                    "a\r\nb\r\nx\r\nc",
                    Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_Success_InsertsMultipleLines()
        {
            SetupFileContent("a\r\nb");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 1, newLines: "x\r\ny"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.LinesInserted, Is.EqualTo(2));

            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath,
                    "a\r\nx\r\ny\r\nb",
                    Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_Success_PadsWithEmptyLinesWhenPositionExceedsLength()
        {
            SetupFileContent("a");

            var result = await _tool.ExecuteAsync(CreateParams(position: 5, newLines: "new"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.LinesInserted, Is.EqualTo(1));

            _snapshotMock.Verify(s =>
                s.SnapshotFileAsync(AbsolutePath, SnapshotChangeStatus.BeforeModify,
                    It.IsAny<CancellationToken>()));
            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath, It.IsAny<string>(),
                    Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_Success_AppendsAtEndMaintainsTrailingNewline()
        {
            // File ends with \r\n → hadTrailingNewline=true → trailing preserved
            SetupFileContent("a\r\nb\r\n");

            var result = await _tool.ExecuteAsync(CreateParams(position: 2, newLines: "c"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);

            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath,
                    "a\r\nb\r\nc\r\n",
                    Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_Success_WorksWithUnixLineEndings()
        {
            SetupFileContent("a\nb\nc");

            var result = await _tool.ExecuteAsync(CreateParams(position: 2, newLines: "x"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);

            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath,
                    "a\nb\nx\nc",
                    Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_Cancellation_ReturnsError()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = await _tool.ExecuteAsync(CreateParams(), cts.Token);

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.False);
        }
    }
}
