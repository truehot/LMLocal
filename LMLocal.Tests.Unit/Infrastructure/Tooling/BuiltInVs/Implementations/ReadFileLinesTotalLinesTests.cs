using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// total_lines is reported with every read of a file up to ReadFileLines.MaxCountedLines lines (the rest of the file is
    /// read only to count lines); a longer file reports it only when the read reached its end.
    /// </summary>
    [TestFixture]
    public class ReadFileLinesTotalLinesTests
    {
        private string _tempDir;
        private ReadFileLines _reader;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "LMLocalTotalLines", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            var vsMock = new Mock<IVsDependencies>();
            vsMock.Setup(v => v.IsSolutionOpen).Returns(true);
            vsMock.Setup(v => v.GetSolutionDirectory()).Returns(_tempDir);

            _reader = new ReadFileLines(vsMock.Object, new PathResolver(), new DefaultFileSystem());

            File.WriteAllText(Path.Combine(_tempDir, "five.txt"), "1\n2\n3\n4\n5\n");
            File.WriteAllText(Path.Combine(_tempDir, "empty.txt"), string.Empty);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        private async Task<ReadFileLines.FileLinesResponse> ReadAsync(string file, int start, int end)
        {
            var result = await _reader.ExecuteAsync(new Dictionary<string, object>
            {
                ["file_path"] = file,
                ["start_line"] = start,
                ["end_line"] = end
            });
            var response = (ReadFileLines.FileLinesResponse)result;
            Assert.That(response.Success, Is.True, response.ErrorMessage);
            return response;
        }

        [Test]
        public async Task RangeInsideTheFile_HasMore_ReportsTotalLines()
        {
            var response = await ReadAsync("five.txt", 1, 2);

            Assert.That(response.HasMoreResults, Is.True);
            Assert.That(response.Content.Count, Is.EqualTo(2));
            Assert.That(response.TotalLines, Is.EqualTo(5));
        }

        [Test]
        public async Task RangeInsideALongFile_OverTheCountLimit_NoTotalLines()
        {
            File.WriteAllLines(Path.Combine(_tempDir, "long.txt"), Enumerable.Range(1, ReadFileLines.MaxCountedLines + 1).Select(i => "l" + i));

            var response = await ReadAsync("long.txt", 1, 2);

            Assert.That(response.HasMoreResults, Is.True);
            Assert.That(response.TotalLines, Is.Null);
            Assert.That(JsonConvert.SerializeObject(response), Does.Not.Contain("total_lines"));
        }

        [Test]
        public async Task RangeInsideAFileAtTheCountLimit_ReportsTotalLines()
        {
            File.WriteAllLines(Path.Combine(_tempDir, "limit.txt"), Enumerable.Range(1, ReadFileLines.MaxCountedLines).Select(i => "l" + i));

            var response = await ReadAsync("limit.txt", 1, 2);

            Assert.That(response.TotalLines, Is.EqualTo(ReadFileLines.MaxCountedLines));
        }

        [Test]
        public async Task RangeEndingAtTheLastLine_ReportsTotalLines()
        {
            var response = await ReadAsync("five.txt", 4, 5);

            Assert.That(response.HasMoreResults, Is.False);
            Assert.That(response.TotalLines, Is.EqualTo(5));
        }

        [Test]
        public async Task WholeFileRead_ReportsTotalLines()
        {
            var response = await ReadAsync("five.txt", 1, 9999);

            Assert.That(response.Content.Count, Is.EqualTo(5));
            Assert.That(response.TotalLines, Is.EqualTo(5));
        }

        [Test]
        public async Task StartPastTheEnd_EmptyContentWithTotalLines()
        {
            // Seen in a real session: lines 455-490 were requested from a file that had 442 lines.
            var response = await ReadAsync("five.txt", 455, 490);

            Assert.That(response.Content, Is.Empty);
            Assert.That(response.HasMoreResults, Is.False);
            Assert.That(response.TotalLines, Is.EqualTo(5));
        }

        [Test]
        public async Task EmptyFile_ReportsZeroLines()
        {
            var response = await ReadAsync("empty.txt", 1, 10);

            Assert.That(response.Content, Is.Empty);
            Assert.That(response.TotalLines, Is.EqualTo(0));
        }
    }
}
