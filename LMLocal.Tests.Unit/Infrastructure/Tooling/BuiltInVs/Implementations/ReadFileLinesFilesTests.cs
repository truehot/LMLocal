using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LMLocal.Application.SubAgents.ReadRefs;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// read_file_lines with 'files': several ranges in one call, one block per range.
    /// </summary>
    [TestFixture]
    public class ReadFileLinesFilesTests
    {
        private string _tempDir;
        private ReadFileLines _reader;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "LMLocalReadFiles", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            var vsMock = new Mock<IVsDependencies>();
            vsMock.Setup(v => v.IsSolutionOpen).Returns(true);
            vsMock.Setup(v => v.GetSolutionDirectory()).Returns(_tempDir);

            _reader = new ReadFileLines(vsMock.Object, new PathResolver(), new DefaultFileSystem());

            File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "a1\na2\na3\na4\na5\n");
            File.WriteAllText(Path.Combine(_tempDir, "b.txt"), "b1\nb2\nb3\n");
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        private static JObject Range(string file, int start, int end)
        {
            return new JObject { ["file_path"] = file, ["start_line"] = start, ["end_line"] = end };
        }

        private async Task<ReadFileLines.ReadFilesResponse> ReadFilesAsync(object files, Dictionary<string, object> extra = null)
        {
            var parameters = extra ?? new Dictionary<string, object>();
            parameters["files"] = files;
            var result = await _reader.ExecuteAsync(parameters);
            Assert.That(result, Is.TypeOf<ReadFileLines.ReadFilesResponse>());
            return (ReadFileLines.ReadFilesResponse)result;
        }

        [Test]
        public async Task Files_ReturnsOneBlockPerRangeInOrder()
        {
            var response = await ReadFilesAsync(new JArray(Range("b.txt", 2, 3), Range("a.txt", 1, 2), Range("a.txt", 4, 99)));

            Assert.That(response.Success, Is.True);
            Assert.That(response.Files.Select(f => f.FilePath), Is.EqualTo(new[] { "b.txt", "a.txt", "a.txt" }));
            Assert.That(response.Files[0].Content.Select(l => l.Text), Is.EqualTo(new[] { "b2", "b3" }));
            Assert.That(response.Files[1].Content.Select(l => l.LineNumber), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(response.Files[1].HasMoreResults, Is.True);
            Assert.That(response.Files[2].Content.Select(l => l.Text), Is.EqualTo(new[] { "a4", "a5" }));
            Assert.That(response.Files[2].TotalLines, Is.EqualTo(5));
            Assert.That(_reader.GetCompletionMessage(response), Is.EqualTo("Read 6 lines from 3 of 3 ranges."));
        }

        [Test]
        public async Task Files_FailedEntryFailsOnlyItsBlock()
        {
            var response = await ReadFilesAsync(new JArray(Range("missing.txt", 1, 5), Range("a.txt", 1, 1),
                new JObject { ["file_path"] = "b.txt", ["start_line"] = 3, ["end_line"] = 1 }));

            Assert.That(response.Success, Is.True);
            Assert.That(response.Files[0].Success, Is.False);
            Assert.That(response.Files[0].ErrorMessage, Does.Contain("File not found"));
            Assert.That(response.Files[1].Success, Is.True);
            Assert.That(response.Files[2].ErrorMessage, Does.Contain("end_line must be greater"));
            Assert.That(_reader.GetCompletionMessage(response), Does.StartWith("Read 1 line from 1 of 3 ranges (2 failed: File not found"));
        }

        [Test]
        public async Task Files_AllFailed_IsNotSuccess()
        {
            var response = await ReadFilesAsync(new JArray(Range("missing.txt", 1, 5)));

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("No range was read"));
            Assert.That(_reader.GetCompletionMessage(response), Does.StartWith("Read lines failed: File not found"));
        }

        [Test]
        public async Task Files_OneRange_MessageSaysOneRange()
        {
            var response = await ReadFilesAsync(new JArray(Range("a.txt", 1, 3)));

            Assert.That(_reader.GetCompletionMessage(response), Is.EqualTo("Read 3 lines from 1 range."));
        }

        [Test]
        public async Task Files_AsJsonString_IsParsed()
        {
            var response = await ReadFilesAsync("[{\"file_path\":\"a.txt\",\"start_line\":1,\"end_line\":1}]");

            Assert.That(response.Files.Single().Content.Single().Text, Is.EqualTo("a1"));
        }

        [Test]
        public async Task Files_WithTopLevelFilePath_ReadsItFirst()
        {
            var response = await ReadFilesAsync(new JArray(Range("a.txt", 1, 1)), new Dictionary<string, object>
            {
                ["file_path"] = "b.txt",
                ["start_line"] = 1,
                ["end_line"] = 1
            });

            Assert.That(response.Files.Select(f => f.FilePath), Is.EqualTo(new[] { "b.txt", "a.txt" }));
        }

        [Test]
        public async Task Files_NotArrayOrTooMany_IsTopLevelError()
        {
            var notArray = await ReadFilesAsync(Range("a.txt", 1, 1));
            Assert.That(notArray.Success, Is.False);
            Assert.That(notArray.ErrorMessage, Does.Contain("array of objects"));

            var itemNotObject = await ReadFilesAsync(new JArray("a.txt"));
            Assert.That(itemNotObject.ErrorMessage, Does.Contain("must be an object"));

            var empty = await ReadFilesAsync(new JArray());
            Assert.That(empty.ErrorMessage, Does.Contain("no entries"));

            var many = await ReadFilesAsync(new JArray(Enumerable.Range(1, ReadFileLines.MaxFiles + 1).Select(i => (object)Range("a.txt", 1, 1)).ToArray()));
            Assert.That(many.ErrorMessage, Does.StartWith($"At most {ReadFileLines.MaxFiles} ranges per call (got {ReadFileLines.MaxFiles + 1})."));
        }

        [Test]
        public async Task SingleRead_WithoutFiles_IsUnchanged()
        {
            var result = await _reader.ExecuteAsync(new Dictionary<string, object> { ["file_path"] = "a.txt", ["start_line"] = 1, ["end_line"] = 2 });

            Assert.That(result, Is.TypeOf<ReadFileLines.FileLinesResponse>());
            Assert.That(JObject.FromObject(result).Property("files"), Is.Null);
        }

        [Test]
        public void ProcessingMessage_ListsTheRanges()
        {
            var message = _reader.GetProcessingMessage(new Dictionary<string, object>
            {
                ["files"] = new JArray(Range("a.txt", 1, 20), Range("b.txt", 5, 9))
            });

            Assert.That(message, Is.EqualTo("Reading 'a.txt' 1-20, 'b.txt' 5-9... "));
        }

        // ---- omitted start_line / end_line --------------------------------------------------

        private void WriteLongFile(int lines)
        {
            File.WriteAllText(Path.Combine(_tempDir, "long.txt"),
                string.Join("\n", Enumerable.Range(1, lines).Select(i => "l" + i)) + "\n");
        }

        [Test]
        public async Task Single_NoEndLine_ReadsDefaultWindowFromStart()
        {
            WriteLongFile(450);

            var result = (ReadFileLines.FileLinesResponse)await _reader.ExecuteAsync(new Dictionary<string, object> { ["file_path"] = "long.txt", ["start_line"] = 101 });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Content.First().LineNumber, Is.EqualTo(101));
            Assert.That(result.Content.Last().LineNumber, Is.EqualTo(100 + ReadFileLines.DefaultWindowLines));
            Assert.That(result.HasMoreResults, Is.True);
            Assert.That(result.TotalLines, Is.EqualTo(450));
        }

        [Test]
        public async Task Single_NoStartAndNoEnd_ReadsDefaultWindowFromLineOne()
        {
            WriteLongFile(450);

            var result = (ReadFileLines.FileLinesResponse)await _reader.ExecuteAsync(new Dictionary<string, object>
            {
                ["file_path"] = "long.txt",
                ["start_line"] = null,
                ["end_line"] = JValue.CreateNull()
            });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Content.Count, Is.EqualTo(ReadFileLines.DefaultWindowLines));
            Assert.That(result.Content.First().LineNumber, Is.EqualTo(1));
        }

        [Test]
        public async Task Single_NoEndLine_ShortFileReadsToTheEnd()
        {
            var result = (ReadFileLines.FileLinesResponse)await _reader.ExecuteAsync(new Dictionary<string, object> { ["file_path"] = "a.txt" });

            Assert.That(result.Content.Select(l => l.Text), Is.EqualTo(new[] { "a1", "a2", "a3", "a4", "a5" }));
            Assert.That(result.HasMoreResults, Is.False);
            Assert.That(result.TotalLines, Is.EqualTo(5));
        }

        [Test]
        public async Task Single_NonIntegerBound_IsStillError()
        {
            var badEnd = (ReadFileLines.FileLinesResponse)await _reader.ExecuteAsync(new Dictionary<string, object> { ["file_path"] = "a.txt", ["start_line"] = 1, ["end_line"] = "end" });
            Assert.That(badEnd.Success, Is.False);
            Assert.That(badEnd.ErrorMessage, Is.EqualTo("end_line parameter must be an integer."));

            var badStart = (ReadFileLines.FileLinesResponse)await _reader.ExecuteAsync(new Dictionary<string, object> { ["file_path"] = "a.txt", ["start_line"] = "x" });
            Assert.That(badStart.ErrorMessage, Is.EqualTo("start_line parameter must be an integer."));
        }

        [Test]
        public async Task Single_EndLineBelowStart_ErrorExplainsLineNumberNotCount()
        {
            var result = (ReadFileLines.FileLinesResponse)await _reader.ExecuteAsync(new Dictionary<string, object> { ["file_path"] = "a.txt", ["start_line"] = 413, ["end_line"] = 30 });

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(
                "end_line must be greater than or equal to start_line (got start_line 413, end_line 30). " +
                "end_line is the last line number to read, not a line count: to read 30 lines from line 413, pass end_line 442, or omit end_line."));
        }

        [Test]
        public async Task Files_EntryWithoutEndLine_ReadsDefaultWindow()
        {
            var response = await ReadFilesAsync(new JArray(new JObject { ["file_path"] = "b.txt", ["start_line"] = 2 }, Range("a.txt", 1, 1)));

            Assert.That(response.Files[0].Content.Select(l => l.Text), Is.EqualTo(new[] { "b2", "b3" }));
            Assert.That(response.Files[1].Content.Single().Text, Is.EqualTo("a1"));
        }

        [Test]
        public void ProcessingMessage_ShowsTheDefaultWindow()
        {
            Assert.That(_reader.GetProcessingMessage(new Dictionary<string, object> { ["file_path"] = "a.txt" }),
                Is.EqualTo($"Reading lines 1-{ReadFileLines.DefaultWindowLines} of 'a.txt'... "));
            Assert.That(_reader.GetProcessingMessage(new Dictionary<string, object> { ["file_path"] = "a.txt", ["start_line"] = 10 }),
                Is.EqualTo($"Reading lines 10-{9 + ReadFileLines.DefaultWindowLines} of 'a.txt'... "));
            Assert.That(_reader.GetProcessingMessage(new Dictionary<string, object> { ["files"] = new JArray(new JObject { ["file_path"] = "b.txt", ["start_line"] = 5, ["end_line"] = "x" }) }),
                Is.EqualTo("Reading 'b.txt' 5-?... "));
        }

        [Test]
        public async Task Recorder_RecordsEverySuccessfulBlock()
        {
            var response = await ReadFilesAsync(new JArray(Range("a.txt", 1, 2), Range("missing.txt", 1, 2), Range("b.txt", 2, 3)));
            var ledger = new ReadLedger();

            Assert.That(ReadResultRecorder.TryRecord(ledger, response), Is.True);

            Assert.That(ledger.Count, Is.EqualTo(2));
            Assert.That(ledger.TryResolve("a.txt", new LineRange(1, 2), out _), Is.True);
            Assert.That(ledger.TryResolve("b.txt", new LineRange(2, 3), out _), Is.True);
        }

        [Test]
        public async Task MarkdownFormatter_FormatsEverySuccessfulBlock()
        {
            var response = await ReadFilesAsync(new JArray(Range("a.txt", 1, 1), Range("missing.txt", 1, 2), Range("b.txt", 3, 3)));

            var markdown = new ToolResultMarkdownFormatter().FormatToolResults(new[] { ("read_file_lines", JsonConvert.SerializeObject(response)) });

            Assert.That(markdown, Does.Contain("a.txt").And.Contain("a1").And.Contain("b.txt").And.Contain("b3"));
            Assert.That(markdown, Does.Not.Contain("missing.txt"));
        }
    }
}
