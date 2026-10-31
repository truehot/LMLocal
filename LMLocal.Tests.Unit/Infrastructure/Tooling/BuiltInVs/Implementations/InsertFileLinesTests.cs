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
        public async Task ExpectedLine_PositionBeyondFile_IsCheckedAndFails()
        {
            // A position past the end of the file used to skip the expected_line check entirely, so the caller
            // padded the file and inserted blindly — the anchor the model sent was never looked at. The anchor is
            // now checked, the error names the real length of the file, and nothing is written.
            SetupFileContent("a");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 5, newLines: "new", expectedLine: "anything"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("expected_line not found"));
            Assert.That(resp.ErrorMessage, Does.Contain("Position 5 is past the end of the file, which has 1 line(s)"));
            _snapshotMock.Verify(s => s.SnapshotFileAsync(
                AbsolutePath, SnapshotChangeStatus.BeforeModify, It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ExpectedLine_PositionBeyondFile_AnchorFound_AutoCorrectsToRealLine()
        {
            // The useful half of the same case: the anchor does exist, so the position is auto-corrected instead of
            // the insert being guessed from a position the file does not have.
            SetupFileContent("a\nb");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 4, newLines: "X", expectedLine: "b"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.AutoCorrected, Is.True);
            Assert.That(resp.OriginalPosition, Is.EqualTo(4));
            Assert.That(resp.AppliedPosition, Is.EqualTo(2));
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\nb\nX\n", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
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

        // ── P1: canonical matching of expected_line ───────────────────────────

        [Test]
        public async Task Insert_ExpectedLineInNormalizedForm_MatchesCanonically()
        {
            // expected_line arrives with a plain space where the file has a no-break space. Without the canonical
            // fallback the tool reports "expected_line not found"; with it the insertion lands on line 2.
            SetupFileContent("a\nlabel\u00A0x\nc");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 3, newLines: "inserted", expectedLine: "label x"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.MatchedWithNormalization, Is.True);
            Assert.That(resp.AutoCorrected, Is.True);
            Assert.That(resp.AppliedPosition, Is.EqualTo(2));
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\nlabel\u00A0x\ninserted\nc",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Insert_ExpectedLineInNormalizedForm_ReportsNormalizationInCompletionMessage()
        {
            SetupFileContent("a\nlabel\u00A0x\nc");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 3, newLines: "inserted", expectedLine: "label x"));

            var message = _tool.GetCompletionMessage(result);
            Assert.That(message, Is.EqualTo(
                "1 line inserted (auto-corrected from position 3 to 2, matched after character normalization)."));
        }

        // ── One line model for file and text (P2-0 / P2-4 / P2-5, checks 9, 10, 40, 45, 45б, 47) ──

        [Test]
        public async Task Insert_MixedEol_InsertsAtRequestedLine()
        {
            // E11: splitting by the file separator turned "b\nc" into a single line, so the new text landed at the
            // end of the file instead of after line 2.
            SetupFileContent("a\r\nb\nc\r\n");

            var result = await _tool.ExecuteAsync(CreateParams(position: 2, newLines: "X"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\r\nb\r\nX\r\nc\r\n",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Insert_CrOnlyFile_UsesTheSameLineNumbersAsRead()
        {
            // A CR-only file is numbered by lone CR (ReadLine), so "insert after line 2" means after "b".
            // The written EOL is unified to the file's separator — that is E5, deliberately out of scope.
            SetupFileContent("a\rb\rc");

            var result = await _tool.ExecuteAsync(CreateParams(position: 2, newLines: "X"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\nb\nX\nc",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Insert_CrOnlyFile_AppendAtEnd()
        {
            // Appending at the end keeps the CR-only file's own trailing break instead of adding a second line.
            SetupFileContent("a\rb\rc");

            var result = await _tool.ExecuteAsync(CreateParams(position: 3, newLines: "X"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\nb\nc\nX\n",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task InsertFileLines_NewLinesWithTrailingNewline_DoesNotAddBlankLine()
        {
            // Check 10: "X\n" is one line, not two — the trailing break is not a line.
            SetupFileContent("a\nb");

            var result = await _tool.ExecuteAsync(CreateParams(position: 1, newLines: "X\n"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.LinesInserted, Is.EqualTo(1));
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\nX\nb",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Insert_NewLinesSingleBreak_InsertsOneBlankLine()
        {
            // A single break is the only way to express "one empty line" (new_lines = "" is rejected).
            SetupFileContent("a\nb");

            var result = await _tool.ExecuteAsync(CreateParams(position: 1, newLines: "\n"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.LinesInserted, Is.EqualTo(1));
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\n\nb",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Insert_EmptyFile_Position0_KeepsNoTrailingNewline()
        {
            // Check 40а: an empty file is zero lines, so position 0 would count as "append" and add a break.
            // The current shape (no trailing break) is kept on purpose.
            SetupFileContent("");

            var result = await _tool.ExecuteAsync(CreateParams(position: 0, newLines: "X"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "X", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Insert_EmptyFile_Position0_NewLinesEndingWithBlankLine_KeepsTheBlankLine()
        {
            // Check 40б: the inserted block ends with an empty line. Written as "X\n" it reads back as the single
            // line "X", so the blank line disappeared while the response still counted two lines.
            SetupFileContent("");

            var result = await _tool.ExecuteAsync(CreateParams(position: 0, newLines: "X\n\n"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.LinesInserted, Is.EqualTo(2));
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "X\n\n", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Insert_EmptyFile_Position0_SingleBreak_IsNotASilentNoOp()
        {
            // Check 40б: "\n" is one empty line, but it used to be joined to "" — the file stayed empty (zero
            // lines) while the response claimed one line was inserted.
            SetupFileContent("");

            var result = await _tool.ExecuteAsync(CreateParams(position: 0, newLines: "\n"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.LinesInserted, Is.EqualTo(1));
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "\n", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Insert_Append_NewLinesEndingWithBlankLine_KeepsTheBlankLine()
        {
            // The append branch already asks for a break; the blank last line must not consume it twice.
            SetupFileContent("abc");

            var result = await _tool.ExecuteAsync(CreateParams(position: 1, newLines: "X\n\n"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.LinesInserted, Is.EqualTo(2));
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "abc\nX\n\n", Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Insert_PositionBeyondEof_PadsAndAppends()
        {
            // Check 40: padding past the end of the file is an append, so the last line gets a break.
            SetupFileContent("a");

            var result = await _tool.ExecuteAsync(CreateParams(position: 5, newLines: "new"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\n\n\n\n\nnew\n",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Insert_PaddingUpToTheCap_IsStillAllowed()
        {
            // The cap must not eat the legitimate "append a little past the end" case: 50 empty lines are the
            // documented maximum, so exactly 50 still work.
            SetupFileContent("a");

            var result = await _tool.ExecuteAsync(CreateParams(position: 51, newLines: "new"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
        }

        [Test]
        public async Task Insert_PositionFarPastEof_IsRejectedWithRealLineCount()
        {
            // The position is untrusted model input: padding to a hallucinated line number used to allocate that
            // many empty cells before the tool could report anything (CWE-400). The error has to carry the real
            // length of the file, otherwise the model corrects itself blindly.
            SetupFileContent("a\nb");

            var result = await _tool.ExecuteAsync(CreateParams(position: 1_000_000, newLines: "new"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("999998 line(s) past the end of the file"));
            Assert.That(resp.ErrorMessage, Does.Contain("which has 2 line(s)"));
            Assert.That(resp.ErrorMessage, Does.Contain("at most 50"));
            _snapshotMock.Verify(s => s.SnapshotFileAsync(
                AbsolutePath, SnapshotChangeStatus.BeforeModify, It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task Insert_ExpectedLineEmpty_TreatedAsAbsent()
        {
            // Check 45б: "" keeps meaning "not provided" — the validation stays on IsNullOrEmpty, so "" must not
            // become a cardinality error.
            SetupFileContent("a\nb");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 1, newLines: "X", expectedLine: ""));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.AutoCorrected, Is.Not.True);
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\nX\nb",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Insert_ExpectedLineSingleBreak_MatchesEmptyLine()
        {
            // Check 45б: "\n" parses to exactly one empty line and finds the empty line of the file on purpose,
            // instead of matching it accidentally through TrimEnd.
            SetupFileContent("a\n\nc");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 1, newLines: "X", expectedLine: "\n"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.AppliedPosition, Is.EqualTo(2));
            _fileSystemMock.Verify(f => f.WriteAllBytesWithEncodingAsync(
                AbsolutePath, "a\n\nX\nc",
                Encoding.UTF8, false, It.IsAny<CancellationToken>()));
        }

        [Test]
        public async Task Insert_ExpectedLineMultiLine_ReturnsCardinalityError()
        {
            // Check 45: a two-line expected_line never matched anything and produced a misleading "not found".
            SetupFileContent("a\nb");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 1, newLines: "X", expectedLine: "a\nb"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Is.EqualTo(
                "expected_line must be a single line (got 2). Use replace_file_lines for a multi-line block."));
        }

        [Test]
        public async Task Insert_ExpectedLineWithTrailingBreak_FindsTheLine()
        {
            // Check 45: the trailing break the model captured is removed by parsing, not by comparison.
            SetupFileContent("a\nint x = 1;\nc");

            var result = await _tool.ExecuteAsync(CreateParams(
                position: 1, newLines: "X", expectedLine: "int x = 1;\r\n"));

            var resp = result as InsertLinesResponse;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.AppliedPosition, Is.EqualTo(2));
        }
    }
}
