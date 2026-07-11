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
        private Mock<ISyntaxCheckerFactory> _syntaxFactoryMock;
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

            _tool = new ReplaceFileLines(
                _vsMock.Object, _pathResolverMock.Object, _snapshotMock.Object,
                _fileSystemMock.Object, _syntaxFactoryMock.Object);
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

        // ── Mismatch — not found ──────────────────────────────────────────────

        [Test]
        public async Task ExecuteAsync_OldContentMismatch_ReturnsError()
        {
            SetupFileContent("actualContent");
            // "xyz_nonexistent" does not appear anywhere in the file
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1, oldLines: "xyz_nonexistent", newLines: "replacement"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("Old content not found in file"));
        }

        [Test]
        public async Task ExecuteAsync_OldLinesLongerThanFile_ReturnsError()
        {
            SetupFileContent("short");
            // "zzz_not_present" is not in the file at all
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1,
                oldLines: "zzz_not_present",
                newLines: "x"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("Old content not found in file"));
        }

        [Test]
        public async Task ExecuteAsync_StartLineBeyondFile_PadsAndFailsOnMismatch()
        {
            SetupFileContent("a\nb");
            // "zzz_unique" won't match padded empty lines and won't be found
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 5,
                oldLines: "zzz_unique",
                newLines: "x"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("Old content not found in file"));
        }

        // ── Mismatch — first line found, block doesn't match ──────────────────

        [Test]
        public async Task ExecuteAsync_FirstLineFound_BlockMismatch_ReturnsCandidates()
        {
            SetupFileContent("a\r\nb\r\nc");
            // First line "a" exists, but block "a\nWRONG" doesn't match anywhere
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1,
                oldLines: "a\r\nWRONG",
                newLines: "x"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("First line of old_lines found"));
            Assert.That(resp.Candidates, Is.Not.Null);
            Assert.That(resp.Candidates.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(resp.Candidates[0].StartLine, Is.EqualTo(1));
            Assert.That(resp.Candidates[0].Text, Is.EqualTo("a\nb\nc"));
        }

        // ── Auto-correct: block found at exactly one location ─────────────────

        [Test]
        public async Task ExecuteAsync_BlockShifted_AutoCorrectsToActualPosition()
        {
            // File: "a\nb\nc\ntarget\nd\ne"
            // Agent thinks "target" is at line 2, but it's at line 4
            SetupFileContent("a\nb\nc\ntarget\nd\ne");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2,          // agent's wrong guess
                oldLines: "target",    // unique — only at line 4
                newLines: "replaced"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.AutoCorrected, Is.True);
            Assert.That(resp.OriginalStartLine, Is.EqualTo(2));
            Assert.That(resp.AppliedStartLine, Is.EqualTo(4));
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\nb\nc\nreplaced\nd\ne",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_MultiLineBlockShifted_AutoCorrects()
        {
            // File: "a\nb\nc\nd\ne\nf"
            // Block "c\nd" is at lines 3-4. Agent guesses line 1.
            SetupFileContent("a\nb\nc\nd\ne\nf");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1,
                oldLines: "c\nd",
                newLines: "x\ny"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.AutoCorrected, Is.True);
            Assert.That(resp.OriginalStartLine, Is.EqualTo(1));
            Assert.That(resp.AppliedStartLine, Is.EqualTo(3));
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\nb\nx\ny\ne\nf",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        // ── Candidates: block found at multiple locations ─────────────────────

        [Test]
        public async Task ExecuteAsync_BlockMatchesMultipleLocations_ReturnsCandidates()
        {
            // "dup" appears at lines 2 and 5
            SetupFileContent("a\ndup\nc\nd\ndup\nf");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1,
                oldLines: "dup",
                newLines: "x"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("matches 2 locations"));
            Assert.That(resp.Candidates, Is.Not.Null);
            Assert.That(resp.Candidates.Count, Is.EqualTo(2));
            Assert.That(resp.Candidates[0].StartLine, Is.EqualTo(2));
            Assert.That(resp.Candidates[0].Text, Is.EqualTo("a\ndup\nc\nd"));
            Assert.That(resp.Candidates[1].StartLine, Is.EqualTo(5));
            Assert.That(resp.Candidates[1].Text, Is.EqualTo("c\nd\ndup\nf"));
        }

        [Test]
        public async Task ExecuteAsync_MultiLineBlockMultipleMatches_ReturnsCandidates()
        {
            // "b\nc" appears at 2-3 and 5-6
            SetupFileContent("a\nb\nc\nd\nb\nc\ne");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1,
                oldLines: "b\nc",
                newLines: "x"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("matches 2 locations"));
            Assert.That(resp.Candidates, Is.Not.Null);
            Assert.That(resp.Candidates.Count, Is.EqualTo(2));
            Assert.That(resp.Candidates[0].StartLine, Is.EqualTo(2));
            Assert.That(resp.Candidates[1].StartLine, Is.EqualTo(5));
        }

        // ── Auto-correct: exact match (no correction needed) ──────────────────

        [Test]
        public async Task ExecuteAsync_ExactMatch_NoAutoCorrectFlag()
        {
            SetupFileContent("a\nb\nc");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2,
                oldLines: "b",
                newLines: "x"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.AutoCorrected, Is.Not.True);
        }

        [Test]
        public async Task ExecuteAsync_ExactMatch_BlockWithinWindow_NoCorrection()
        {
            // Block at line 2, agent says line 2 — exact match, no correction
            SetupFileContent("a\nb\nc\nd\ne\nf\ng\nh\ni\nj");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2,
                oldLines: "b",
                newLines: "x"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.AutoCorrected, Is.Not.True);
        }

        // ── Success cases ─────────────────────────────────────────────────────

        [Test]
        public async Task ExecuteAsync_Success_ReplacesSingleLine()
        {
            SetupFileContent("line1\r\nline2\r\nline3");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "line2", newLines: "modified2"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            _snapshotMock.Verify(s => s.SnapshotFileAsync(
                AbsolutePath, SnapshotChangeStatus.BeforeModify, It.IsAny<CancellationToken>()));
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "line1\r\nmodified2\r\nline3",
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
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\r\nx\r\ny\r\nd",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_Success_ReplaceOneLineWithThree_Succeeds()
        {
            SetupFileContent("a\r\nb\r\nc");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2,
                oldLines: "b",
                newLines: "x\r\ny\r\nz"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\r\nx\r\ny\r\nz\r\nc",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_Success_UnixLineEndings()
        {
            SetupFileContent("a\nb\nc");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "b", newLines: "x"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\nx\nc",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_Success_NewLinesWithTrailingNewline_TrimsCorrectly()
        {
            SetupFileContent("line1\r\nline2\r\nline3");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "line2", newLines: "x\r\n"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "line1\r\nx\r\nline3",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_Success_OldLinesUnixNewline_FileWindows_Matches()
        {
            SetupFileContent("a\r\nb\r\nc");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "b\n", newLines: "x"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\r\nx\r\nc",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        // ── Delete cases ─────────────────────────────────────────────────────

        [Test]
        public async Task ExecuteAsync_DeleteSingleLine_Succeeds()
        {
            SetupFileContent("line1\r\nline2\r\nline3");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "line2", newLines: ""));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "line1\r\nline3",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_DeleteMultipleLines_Succeeds()
        {
            SetupFileContent("a\r\nb\r\nc\r\nd");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "b\r\nc", newLines: ""));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\r\nd",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_DeleteWithTrailingNewlineInOldLines_Succeeds()
        {
            SetupFileContent("line1\r\nline2\r\nline3");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "line2\r\n", newLines: ""));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "line1\r\nline3",
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

        // ── Trailing whitespace tolerance (step 1) ────────────────────────────

        [Test]
        public async Task ExecuteAsync_OldLinesHasTrailingSpaces_MatchesAndReplaces()
        {
            // File line has no trailing space, old_lines has trailing space — should still match
            SetupFileContent("a\nb\nc");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "b   ", newLines: "x"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\nx\nc",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_FileLineHasTrailingSpaces_MatchesAndReplaces()
        {
            // File line has trailing spaces, old_lines does not — should still match
            SetupFileContent("a\nb   \nc");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "b", newLines: "x"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\nx\nc",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_TrailingSpaceMismatch_AutoCorrectsToCorrectLine()
        {
            // Block at line 4 with trailing space in file; agent says line 2 with no trailing space
            SetupFileContent("a\nb\nc\ntarget   \nd");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "target", newLines: "replaced"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.AutoCorrected, Is.True);
            Assert.That(resp.AppliedStartLine, Is.EqualTo(4));
        }

        // ── Candidate context (step 2) ────────────────────────────────────────

        [Test]
        public async Task ExecuteAsync_MultipleMatches_CandidatesIncludeContextLines()
        {
            // "dup" at lines 2 and 5; each candidate Text should contain surrounding lines
            SetupFileContent("before\ndup\nafter\nd\nbefore\ndup\nafter");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1, oldLines: "dup", newLines: "x"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.Candidates, Is.Not.Null);
            Assert.That(resp.Candidates.Count, Is.EqualTo(2));
            // Text should contain more than just the matched line (context included)
            Assert.That(resp.Candidates[0].Text, Does.Contain("before"));
            Assert.That(resp.Candidates[0].Text, Does.Contain("dup"));
            Assert.That(resp.Candidates[0].Text, Does.Contain("after"));
        }
    }
}
