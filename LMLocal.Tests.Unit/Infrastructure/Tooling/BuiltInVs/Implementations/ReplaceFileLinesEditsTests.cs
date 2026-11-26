using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Syntax;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// replace_file_lines with 'edits': several edits of one file from the same read, applied bottom-up in one write,
    /// all or nothing. Docs\multi-replace-file-lines-plan.md.
    /// </summary>
    [TestFixture]
    public class ReplaceFileLinesEditsTests
    {
        private const string SolutionDir = @"C:\solution";
        private const string FilePath = "test.cs";
        private const string AbsolutePath = @"C:\solution\test.cs";

        private Mock<IFileSystem> _fileSystemMock;
        private Mock<ISnapshotManager> _snapshotMock;
        private Mock<ISyntaxCheckerFactory> _syntaxFactoryMock;
        private ReplaceFileLines _tool;
        private List<string> _written;

        [SetUp]
        public void SetUp()
        {
            var vsMock = new Mock<IVsDependencies>();
            vsMock.Setup(v => v.IsSolutionOpen).Returns(true);
            vsMock.Setup(v => v.GetSolutionDirectory()).Returns(SolutionDir);

            var pathResolverMock = new Mock<IPathResolver>();
            string resolvedPath = AbsolutePath;
            pathResolverMock.Setup(p => p.TryResolveFilePath(It.IsAny<string>(), It.IsAny<string>(), out resolvedPath)).Returns(true);
            pathResolverMock.Setup(p => p.IsPathInsideDirectory(AbsolutePath, SolutionDir)).Returns(true);
            string relativePath = FilePath;
            pathResolverMock.Setup(p => p.TryGetRelativePath(AbsolutePath, SolutionDir, out relativePath)).Returns(true);

            _fileSystemMock = new Mock<IFileSystem>();
            _fileSystemMock.Setup(f => f.FileExists(AbsolutePath)).Returns(true);
            _written = new List<string>();
            _fileSystemMock
                .Setup(f => f.WriteAllBytesWithEncodingAsync(AbsolutePath, It.IsAny<string>(), It.IsAny<Encoding>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, Encoding, bool, CancellationToken>((p, content, e, b, c) => _written.Add(content))
                .Returns(Task.CompletedTask);

            _snapshotMock = new Mock<ISnapshotManager>();
            _syntaxFactoryMock = new Mock<ISyntaxCheckerFactory>();
            _syntaxFactoryMock.Setup(f => f.GetChecker(It.IsAny<string>())).Returns((ISyntaxChecker)null);

            _tool = new ReplaceFileLines(vsMock.Object, pathResolverMock.Object, _snapshotMock.Object, _fileSystemMock.Object, _syntaxFactoryMock.Object);
        }

        private void SetupFile(string content)
        {
            _fileSystemMock
                .Setup(f => f.ReadAllTextWithDetectedEncodingAsync(AbsolutePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync((content, Encoding.UTF8, false));
        }

        private static JObject Edit(int startLine, string oldLines, string newLines)
            => new JObject { { "start_line", startLine }, { "old_lines", oldLines }, { "new_lines", newLines } };

        private static Dictionary<string, object> Params(params JObject[] edits)
            => new Dictionary<string, object> { ["file_path"] = FilePath, ["edits"] = new JArray(edits) };

        private async Task<ReplaceLinesBatchResponse> RunAsync(Dictionary<string, object> parameters)
        {
            var result = await _tool.ExecuteAsync(parameters);
            Assert.That(result, Is.InstanceOf<ReplaceLinesBatchResponse>(), JsonConvert.SerializeObject(result));
            return (ReplaceLinesBatchResponse)result;
        }

        private void AssertNothingWritten()
        {
            Assert.That(_written, Is.Empty);
            _snapshotMock.Verify(s => s.SnapshotFileAsync(It.IsAny<string>(), It.IsAny<SnapshotChangeStatus>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // ── Applying ──────────────────────────────────────────────────────────

        [Test]
        public async Task Edits_InAnyOrder_AreAppliedAgainstTheSameRead()
        {
            SetupFile("a\nb\nc\nd\ne\nf");

            // Top-down order on purpose: line numbers of the second edit refer to the file as read, not as changed by the first.
            var resp = await RunAsync(Params(Edit(2, "b", "B1\nB2\nB3"), Edit(5, "e", "E")));

            Assert.That(resp.Success, Is.True, resp.ErrorMessage);
            Assert.That(_written.Single(), Is.EqualTo("a\nB1\nB2\nB3\nc\nd\nE\nf"));
            Assert.That(resp.Edits.Select(e => e.StartLine), Is.EqualTo(new[] { 2, 5 }), "results follow the request order");
            Assert.That(resp.Edits[0].LinesReplaced, Is.EqualTo(3));
            Assert.That(resp.Edits[1].LinesReplaced, Is.EqualTo(1));
        }

        [Test]
        public async Task Edits_SameResultAsSingleCallsBottomUp()
        {
            const string file = "l1\nl2\nl3\nl4\nl5\nl6\nl7";
            SetupFile(file);
            await RunAsync(Params(Edit(6, "l6", "X"), Edit(2, "l2\nl3", ""), Edit(4, "l4", "Y\nZ")));
            string batch = _written.Single();

            // Same edits as three single calls, bottom-up, each on the file the previous one wrote.
            string current = file;
            foreach (var (start, oldLines, newLines) in new[] { (6, "l6", "X"), (4, "l4", "Y\nZ"), (2, "l2\nl3", "") })
            {
                SetupFile(current);
                _written.Clear();
                await _tool.ExecuteAsync(new Dictionary<string, object>
                {
                    ["file_path"] = FilePath, ["start_line"] = start, ["old_lines"] = oldLines, ["new_lines"] = newLines
                });
                current = _written.Single();
            }

            Assert.That(batch, Is.EqualTo(current));
        }

        [Test]
        public async Task Edits_InsertThroughAnchorLine_KeepsTheAnchor()
        {
            SetupFile("using A;\nclass C\n{\n}");

            var resp = await RunAsync(Params(
                Edit(1, "using A;", "using A;\nusing B;"),
                Edit(3, "{", "{\n    int x;")));

            Assert.That(resp.Success, Is.True, resp.ErrorMessage);
            Assert.That(_written.Single(), Is.EqualTo("using A;\nusing B;\nclass C\n{\n    int x;\n}"));
        }

        [Test]
        public async Task Edits_OneSnapshotAndOneWrite_KeepsCrLfAndTrailingNewline()
        {
            SetupFile("a\r\nb\r\nc\r\n");

            var resp = await RunAsync(Params(Edit(1, "a", "A"), Edit(3, "c", "C")));

            Assert.That(resp.Success, Is.True, resp.ErrorMessage);
            Assert.That(_written.Single(), Is.EqualTo("A\r\nb\r\nC\r\n"));
            _snapshotMock.Verify(s => s.SnapshotFileAsync(AbsolutePath, SnapshotChangeStatus.BeforeModify, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task Edits_DeletionReportsLinesRemoved()
        {
            SetupFile("a\nb\nc\nd");

            var resp = await RunAsync(Params(Edit(2, "b\nc", "")));

            Assert.That(_written.Single(), Is.EqualTo("a\nd"));
            Assert.That(resp.Edits[0].LinesRemoved, Is.EqualTo(2));
            Assert.That(resp.Edits[0].LinesReplaced, Is.Null);
        }

        [Test]
        public async Task Edits_ShiftedEdit_IsAutoCorrected()
        {
            SetupFile("a\nb\nc\ntarget\nd");

            var resp = await RunAsync(Params(Edit(2, "target", "T"), Edit(1, "a", "A")));

            Assert.That(resp.Success, Is.True, resp.ErrorMessage);
            Assert.That(_written.Single(), Is.EqualTo("A\nb\nc\nT\nd"));
            Assert.That(resp.Edits[0].AutoCorrected, Is.True);
            Assert.That(resp.Edits[0].AppliedStartLine, Is.EqualTo(4));
            Assert.That(resp.Edits[1].AutoCorrected, Is.Null);
        }

        [Test]
        public async Task Edits_TopLevelEditIsTheFirst()
        {
            SetupFile("a\nb\nc");
            var parameters = Params(Edit(3, "c", "C"));
            parameters["start_line"] = 1;
            parameters["old_lines"] = "a";
            parameters["new_lines"] = "A";

            var resp = await RunAsync(parameters);

            Assert.That(_written.Single(), Is.EqualTo("A\nb\nC"));
            Assert.That(resp.Edits.Select(e => e.StartLine), Is.EqualTo(new[] { 1, 3 }));
        }

        // Issue #39: a client that fills every schema property sends empty top-level fields next to 'edits'.
        [Test]
        public async Task Edits_PlaceholderTopLevelFields_AreIgnored()
        {
            SetupFile("a\nb\nc");
            var parameters = Params(Edit(1, "a", "A"), Edit(3, "c", "C"));
            parameters["start_line"] = 1;
            parameters["old_lines"] = "";
            parameters["new_lines"] = "";

            var resp = await RunAsync(parameters);

            Assert.That(resp.Success, Is.True, resp.ErrorMessage);
            Assert.That(_written.Single(), Is.EqualTo("A\nb\nC"));
            Assert.That(resp.Edits.Select(e => e.StartLine), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(resp.Note, Does.Contain("Top-level start_line/old_lines/new_lines were ignored"));
        }

        [Test]
        public async Task Edits_TopLevelStartLineOnly_IsIgnored()
        {
            SetupFile("a\nb");
            var parameters = Params(Edit(2, "b", "B"));
            parameters["start_line"] = 2;

            var resp = await RunAsync(parameters);

            Assert.That(resp.Success, Is.True, resp.ErrorMessage);
            Assert.That(_written.Single(), Is.EqualTo("a\nB"));
        }

        [Test]
        public async Task Edits_TopLevelInsertWithoutAnchor_IsRejectedWithHint()
        {
            SetupFile("a\nb");
            var parameters = Params(Edit(2, "b", "B"));
            parameters["start_line"] = 1;
            parameters["old_lines"] = "";
            parameters["new_lines"] = "x";

            var resp = await RunAsync(parameters);

            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.StartWith("the top-level edit: old_lines is empty but new_lines is set").And.Contain("repeat it in new_lines"));
            AssertNothingWritten();
        }

        [Test]
        public async Task Edits_ItemInsertWithoutAnchor_IsRejectedWithHint()
        {
            SetupFile("a\nb");

            var resp = await RunAsync(Params(Edit(2, "", "x")));

            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.StartWith("edits[0]: old_lines is empty but new_lines is set"));
            AssertNothingWritten();
        }

        [Test]
        public async Task Edits_TopLevelEditRepeatedInEdits_IsAppliedOnce()
        {
            SetupFile("a\nb\nc");
            var parameters = Params(Edit(1, "a", "A"), Edit(3, "c", "C"));
            parameters["start_line"] = 1;
            parameters["old_lines"] = "a";
            parameters["new_lines"] = "A";

            var resp = await RunAsync(parameters);

            Assert.That(resp.Success, Is.True, resp.ErrorMessage);
            Assert.That(_written.Single(), Is.EqualTo("A\nb\nC"));
            Assert.That(resp.Edits.Select(e => e.StartLine), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(resp.Note, Does.Contain("edits[0] repeats the top-level edit"));
        }

        [Test]
        public async Task Edits_RepeatedItem_IsAppliedOnce()
        {
            SetupFile("a\nb");

            var resp = await RunAsync(Params(Edit(2, "b", "B"), Edit(2, "b", "B")));

            Assert.That(resp.Success, Is.True, resp.ErrorMessage);
            Assert.That(_written.Single(), Is.EqualTo("a\nB"));
            Assert.That(resp.Edits, Has.Count.EqualTo(1));
            Assert.That(resp.Note, Does.Contain("edits[1] repeats edits[0]"));
        }

        [Test]
        public async Task Edits_SameRangeDifferentNewLines_StillOverlap()
        {
            SetupFile("a\nb");

            var resp = await RunAsync(Params(Edit(2, "b", "B"), Edit(2, "b", "X")));

            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("overlaps"));
            AssertNothingWritten();
        }

        [Test]
        public async Task Edits_AsJsonString_AreAccepted()
        {
            SetupFile("a\nb");
            var parameters = new Dictionary<string, object>
            {
                ["file_path"] = FilePath,
                ["edits"] = new JArray(Edit(2, "b", "B")).ToString(Formatting.None)
            };

            var resp = await RunAsync(parameters);

            Assert.That(resp.Success, Is.True, resp.ErrorMessage);
            Assert.That(_written.Single(), Is.EqualTo("a\nB"));
        }

        // ── No-op ─────────────────────────────────────────────────────────────

        [Test]
        public async Task Edits_AllNoOp_WritesNothing()
        {
            SetupFile("a\nb");

            var resp = await RunAsync(Params(Edit(1, "a", "a"), Edit(2, "b", "b")));

            Assert.That(resp.Success, Is.True);
            Assert.That(resp.Edits.All(e => e.NoOp == true), Is.True);
            Assert.That(_tool.GetCompletionMessage(resp), Is.EqualTo("No changes needed."));
            AssertNothingWritten();
        }

        [Test]
        public async Task Edits_OneNoOp_OthersAreWritten()
        {
            SetupFile("a\nb");

            var resp = await RunAsync(Params(Edit(1, "a", "a"), Edit(2, "b", "B")));

            Assert.That(_written.Single(), Is.EqualTo("a\nB"));
            Assert.That(resp.Edits[0].NoOp, Is.True);
            Assert.That(resp.Edits[1].NoOp, Is.Null);
            Assert.That(_tool.GetCompletionMessage(resp), Does.StartWith("1 edit applied"));
        }

        // ── All or nothing ────────────────────────────────────────────────────

        [Test]
        public async Task Edits_OneDoesNotMatch_NothingIsWritten()
        {
            SetupFile("a\nb\nc");

            var resp = await RunAsync(Params(Edit(1, "a", "A"), Edit(3, "WRONG", "X")));

            Assert.That(resp.Success, Is.False);
            Assert.That(resp.FailedEdit, Is.EqualTo("edits[1]"));
            Assert.That(resp.ErrorMessage, Does.StartWith("edits[1] (start_line 3):").And.Contain("Nothing was written"));
            Assert.That(_tool.GetCompletionMessage(resp), Does.Contain("edits[1] did not match"));
            AssertNothingWritten();
        }

        [Test]
        public async Task Edits_FirstLineFoundBlockMismatch_ErrorNamesTheDifferingLine()
        {
            SetupFile("a\nb\nc;");

            var resp = await RunAsync(Params(Edit(1, "a\nb\nc", "X")));

            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.StartWith("edits[0] (start_line 1): First line of old_lines found at 1 location(s)"));
            Assert.That(resp.ErrorMessage, Does.Contain("line 3 of old_lines (file line 3) differs at column 2: the file has ';' (U+003B), old_lines has end of line."));
            Assert.That(resp.ErrorMessage, Does.EndWith("Nothing was written: fix this edit and resend all edits."));
            AssertNothingWritten();
        }

        [Test]
        public async Task Edits_Ambiguous_ReturnsCandidates()
        {
            SetupFile("x\ndup\ny\ndup\nz");

            var resp = await RunAsync(Params(Edit(1, "x", "X"), Edit(3, "dup", "D")));

            Assert.That(resp.Success, Is.False);
            Assert.That(resp.FailedEdit, Is.EqualTo("edits[1]"));
            Assert.That(resp.Candidates, Has.Count.EqualTo(2));
            AssertNothingWritten();
        }

        [Test]
        public async Task Edits_DuplicateLinesWithoutIndent_EachTakenAtItsStartLine_NoNote()
        {
            // 5920dad5: three identical catch lines, old_lines without indentation, each start_line on its own copy.
            SetupFile("x\n    dup;\ny\n    dup;\nz\n    dup;");

            var resp = await RunAsync(Params(Edit(2, "dup;", "a;"), Edit(4, "dup;", "b;"), Edit(6, "dup;", "c;")));

            Assert.That(resp.Success, Is.True, resp.ErrorMessage);
            Assert.That(_written.Single(), Is.EqualTo("x\n    a;\ny\n    b;\nz\n    c;"));
            Assert.That(resp.Edits.Select(e => e.Note), Is.All.Null);
            Assert.That(resp.Edits.Select(e => e.MatchedIgnoringFirstLineIndent), Is.All.True);
        }

        [Test]
        public async Task Edits_OneOfTwoDuplicates_NoteNamesTheOtherOnly()
        {
            SetupFile("x\n    dup;\ny\n    dup;\nz");

            var resp = await RunAsync(Params(Edit(1, "x", "X"), Edit(4, "dup;", "b;")));

            Assert.That(resp.Success, Is.True, resp.ErrorMessage);
            Assert.That(_written.Single(), Is.EqualTo("X\n    dup;\ny\n    b;\nz"));
            Assert.That(resp.Edits[0].Note, Is.Null);
            Assert.That(resp.Edits[1].Note, Is.EqualTo(
                "old_lines also matches line(s) 2 (first-line indentation ignored); start_line 4 was used, the others were left unchanged."));
        }

        [Test]
        public async Task Edits_PastTheEndOfTheFile_AreRejectedWithHint()
        {
            SetupFile("a\nb");

            var resp = await RunAsync(Params(Edit(1, "a", "A"), Edit(5, "x", "y")));

            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("must lie within the file (2 line(s))"));
            AssertNothingWritten();
        }

        [Test]
        public async Task Edits_StartLineFarPastTheEnd_IsRejectedWithoutOverflow()
        {
            SetupFile("a\nb");

            var resp = await RunAsync(Params(Edit(int.MaxValue, "x", "y")));

            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("past the end of the file").And.Contain("must lie within the file"));
            AssertNothingWritten();
        }

        [Test]
        public async Task Edits_Overlapping_AreRejected()
        {
            SetupFile("a\nb\nc\nd");

            var resp = await RunAsync(Params(Edit(1, "a\nb", "X"), Edit(2, "b\nc", "Y")));

            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("edits[0] (lines 1-2) overlaps edits[1] (lines 2-3)").And.Contain("Merge them"));
            AssertNothingWritten();
        }

        [Test]
        public async Task Edits_AdjacentRanges_AreAccepted()
        {
            SetupFile("a\nb\nc");

            var resp = await RunAsync(Params(Edit(1, "a", "A"), Edit(2, "b", "B")));

            Assert.That(resp.Success, Is.True, resp.ErrorMessage);
            Assert.That(_written.Single(), Is.EqualTo("A\nB\nc"));
        }

        // ── Parsing ───────────────────────────────────────────────────────────

        [Test]
        public async Task Edits_Empty_IsError()
        {
            SetupFile("a");

            var resp = await RunAsync(new Dictionary<string, object> { ["file_path"] = FilePath, ["edits"] = new JArray() });

            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("has no edits"));
        }

        [Test]
        public async Task Edits_MoreThanTheLimit_IsError()
        {
            SetupFile("a");
            var edits = Enumerable.Range(1, ReplaceFileLines.MaxEdits + 1).Select(i => Edit(i, "x" + i, "y")).ToArray();

            var resp = await RunAsync(Params(edits));

            Assert.That(resp.ErrorMessage, Does.Contain($"At most {ReplaceFileLines.MaxEdits} edits per call (got {ReplaceFileLines.MaxEdits + 1})"));
            AssertNothingWritten();
        }

        [TestCase("{\"old_lines\":\"a\",\"new_lines\":\"b\"}", "edits[0]: start_line")]
        [TestCase("{\"start_line\":0,\"old_lines\":\"a\",\"new_lines\":\"b\"}", "edits[0]: start_line")]
        [TestCase("{\"start_line\":1,\"old_lines\":\"\",\"new_lines\":\"b\"}", "edits[0]: old_lines")]
        [TestCase("{\"start_line\":1,\"old_lines\":\"a\"}", "edits[0]: new_lines")]
        public async Task Edits_InvalidItem_IsError(string item, string expected)
        {
            SetupFile("a");

            var resp = await RunAsync(new Dictionary<string, object> { ["file_path"] = FilePath, ["edits"] = JArray.Parse("[" + item + "]") });

            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.StartWith(expected));
        }

        [Test]
        public async Task Edits_NotAnArray_IsError()
        {
            var resp = await RunAsync(new Dictionary<string, object> { ["file_path"] = FilePath, ["edits"] = "not json" });

            Assert.That(resp.ErrorMessage, Does.Contain("must be an array of objects"));
        }

        [Test]
        public void ProcessingMessage_CountsEdits()
        {
            var message = _tool.GetProcessingMessage(Params(Edit(1, "a", "A"), Edit(2, "b", "B")));

            Assert.That(message, Is.EqualTo("Replacing 2 ranges in 'test.cs'... "));
        }

        // ── Single call is unchanged ──────────────────────────────────────────

        [Test]
        public async Task WithoutEdits_ReturnsTheSingleResponse()
        {
            SetupFile("a\nb");

            var result = await _tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["file_path"] = FilePath, ["start_line"] = 2, ["old_lines"] = "b", ["new_lines"] = "B"
            });

            Assert.That(result, Is.InstanceOf<ReplaceLinesResponse>());
            Assert.That(_written.Single(), Is.EqualTo("a\nB"));
        }
    }
}
