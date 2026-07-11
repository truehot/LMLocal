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
        private Mock<ISyntaxCheckerFactory> _syntaxFactoryMock;
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

            _syntaxFactoryMock.Setup(f => f.GetChecker(It.IsAny<string>())).Returns((ISyntaxChecker)null);

            _tool = new InsertFileLines(
                _vsMock.Object, _pathResolverMock.Object, _snapshotMock.Object,
                _fileSystemMock.Object, _syntaxFactoryMock.Object);
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
            string newLines = "inserted",
            string expectedLine = null)
        {
            var dict = new Dictionary<string, object>
            {
                ["file_path"] = filePath,
                ["position"] = position,
                ["new_lines"] = newLines
            };
            if (expectedLine != null)
                dict["expected_line"] = expectedLine;
            return dict;
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

        // ── Insert scenarios (without expected_line) ──────────────────────────

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

        // ── expected_line: matches ────────────────────────────────────────────

        [Test]
        public async Task ExpectedLine_Matches_InsertHappensNormally()
        {
            SetupFileContent("a\nb\nc");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 1, newLines: "x", expectedLine: "a"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.AutoCorrected, Is.Not.True);
            Assert.That(resp.LinesInserted, Is.EqualTo(1));
            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath,
                    "a\nx\nb\nc",
                    Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExpectedLine_Matches_MultiLineFile()
        {
            SetupFileContent("line1\nline2\nline3\nline4\nline5");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 3, newLines: "inserted", expectedLine: "line3"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.AutoCorrected, Is.Not.True);
        }

        // ── expected_line: auto-correct ───────────────────────────────────────

        [Test]
        public async Task ExpectedLine_Shifted_AutoCorrectsPosition()
        {
            // "targetLine" is at line 4, agent thinks position=2
            SetupFileContent("a\nb\nc\ntargetLine\nd\ne");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 2, newLines: "inserted", expectedLine: "targetLine"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.AutoCorrected, Is.True);
            Assert.That(resp.OriginalPosition, Is.EqualTo(2));
            Assert.That(resp.AppliedPosition, Is.EqualTo(4));
            _fileSystemMock.Verify(f =>
                f.WriteAllBytesWithEncodingAsync(AbsolutePath,
                    "a\nb\nc\ntargetLine\ninserted\nd\ne",
                    Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExpectedLine_ShiftedDown_AutoCorrects()
        {
            // "marker" was at 2, now at 3 (due to previous insert adding line at 0)
            SetupFileContent("header\na\nmarker\nb\nc");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 2, newLines: "inserted", expectedLine: "marker"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.AutoCorrected, Is.True);
            Assert.That(resp.AppliedPosition, Is.EqualTo(3));
        }

        // ── expected_line: multiple matches ───────────────────────────────────

        [Test]
        public async Task ExpectedLine_MultipleMatches_ReturnsCandidates()
        {
            SetupFileContent("a\ndup\nc\nd\ndup\nf");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 1, newLines: "x", expectedLine: "dup"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("matches 2 locations"));
            Assert.That(resp.Candidates, Is.Not.Null);
            Assert.That(resp.Candidates.Count, Is.EqualTo(2));
            Assert.That(resp.Candidates[0].StartLine, Is.EqualTo(2));
            Assert.That(resp.Candidates[0].Text, Is.EqualTo("a\ndup\nc\nd"));
            Assert.That(resp.Candidates[1].StartLine, Is.EqualTo(5));
            Assert.That(resp.Candidates[1].Text, Is.EqualTo("c\nd\ndup\nf"));
        }

        // ── expected_line: not found ──────────────────────────────────────────

        [Test]
        public async Task ExpectedLine_NotFound_ReturnsError()
        {
            SetupFileContent("a\nb\nc");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 2, newLines: "x", expectedLine: "zzz_not_in_file"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("expected_line not found"));
        }

        // ── expected_line: edge cases ─────────────────────────────────────────

        [Test]
        public async Task ExpectedLine_PositionZero_Ignored()
        {
            SetupFileContent("a\nb\nc");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 0, newLines: "header", expectedLine: "a"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.LinesInserted, Is.EqualTo(1));
            // expected_line is ignored for position=0, normal insert before first line
        }

        [Test]
        public async Task ExpectedLine_PositionBeyondFile_PadsWithoutChecking()
        {
            // position > file length → padding, expected_line not checked
            SetupFileContent("a");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 5, newLines: "new", expectedLine: "anything"));

            var resp = result as InsertLinesResponse;
            // position(5) > linesList.Count(1) → padding happens before expected_line check
            // position is still 5 which exceeds padded linesList.Count → isAppendingToEnd
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.AutoCorrected, Is.Not.True);
        }

        [Test]
        public async Task ExpectedLine_Missing_DifferentThanMismatchAtPosition()
        {
            // expected_line provided but not matching: "c" is at position 3, not "b"
            SetupFileContent("a\nb\nc");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 3, newLines: "x", expectedLine: "b"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.AutoCorrected, Is.True);
            Assert.That(resp.AppliedPosition, Is.EqualTo(2));
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

        // ── Trailing whitespace tolerance (step 1) ────────────────────────────

        [Test]
        public async Task ExpectedLine_HasTrailingSpaces_MatchesAndInserts()
        {
            // expected_line has trailing spaces, file line does not — should still match
            SetupFileContent("a\nb\nc");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 2, newLines: "x", expectedLine: "b   "));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.AutoCorrected, Is.Not.True);
        }

        [Test]
        public async Task ExpectedLine_FileLineHasTrailingSpaces_MatchesAndInserts()
        {
            // File line has trailing spaces, expected_line does not — should still match
            SetupFileContent("a\nb   \nc");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 2, newLines: "x", expectedLine: "b"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.AutoCorrected, Is.Not.True);
        }

        [Test]
        public async Task ExpectedLine_TrailingSpaces_AutoCorrectsToCorrectPosition()
        {
            // "target   " in file at line 4, agent says position=2, expected_line="target"
            SetupFileContent("a\nb\nc\ntarget   \nd");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 2, newLines: "inserted", expectedLine: "target"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.AutoCorrected, Is.True);
            Assert.That(resp.AppliedPosition, Is.EqualTo(4));
        }

        // ── Candidate context (step 2) ────────────────────────────────────────

        [Test]
        public async Task ExpectedLine_MultipleMatches_CandidatesIncludeContextLines()
        {
            // "dup" at lines 2 and 5; Text should include surrounding lines
            SetupFileContent("before\ndup\nafter\nd\nbefore\ndup\nafter");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 1, newLines: "x", expectedLine: "dup"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.Candidates, Is.Not.Null);
            Assert.That(resp.Candidates.Count, Is.EqualTo(2));
            Assert.That(resp.Candidates[0].Text, Does.Contain("before"));
            Assert.That(resp.Candidates[0].Text, Does.Contain("dup"));
            Assert.That(resp.Candidates[0].Text, Does.Contain("after"));
        }
    }
}
