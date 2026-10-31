using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Syntax;
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
                ["start_line"] = 1,
                ["old_lines"] = "o",
                ["new_lines"] = "n"
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
                ["file_path"] = FilePath,
                ["old_lines"] = "o",
                ["new_lines"] = "n"
            });
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_MissingOldLines_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["file_path"] = FilePath,
                ["start_line"] = 1,
                ["new_lines"] = "n"
            });
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
        }

        [Test]
        public async Task ExecuteAsync_MissingNewLines_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["file_path"] = FilePath,
                ["start_line"] = 1,
                ["old_lines"] = "o"
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
        public async Task ExecuteAsync_FirstLineLeadingWhitespace_MatchesIgnoringIndent()
        {
            SetupFileContent("    /// <summary>");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1, oldLines: "/// <summary>", newLines: "    /// <summary>"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.ErrorMessage, Is.Null);
            Assert.That(resp.MatchedIgnoringFirstLineIndent, Is.True);
        }

        [Test]
        public async Task ExecuteAsync_FirstLineLeadingWhitespace_MultiLineBlock_Matches()
        {
            SetupFileContent("    public void M()\n    {\n        return;\n    }");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1,
                oldLines: "public void M()\n    {\n        return;\n    }",
                newLines: "    public void M2()\n    {\n        return;\n    }"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.MatchedIgnoringFirstLineIndent, Is.True);
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

        [Test]
        public async Task Replace_StartLineFarPastEof_IsRejectedWithRealLineCount()
        {
            // start_line is untrusted model input: padding to a hallucinated line number used to allocate that many
            // empty cells before the tool could report anything (CWE-400), and the int sum had no guard against
            // overflow either. The error carries the real length of the file so the model corrects itself at once.
            SetupFileContent("a\nb");

            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1_000_000, oldLines: "b", newLines: "x"));

            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("needs 999998 empty line(s)"));
            Assert.That(resp.ErrorMessage, Does.Contain("a file that has 2 line(s)"));
            Assert.That(resp.ErrorMessage, Does.Contain("at most 50"));
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, It.IsAny<string>(), It.IsAny<Encoding>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task Replace_OldLinesBeyondEofOverCap_IsRejected()
        {
            // The padding also grows with the size of old_lines while start_line stays inside the file: 53 lines of
            // old_lines against a two-line file need 51 padding lines even though start_line is 1. A cap written
            // only against start_line would miss this vector.
            SetupFileContent("a\nb");
            // 54 empty elements joined by "\n" are 53 lines: the last element has no break behind it.
            string oldLines = string.Join("\n", new string[54]);

            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1, oldLines: oldLines, newLines: "x"));

            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("needs 51 empty line(s)"));
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
        // ── JSON-escape asymmetry (investigation, escaped output vs raw input) ──
        // read_file_lines returns each line JSON-serialized, so in the *tool output*
        // the model sees \" and \\ inside "text". But old_lines/new_lines are passed
        // to this tool as RAW characters (argument deserialization is one level only).
        // So:
        //   escaped-looking input (\" / \\\\ as typed in the tool-call JSON value)
        //   → after JSON deserialize yields literal " and \ characters → must NOT match
        //     a file whose line really contains \" / \\\\ (which is the escaped form of
        //     a single " / \). This is the FAIL row in the investigation table.
        //   raw input (single " / \ characters) → matches the real file. SUCCESS row.

        [Test]
        public async Task ExecuteAsync_EscapedDoubleQuote_DoesNotMatchRawQuote_ReturnsError()
        {
            // File really contains: SYM-QUOTE-DOUBLE: "double quoted value"
            SetupFileContent("SYM-QUOTE-DOUBLE: \"double quoted value\"");
            // Model errors by passing the escaped form \" (as seen in JSON tool output).
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1,
                oldLines: "SYM-QUOTE-DOUBLE: \\\"double quoted value\\\"",
                newLines: "replaced"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("Old content not found in file"));
        }

        [Test]
        public async Task ExecuteAsync_RawDoubleQuote_MatchesAndReplaces()
        {
            // File really contains: SYM-QUOTE-DOUBLE: "double quoted value"
            SetupFileContent("SYM-QUOTE-DOUBLE: \"double quoted value\"");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1,
                oldLines: "SYM-QUOTE-DOUBLE: \"double quoted value\"",
                newLines: "SYM-QUOTE-DOUBLE: \"changed value\""));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "SYM-QUOTE-DOUBLE: \"changed value\"",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task ExecuteAsync_RawBackslashString_MatchesAndReplaces()
        {
            // File really contains: \t (one backslash) and escaped string \" (backslash + quote)
            SetupFileContent("var s = \"a\\\"b\";\nvar t = '\\t';");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1,
                oldLines: "var s = \"a\\\"b\";",
                newLines: "var s = \"changed\";"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "var s = \"changed\";\nvar t = '\\t';",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        // ── P1: canonical matching and restore ────────────────────────────────

        [Test]
        public async Task Replace_CanonicalMatch_OldLinesWithNormalizedChars_KeepsFileBytes()
        {
            // The file holds a real no-break space and typographic quotes; the model can only send space + ASCII quotes.
            SetupFileContent("int x = 1;\u00A0// \u201Cmetka\u201D");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1,
                oldLines: "int x = 1; // \"metka\"",
                newLines: "int x = 1; // \"metka\""));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.MatchedWithNormalization, Is.True);
            Assert.That(resp.AutoCorrected, Is.Not.True);
            // Restore turns the block back into the file's own lines, so the range already holds the requested
            // text: the call is a no-op and the invisible characters survive because nothing is written (P2-2).
            Assert.That(resp.IsNoOp, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, It.IsAny<string>(), It.IsAny<Encoding>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task Replace_StrictMatch_ReportsNoNormalizationFlag()
        {
            SetupFileContent("a\nb\nc");
            var result = await _tool.ExecuteAsync(CreateParams(startLine: 2, oldLines: "b", newLines: "x"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.MatchedWithNormalization, Is.Null);
            Assert.That(resp.MatchedIgnoringFirstLineIndent, Is.Null);
        }

        [Test]
        public async Task Replace_StrictBlockAmbiguity_FailsBeforeNormalizationPasses()
        {
            SetupFileContent("a\ndup\nc\nd\ndup\nf");
            var result = await _tool.ExecuteAsync(CreateParams(startLine: 1, oldLines: "dup", newLines: "x"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("matches 2 locations"));
            Assert.That(resp.ErrorMessage, Does.Not.Contain("after character normalization"));
        }

        [Test]
        public async Task Replace_CanonicalBlockMatch_IsFoundBeforeFirstLineBranch()
        {
            // The first line matches strictly, the block only after normalization: pass 4 must win over the
            // "first line found, block didn't match" diagnostics, otherwise this edit is refused needlessly.
            SetupFileContent("p-q\nc\u2014d");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1, oldLines: "p-q\nc-d", newLines: "p-q\nC-D"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.MatchedWithNormalization, Is.True);
            Assert.That(resp.ErrorMessage, Is.Null);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "p-q\nC-D", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_CanonicalMatchAtTwoLocations_ReturnsCandidates()
        {
            SetupFileContent("a\u2014b\na-b");
            var result = await _tool.ExecuteAsync(CreateParams(startLine: 1, oldLines: "a\u2013b", newLines: "X"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("after character normalization"));
            Assert.That(resp.Candidates, Is.Not.Null);
            Assert.That(resp.Candidates.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task Replace_CanonicalMatch_PartialEdit_RestoresUnchangedLines()
        {
            SetupFileContent("class C\n{\n// A \u2192 B\nint x = 1;\nvar y = 1;");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1,
                oldLines: "class C\n{\n// A -> B\nint x = 1;\nvar y = 1;",
                newLines: "class C\n{\n// A -> B\nint x = 1;\nvar y = 2;"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.MatchedWithNormalization, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "class C\n{\n// A \u2192 B\nint x = 1;\nvar y = 2;",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_StrictMatch_WritesNewLinesVerbatim_NoRestore()
        {
            // The model reproduced the no-break space exactly, so its plain space is an intentional edit.
            SetupFileContent("X\u00A0Y");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1, oldLines: "X\u00A0Y", newLines: "X Y"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.MatchedWithNormalization, Is.Null);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "X Y", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_CanonicalMatch_TrailingWhitespaceEdit_IsNotReverted()
        {
            // Restore compares exactly (no TrimEnd), so an intentional trailing-whitespace edit survives.
            SetupFileContent("// A \u2192 B\nint a = 1;   ");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1,
                oldLines: "// A -> B\nint a = 1;   ",
                newLines: "// A -> B\nint a = 1;"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "// A \u2192 B\nint a = 1;",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_CanonicalMatch_InsertLine_RestoresPrefixAndSuffix()
        {
            SetupFileContent("a\nb\n// A \u2192 B\nd\ne");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1,
                oldLines: "a\nb\n// A -> B\nd\ne",
                newLines: "a\nb\n// A -> B\nNEW\nd\ne"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.MatchedWithNormalization, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\nb\n// A \u2192 B\nNEW\nd\ne",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_CanonicalRelaxed_LeadingZwsp_DoesNotDoubleIndent()
        {
            // Pass 5: the model dropped the zero-width space AND the indentation. The leading run is taken from
            // the file exactly once, so the written line keeps a single run even though the edit is applied.
            SetupFileContent("\u200B    /// <summary>");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1, oldLines: "/// <summary>", newLines: "/// <summary> changed"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.MatchedIgnoringFirstLineIndent, Is.True);
            Assert.That(resp.MatchedWithNormalization, Is.True);
            Assert.That(resp.IsNoOp, Is.False);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "\u200B    /// <summary> changed",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_CanonicalRelaxed_ReportsBothNotesInFixedOrder()
        {
            SetupFileContent("\u200B    /// <summary>");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1, oldLines: "/// <summary>", newLines: "/// <summary> changed"));
            var message = _tool.GetCompletionMessage(result);
            Assert.That(message, Is.EqualTo(
                "1 line replaced (matched ignoring first-line indentation, matched after character normalization)."));
        }

        [Test]
        public async Task Replace_SpaceInsteadOfLineSeparator_MatchesCanonically()
        {
            // The file holds U+2028 where the model sent a plain space; the edit inside the line is written as sent.
            SetupFileContent("word1\u2028word2");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1, oldLines: "word1 word2", newLines: "word1 word3"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.MatchedWithNormalization, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "word1 word3", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_CanonicalMatch_EditInsideLineWithSpecialChars_LosesThem_DocumentsBoundary()
        {
            // Boundary, not a protection: an edit inside the line is written as the model sent it (no per-fragment restore).
            SetupFileContent("int x = 1;\u00A0// \u2192 vazhno");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1,
                oldLines: "int x = 1; // -> vazhno",
                newLines: "int x = 2; // -> vazhno"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.MatchedWithNormalization, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "int x = 2; // -> vazhno",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_CanonicalMatch_DeletedLineDoesNotOverwriteRestoredPrefix()
        {
            // The no-break space sits inside the line, so only the canonical pass matches; the suffix must not walk
            // back into the line the prefix already restored.
            SetupFileContent("int x\u00A0= 1;\nint x = 1;\nz");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1,
                oldLines: "int x = 1;\nint x = 1;\nz",
                newLines: "int x = 1;\nz"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.MatchedWithNormalization, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "int x\u00A0= 1;\nz",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_CanonicalMatch_UniformlyTrimmedTrailingSpaces_AreKept()
        {
            // The model strips trailing spaces on every context line; matching tolerates it and restore turns every
            // line back into the file's own one, so the range already holds the requested text: a no-op, and the
            // trailing spaces are kept because nothing is written (P2-2).
            SetupFileContent("// A \u2192 B\nint a = 1;   ");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1,
                oldLines: "// A -> B\nint a = 1;",
                newLines: "// A -> B\nint a = 1;"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.IsNoOp, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, It.IsAny<string>(), It.IsAny<Encoding>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task Replace_CanonicalMatch_BlockPastEndOfFile_DoesNotThrow()
        {
            // The second old_lines line only matches the padding: restore indexes must stay inside the real file.
            SetupFileContent("a\nb\u2192c");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "b->c\n\n", newLines: "B"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.MatchedWithNormalization, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\nB", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_CanonicalMatch_DeleteLastLine_DoesNotRestoreKeptLine()
        {
            // "b" is ordinally equal to old middle and NOT to old last: the kept line must not be replaced by "c".
            SetupFileContent("a\u00A0x\nb\nc");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1, oldLines: "a x\nb\nc", newLines: "X\nb"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.MatchedWithNormalization, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "X\nb", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_CanonicalMatch_DeleteLastLine_DoesNotMoveDeletedContent()
        {
            // E20 guard: the second disjunct is limited to oldIdx == newIdx, so the NBSP of the deleted third line
            // cannot migrate onto the kept second line.
            SetupFileContent("a\u00A0x\nx = 1\nx\u00A0= 1");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1,
                oldLines: "a x\nx = 1\nx\u00A0= 1",
                newLines: "X\nx = 1"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "X\nx = 1", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_CanonicalMatch_RewrittenContextLine_IsRestoredFromFile()
        {
            // Same length: the model rewrote a context line, but its canonical form still equals the file, so the
            // second disjunct applies (the E20 guard must not disable it where it is legitimate).
            SetupFileContent("a\u2014b\nvar y = 1;");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1, oldLines: "a-b\nvar y = 1;", newLines: "a\u2013b\nvar y = 2;"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\u2014b\nvar y = 2;", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        // ── P1: boundary conditions of the restore indexes ────────────────────

        [Test]
        public async Task Replace_CanonicalMatch_EmptyNewLines_DeletesRangeWithoutRestore()
        {
            // Empty new_lines on the canonical path: the block still has to be found after normalization, there is
            // nothing to restore, and the range is deleted.
            SetupFileContent("a\u2192b\nc");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1, oldLines: "a->b\nc", newLines: ""));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.MatchedWithNormalization, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_EmptyFile_ReturnsNotFoundWithoutThrowing()
        {
            SetupFileContent("");
            var result = await _tool.ExecuteAsync(CreateParams(startLine: 1, oldLines: "x", newLines: "X"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("Old content not found in file"));
        }

        [Test]
        public async Task Replace_DeleteLastLineOfFile_Succeeds()
        {
            SetupFileContent("a\nb");
            var result = await _tool.ExecuteAsync(CreateParams(startLine: 2, oldLines: "b", newLines: ""));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_LastLineWithSingleBreak_KeepsOneBlankLine()
        {
            // Check 44в: "\n" is the documented way to replace a range with one empty line. The file had no
            // trailing break, so only the blank last line asked for one — without it the file was written as ""
            // and lost its only line, while the response reported one line replaced.
            SetupFileContent("a");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1, oldLines: "a", newLines: "\n"));
            var resp = result as ReplaceLinesResponse;

            Assert.That(resp.Success, Is.True);
            Assert.That(resp.LinesReplaced, Is.EqualTo(1));
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "\n", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_NewLinesEndingWithBlankLine_NoTrailingNewline_KeepsTheBlankLine()
        {
            // Check 44в: the file has no trailing break, so nothing but the blank last line of new_lines can ask
            // for the break that keeps it: "a\nX\n" would read back as two lines instead of three.
            SetupFileContent("a\nb");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "b", newLines: "X\n\n"));
            var resp = result as ReplaceLinesResponse;

            Assert.That(resp.Success, Is.True);
            Assert.That(resp.LinesReplaced, Is.EqualTo(2));
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\nX\n\n", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_DeleteLineAfterBlankLine_KeepsTheBlankLine()
        {
            // Check 44в: the blank line that becomes the last one must survive the write, otherwise deleting the
            // last line silently deletes two.
            SetupFileContent("a\n\nb");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 3, oldLines: "b", newLines: ""));
            var resp = result as ReplaceLinesResponse;

            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\n\n", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_CanonicalMatch_BlockOfPadding_RestoreIsNoOp()
        {
            // The whole block matches only padding: both old_lines are "invisible" and canonicalize to nothing.
            // realCount is then 0 while restore is still entered with new_lines present, so it must return early
            // instead of indexing the padding. A single invisible line cannot be used here: every padded empty
            // line would match it and the pass would report an ambiguity instead of a match.
            SetupFileContent("a");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "\u200B\n\u200B", newLines: "X"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.MatchedWithNormalization, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\nX", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_StrictMatch_OnFileWithSpecialChars_DoesNotNormalize()
        {
            // Strict match on a file that does contain the arrow: the model sent it verbatim, so nothing is
            // normalized, no restore runs and no window cache is needed.
            SetupFileContent("// A\u2192B\nvar y = 1;");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1, oldLines: "// A\u2192B\nvar y = 1;", newLines: "// A\u2192B\nvar y = 2;"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.MatchedWithNormalization, Is.Null);
            Assert.That(resp.MatchedIgnoringFirstLineIndent, Is.Null);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "// A\u2192B\nvar y = 2;", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        // ── Line counts in the response and in the completion message (E18, P2-8) ──

        [Test]
        public async Task Replace_CompletionMessage_ReportsLineCount()
        {
            // Check 34: the message must carry the number of written lines, symmetric with
            // "6 lines inserted" of insert_file_lines — the model reads the transcript, not the JSON.
            string[] lines = { "a", "b", "c", "d", "e", "f", "g", "h" };
            SetupFileContent(string.Join("\n", lines));
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1,
                oldLines: string.Join("\n", lines),
                newLines: string.Join("\n", new[] { "A", "B", "C", "D", "E", "F", "G", "H" })));
            Assert.That(_tool.GetCompletionMessage(result), Is.EqualTo("8 lines replaced."));
        }

        [Test]
        public async Task Replace_SingleLineEdit_CompletionMessageUsesSingular()
        {
            SetupFileContent("line1");
            var result = await _tool.ExecuteAsync(CreateParams());
            Assert.That(_tool.GetCompletionMessage(result), Is.EqualTo("1 line replaced."));
        }

        [Test]
        public async Task Replace_Deletion_CompletionMessageSaysRemoved()
        {
            // A deletion reported as "0 lines replaced" would describe the opposite of what happened,
            // so the verb changes and the count moves to lines_removed.
            string[] lines = { "a", "b", "c", "d", "e", "f", "g", "h" };
            SetupFileContent(string.Join("\n", lines));
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1, oldLines: string.Join("\n", lines), newLines: ""));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.LinesReplaced, Is.EqualTo(0));
            Assert.That(resp.LinesRemoved, Is.EqualTo(8));
            Assert.That(_tool.GetCompletionMessage(result), Is.EqualTo("8 lines removed."));
        }

        [Test]
        public async Task Replace_Response_LinesReplacedMatchesAppliedRange()
        {
            // lines_replaced counts written lines (new_lines), not matched ones: here one old line
            // becomes three, and only the written side describes the applied change.
            SetupFileContent("a\nb\nc");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "b", newLines: "B1\nB2\nB3"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.LinesReplaced, Is.EqualTo(3));
            Assert.That(resp.LinesRemoved, Is.Null);
        }

        [Test]
        public async Task Replace_NotFound_ResponseReportsZeroLinesReplaced()
        {
            SetupFileContent("a");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1, oldLines: "zzz_not_present", newLines: "X"));
            var resp = result as ReplaceLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.LinesReplaced, Is.EqualTo(0));
            Assert.That(resp.LinesRemoved, Is.Null);
        }

        // ── No-op at the write point and the phantom trailing line (P2-2, checks 2, 34, 44, 44б) ──

        [Test]
        public async Task Replace_NoOp_DoesNotSnapshotAndDoesNotWrite()
        {
            // The request asks for exactly what the file already holds. "Not written" is proven with the mocks:
            // mtime on a fast file system can coincide with the read, so it is only the third witness.
            SetupFileContent("a\nb");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "b", newLines: "b"));
            var resp = result as ReplaceLinesResponse;

            Assert.That(resp.Success, Is.True);
            Assert.That(resp.IsNoOp, Is.True);
            Assert.That(resp.LinesReplaced, Is.EqualTo(0));
            Assert.That(resp.LinesRemoved, Is.Null);

            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, It.IsAny<string>(), It.IsAny<Encoding>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), Times.Never);
            _snapshotMock.Verify(s => s.SnapshotFileAsync(
                AbsolutePath, SnapshotChangeStatus.BeforeModify, It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task Replace_NoOp_CompletionMessageSaysNoChangesNeeded()
        {
            // "0 lines replaced" would describe the opposite of what happened, and lines_replaced: 0 is also the
            // value of a deletion, so the signal has to be the message (E21).
            SetupFileContent("a\nb");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "b", newLines: "b"));

            Assert.That(_tool.GetCompletionMessage(result), Is.EqualTo("No changes needed."));
        }

        [Test]
        public async Task Replace_NoOp_OldLinesBeyondEof_IsNotNoOp()
        {
            // Check 44: the second old_lines line exists only as padding. The written range is one line while
            // new_lines has two, so this is an edit that adds a line — not a no-op.
            SetupFileContent("a\nb");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "b\n\n", newLines: "b\nc"));
            var resp = result as ReplaceLinesResponse;

            Assert.That(resp.Success, Is.True);
            Assert.That(resp.IsNoOp, Is.False);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\nb\nc", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_NoOp_OldLinesBeyondEof_SameLength_IsNoOp()
        {
            // Control for check 44: one written line against one real line is a genuine no-op.
            SetupFileContent("a\nb");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "b\n\n", newLines: "b"));
            var resp = result as ReplaceLinesResponse;

            Assert.That(resp.Success, Is.True);
            Assert.That(resp.IsNoOp, Is.True);
        }

        [Test]
        public async Task Replace_PhantomTrailingEmpty_IsNotNoOp()
        {
            // Check 44б: the file ends with a line break. A phantom trailing element used to inflate realCount to
            // 2 and made ["b",""] look identical to the requested block, silently dropping the edit.
            SetupFileContent("a\nb\n");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "b\n\n", newLines: "b\n\n"));
            var resp = result as ReplaceLinesResponse;

            Assert.That(resp.Success, Is.True);
            Assert.That(resp.IsNoOp, Is.False);
            Assert.That(resp.LinesReplaced, Is.EqualTo(2));
            // The blank line is added and the trailing line break survives.
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\nb\n\n", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_LastLineEdit_KeepsTrailingNewline()
        {
            // Check 44б, second vector: RemoveRange used to eat the phantom instead of the real line and
            // InsertRange did not recreate it, so "a\nB\n" became "a\nB".
            SetupFileContent("a\nb\n");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 2, oldLines: "b", newLines: "B"));
            var resp = result as ReplaceLinesResponse;

            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\nB\n", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Replace_OldLinesWithCrOnlySeparators_MatchesByLines()
        {
            // E6/P2-3: old_lines glued by the model with lone CR must match line by line, and a lone CR is a
            // separator, not part of a line. Same text as the file, therefore a no-op.
            SetupFileContent("a\nb\nc\n");
            var result = await _tool.ExecuteAsync(CreateParams(
                startLine: 1, oldLines: "a\rb\rc", newLines: "a\rb\rc"));
            var resp = result as ReplaceLinesResponse;

            Assert.That(resp.Success, Is.True);
            Assert.That(resp.IsNoOp, Is.True);
            Assert.That(resp.LinesReplaced, Is.EqualTo(0));
        }
    }
}
