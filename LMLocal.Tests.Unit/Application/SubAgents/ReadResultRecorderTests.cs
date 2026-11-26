using System.Collections.Generic;
using LMLocal.Application.SubAgents.ReadRefs;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    [TestFixture]
    public class ReadResultRecorderTests
    {
        private static ReadFileLines.FileLinesResponse BuildResponse(
            string path, int startLine, int lineCount, bool hasMore = false, string absolutePath = null)
        {
            var content = new List<ReadFileLines.FileLineEntry>(lineCount);
            for (int i = 0; i < lineCount; i++)
            {
                content.Add(new ReadFileLines.FileLineEntry
                {
                    LineNumber = startLine + i,
                    Text = "line " + (startLine + i)
                });
            }

            return new ReadFileLines.FileLinesResponse
            {
                Success = true,
                FilePath = path,
                AbsolutePath = absolutePath ?? (@"C:\sol\" + path),
                Content = content,
                HasMoreResults = hasMore
            };
        }

        [Test]
        public void TryRecord_SuccessfulResponse_IsRecordedWithActualRange()
        {
            var ledger = new ReadLedger();

            var recorded = ReadResultRecorder.TryRecord(
                ledger, BuildResponse(@"LMLocal\Core\StreamChunk.cs", 1, 200, hasMore: true));

            Assert.That(recorded, Is.True);
            Assert.That(ledger.Count, Is.EqualTo(1));
            Assert.That(ledger.TryResolve(@"LMLocal\Core\StreamChunk.cs", new LineRange(1, 200), out var record), Is.True);
            Assert.That(record.StartLine, Is.EqualTo(1));
            Assert.That(record.EndLine, Is.EqualTo(200));
            Assert.That(record.AbsolutePath, Is.EqualTo(@"C:\sol\LMLocal\Core\StreamChunk.cs"));
        }

        [Test]
        public void TryRecord_UsesReturnedLineNumbers_NotRequestedRange()
        {
            var ledger = new ReadLedger();

            // The tool returns lines 30..39 (e.g. end-of-file clamp); the record must reflect that exact range.
            Assert.That(ReadResultRecorder.TryRecord(ledger, BuildResponse("src/Program.cs", 30, 10)), Is.True);

            Assert.That(ledger.TryResolve("src/Program.cs", new LineRange(30, 39), out _), Is.True);
            Assert.That(ledger.TryResolve("src/Program.cs", new LineRange(1, 39), out _), Is.False);
        }

        [Test]
        public void TryRecord_CapturesAbsolutePath_FromResponse()
        {
            var ledger = new ReadLedger();

            Assert.That(ReadResultRecorder.TryRecord(
                ledger, BuildResponse("src/Program.cs", 1, 10, absolutePath: @"C:\sol\src\Program.cs")), Is.True);

            Assert.That(ledger.TryResolve("src/Program.cs", new LineRange(1, 10), out var record), Is.True);
            Assert.That(record.AbsolutePath, Is.EqualTo(@"C:\sol\src\Program.cs"));
        }

        [Test]
        public void TryRecord_ResponseWithoutAbsolutePath_IsNotRecorded()
        {
            var ledger = new ReadLedger();

            // Without an absolute path the expander could not re-read the file, so the read must not be recorded.
            Assert.That(ReadResultRecorder.TryRecord(
                ledger, BuildResponse("src/Program.cs", 1, 10, absolutePath: string.Empty)), Is.False);
            Assert.That(ledger.Count, Is.EqualTo(0));
        }

        [Test]
        public void TryRecord_FailedResult_IsNotRecorded()
        {
            var ledger = new ReadLedger();
            var response = new ReadFileLines.FileLinesResponse { Success = false, ErrorMessage = "boom" };

            Assert.That(ReadResultRecorder.TryRecord(ledger, response), Is.False);
            Assert.That(ledger.Count, Is.EqualTo(0));
        }

        [Test]
        public void TryRecord_EmptyContent_IsNotRecorded()
        {
            var ledger = new ReadLedger();
            var response = new ReadFileLines.FileLinesResponse
            {
                Success = true,
                FilePath = "a.cs",
                AbsolutePath = @"C:\sol\a.cs",
                Content = new List<ReadFileLines.FileLineEntry>()
            };

            Assert.That(ReadResultRecorder.TryRecord(ledger, response), Is.False);
            Assert.That(ledger.Count, Is.EqualTo(0));
        }

        [Test]
        public void TryRecord_UnknownObjectOrNull_IsNotRecorded()
        {
            var ledger = new ReadLedger();

            // A raw JSON string is no longer accepted: in the run loop the tool result is never serialized.
            Assert.That(ReadResultRecorder.TryRecord(ledger, new object()), Is.False);
            Assert.That(ReadResultRecorder.TryRecord(ledger, "{\"success\":true}"), Is.False);
            Assert.That(ReadResultRecorder.TryRecord(ledger, null), Is.False);
            Assert.That(ledger.Count, Is.EqualTo(0));
        }

        [Test]
        public void TryRecord_PagedReads_AreResolvableAsUnion()
        {
            var ledger = new ReadLedger();

            ReadResultRecorder.TryRecord(ledger, BuildResponse("src/Big.cs", 1, 80, hasMore: true));
            ReadResultRecorder.TryRecord(ledger, BuildResponse("src/Big.cs", 81, 80, hasMore: false));

            Assert.That(ledger.TryResolve("src/Big.cs", new LineRange(1, 160), out _), Is.True);
        }

        [Test]
        public void TryRecord_WindowRead_KeepsTotalLines_AndHasNotReachedEnd()
        {
            var ledger = new ReadLedger();
            var response = BuildResponse("src/P.json", 1, 200, hasMore: true);
            response.TotalLines = 254;

            Assert.That(ReadResultRecorder.TryRecord(ledger, response), Is.True);

            var record = ledger.Records[0];
            Assert.That(record.TotalLines, Is.EqualTo(254));
            Assert.That(record.ReachedEnd, Is.False);
        }

        [Test]
        public void TryRecord_ReadToEndOfFile_HasReachedEnd()
        {
            var ledger = new ReadLedger();
            var response = BuildResponse("src/Tests.cs", 1, 41, hasMore: false);
            response.TotalLines = 41;

            Assert.That(ReadResultRecorder.TryRecord(ledger, response), Is.True);

            Assert.That(ledger.Records[0].ReachedEnd, Is.True);
        }

        [Test]
        public void TryRecord_MultiFileRead_KeepsTotalLinesPerBlock()
        {
            var ledger = new ReadLedger();
            var first = BuildResponse("src/A.cs", 1, 200, hasMore: true);
            first.TotalLines = 300;
            var second = BuildResponse("src/B.cs", 1, 50, hasMore: false);
            second.TotalLines = 50;

            ReadResultRecorder.TryRecord(ledger, new ReadFileLines.ReadFilesResponse
            {
                Success = true,
                Files = new List<ReadFileLines.FileLinesResponse> { first, second }
            });

            Assert.That(ledger.Records[0].TotalLines, Is.EqualTo(300));
            Assert.That(ledger.Records[0].ReachedEnd, Is.False);
            Assert.That(ledger.Records[1].TotalLines, Is.EqualTo(50));
            Assert.That(ledger.Records[1].ReachedEnd, Is.True);
        }
    }
}
