using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.SubAgents.ReadRefs;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using LMLocal.Tests.Unit.Infrastructure;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    /// <summary>
    /// Ref markers for code_explorer: search_file_content matches are recorded in a per-run SearchLedger and markers
    /// (@path:line) in the final answer are replaced with 'path:line: `exact text`'.
    /// </summary>
    [TestFixture]
    public class SearchRefExpansionTests
    {
        private const string FilePath = @"LMLocal\Application\Chat\HistoryCompactor.cs";

        private static SearchLedger LedgerWith(params (string path, int line, string text)[] matches)
        {
            var ledger = new SearchLedger();
            foreach (var (path, line, text) in matches)
                ledger.Add(new SearchRecord(path, line, text));
            return ledger;
        }

        private static Task<ReadExpandResult> ExpandAsync(SearchLedger searchLedger, string content, ReadLedger readLedger = null, InMemoryFileSystem fs = null)
        {
            var expander = new ReadRefExpander(readLedger ?? new ReadLedger(), fs ?? new InMemoryFileSystem(), searchLedger);
            return expander.ExpandAsync(content, CancellationToken.None);
        }

        // ---- SearchLedger ------------------------------------------------------------------

        [Test]
        public void Ledger_ResolvesRecordedLine_WithNormalizedPathAndCase()
        {
            var ledger = LedgerWith((FilePath, 19, "internal enum CompactionOutcome"));

            Assert.That(ledger.TryResolve("lmlocal/application/chat/historycompactor.cs", new LineRange(19, 19), out var a), Is.True);
            Assert.That(ledger.TryResolve(@"LMLocal\\Application\\Chat\\HistoryCompactor.cs", new LineRange(19, 19), out var b), Is.True);
            Assert.That(a[0].Text, Is.EqualTo("internal enum CompactionOutcome"));
            Assert.That(b[0].RelativePath, Is.EqualTo(FilePath), "canonical path comes from the tool result");
        }

        [Test]
        public void Ledger_Range_RequiresEveryLineToBeRecorded()
        {
            var ledger = LedgerWith((FilePath, 10, "a"), (FilePath, 11, "b"), (FilePath, 13, "d"));

            Assert.That(ledger.TryResolve(FilePath, new LineRange(10, 11), out var ok), Is.True);
            Assert.That(ok, Has.Count.EqualTo(2));
            Assert.That(ledger.TryResolve(FilePath, new LineRange(10, 13), out _), Is.False, "line 12 was never returned");
            Assert.That(ledger.TryResolve(FilePath, new LineRange(99, 99), out _), Is.False);
            Assert.That(ledger.TryResolve(@"Other\File.cs", new LineRange(10, 10), out _), Is.False);
        }

        [Test]
        public void Ledger_IgnoresInvalidRecords()
        {
            var ledger = LedgerWith(("", 1, "x"), (FilePath, 0, "x"));

            Assert.That(ledger.Count, Is.EqualTo(0));
        }

        // ---- SearchResultRecorder ------------------------------------------------------------

        [Test]
        public void Recorder_FileGrouping_RecordsEveryMatch()
        {
            var ledger = new SearchLedger();
            var response = new SearchResultsResponse
            {
                Success = true,
                Results = new List<SearchResult>
                {
                    new SearchResult
                    {
                        FilePath = FilePath,
                        Matches = new List<SearchMatch>
                        {
                            new SearchMatch { LineNumber = 19, LineText = "internal enum CompactionOutcome" },
                            new SearchMatch { LineNumber = 52, LineText = "Task<CompactionOutcome> CompactForcedAsync(...);" }
                        }
                    }
                }
            };

            Assert.That(SearchResultRecorder.TryRecord(ledger, response), Is.EqualTo(2));
            Assert.That(ledger.TryResolve(FilePath, new LineRange(52, 52), out var r), Is.True);
            Assert.That(r[0].Text, Is.EqualTo("Task<CompactionOutcome> CompactForcedAsync(...);"));
        }

        [Test]
        public void Recorder_TextGrouping_RecordsEveryFileLine()
        {
            var ledger = new SearchLedger();
            var response = new SearchTextGroupResponse
            {
                Success = true,
                Results = new List<SearchTextGroupResult>
                {
                    new SearchTextGroupResult
                    {
                        Text = "using Newtonsoft.Json;",
                        OccurrenceCount = 3,
                        Files = new List<SearchTextGroupFileInfo>
                        {
                            new SearchTextGroupFileInfo { FilePath = @"A\One.cs", Lines = new List<int> { 3 } },
                            new SearchTextGroupFileInfo { FilePath = @"B\Two.cs", Lines = new List<int> { 1, 7 } }
                        }
                    }
                }
            };

            Assert.That(SearchResultRecorder.TryRecord(ledger, response), Is.EqualTo(3));
            Assert.That(ledger.TryResolve(@"B\Two.cs", new LineRange(7, 7), out var r), Is.True);
            Assert.That(r[0].Text, Is.EqualTo("using Newtonsoft.Json;"));
        }

        [Test]
        public void Recorder_FailedOrForeignResult_RecordsNothing()
        {
            var ledger = new SearchLedger();

            Assert.That(SearchResultRecorder.TryRecord(ledger, new SearchResultsResponse { Success = false, Results = new List<SearchResult>() }), Is.EqualTo(0));
            Assert.That(SearchResultRecorder.TryRecord(ledger, "Error: boom"), Is.EqualTo(0));
            Assert.That(SearchResultRecorder.TryRecord(ledger, null), Is.EqualTo(0));
            Assert.That(SearchResultRecorder.TryRecord(null, new SearchResultsResponse { Success = true }), Is.EqualTo(0));
            Assert.That(ledger.Count, Is.EqualTo(0));
        }

        // ---- FileContentRenderer.AppendSearchLocation ----------------------------------------------

        [TestCase("plain text", "p.cs:5: `plain text`")]
        [TestCase("a `tick` b", "p.cs:5: ``a `tick` b``")]
        [TestCase("``double`` run", "p.cs:5: ``` ``double`` run ```")]
        [TestCase("`starts", "p.cs:5: `` `starts ``")]
        [TestCase("", "p.cs:5:")]
        public void AppendSearchLocation_KeepsTextVerbatim(string text, string expected)
        {
            var sb = new StringBuilder();
            FileContentRenderer.AppendSearchLocation(sb, "p.cs", 5, text);

            Assert.That(sb.ToString(), Is.EqualTo(expected));
        }

        // ---- Expansion -----------------------------------------------------------------------------

        [Test]
        public async Task Expand_SearchMarker_IsReplacedWithExactMatchedText()
        {
            // The text the model would have mangled: backticks and \n inside a JS template string.
            const string text = "parts.push(`${formatSpeed(stats.tokenSpeed)}\\n`);";
            var ledger = LedgerWith((@"LMLocal\Resources\js\app.js", 16, text));

            var result = await ExpandAsync(ledger, "Found:\n@LMLocal\\Resources\\js\\app.js:16\nDone.");

            Assert.That(result.ExpandedRefCount, Is.EqualTo(1));
            Assert.That(result.UnresolvedRefCount, Is.EqualTo(0));
            Assert.That(result.Content, Is.EqualTo("Found:\nLMLocal\\Resources\\js\\app.js:16: ``" + text + "``\nDone."));
        }

        [Test]
        public async Task Expand_ListMarkerInBullet_ContinuesTheBulletPerLine()
        {
            var ledger = LedgerWith((FilePath, 19, "internal enum CompactionOutcome"), (FilePath, 52, "Task<CompactionOutcome> X();"));

            var result = await ExpandAsync(ledger, "Matches:\n  - @" + FilePath + ":19,52\nEnd");

            Assert.That(result.ExpandedRefCount, Is.EqualTo(1));
            Assert.That(result.Content, Is.EqualTo(
                "Matches:\n" +
                "  - " + FilePath + ":19: `internal enum CompactionOutcome`\n" +
                "  - " + FilePath + ":52: `Task<CompactionOutcome> X();`\n" +
                "End"));
        }

        [Test]
        public async Task Expand_ListMarkerInNumberedItem_IndentsContinuation()
        {
            var ledger = LedgerWith((FilePath, 1, "a"), (FilePath, 2, "b"));

            var result = await ExpandAsync(ledger, "1. @" + FilePath + ":1-2");

            Assert.That(result.Content, Is.EqualTo("1. " + FilePath + ":1: `a`\n   " + FilePath + ":2: `b`"));
        }

        [Test]
        public async Task Expand_ListMarkerMidSentence_JoinsWithSemicolon()
        {
            var ledger = LedgerWith((FilePath, 1, "a"), (FilePath, 2, "b"));

            var result = await ExpandAsync(ledger, "It is used at @" + FilePath + ":1,2 only.");

            Assert.That(result.Content, Is.EqualTo("It is used at " + FilePath + ":1: `a`; " + FilePath + ":2: `b` only."));
        }

        [Test]
        public async Task Expand_LineNotReturnedBySearch_IsLeftUnresolved()
        {
            var ledger = LedgerWith((FilePath, 19, "internal enum CompactionOutcome"));

            var result = await ExpandAsync(ledger, "@" + FilePath + ":19,20");

            Assert.That(result.ExpandedRefCount, Is.EqualTo(0));
            Assert.That(result.UnresolvedRefCount, Is.EqualTo(1));
            Assert.That(result.Content, Is.EqualTo("@" + FilePath + ":19,20"), "a partly fabricated marker is not expanded at all");
        }

        [Test]
        public async Task Expand_BareSearchLocationListItem_IsTreatedAsMarker()
        {
            // Benchmark A1.11: the model wrote "* path:16" without the leading '@'.
            const string text = "parts.push(`${formatSpeed(stats.tokenSpeed)}`);";
            var ledger = LedgerWith((@"LMLocal\Resources\js\lib\token.stats.js", 16, text));

            var result = await ExpandAsync(ledger, "* LMLocal\\Resources\\js\\lib\\token.stats.js:16");

            Assert.That(result.ExpandedRefCount, Is.EqualTo(1));
            Assert.That(result.Content, Is.EqualTo("* LMLocal\\Resources\\js\\lib\\token.stats.js:16: ``" + text + "``"));
        }

        [Test]
        public async Task Expand_BareLocationNotReturnedBySearch_IsLeftAsIs()
        {
            var ledger = LedgerWith((FilePath, 19, "internal enum CompactionOutcome"));
            string content = "- " + FilePath + ":19,20\n- " + FilePath + ":21";

            var result = await ExpandAsync(ledger, content);

            Assert.That(result.Content, Is.EqualTo(content));
            Assert.That(result.ExpandedRefCount + result.UnresolvedRefCount, Is.EqualTo(0));
        }

        [Test]
        public async Task Expand_BareLocationInsideProse_IsLeftAsIs()
        {
            var ledger = LedgerWith((FilePath, 19, "internal enum CompactionOutcome"));
            string content = "Declared at " + FilePath + ":19 in the file.";

            var result = await ExpandAsync(ledger, content);

            Assert.That(result.Content, Is.EqualTo(content));
        }

        [Test]
        public async Task Expand_NoMarkers_IsNoOp()
        {
            var ledger = LedgerWith((FilePath, 19, "x"));
            const string content = "Line 19: `x` — no markers here; email user@host:8080.";

            var result = await ExpandAsync(ledger, content);

            Assert.That(result.Content, Is.EqualTo(content));
            Assert.That(result.ExpandedRefCount + result.UnresolvedRefCount, Is.EqualTo(0));
        }

        [Test]
        public async Task Expand_ExpandedTextContainingMarkerSyntax_IsNotExpandedAgain()
        {
            var ledger = LedgerWith((FilePath, 1, "see @" + FilePath + ":2"), (FilePath, 2, "SECOND"));

            var result = await ExpandAsync(ledger, "@" + FilePath + ":1");

            Assert.That(result.ExpandedRefCount, Is.EqualTo(1));
            Assert.That(result.Content, Does.Contain("`see @" + FilePath + ":2`"));
            Assert.That(result.Content, Does.Not.Contain("SECOND"));
        }

        [Test]
        public async Task Expand_ReadLedgerCoverage_TakesPriorityOverSearch()
        {
            const string abs = @"C:\sol\src\Program.cs";
            var fs = new InMemoryFileSystem();
            await fs.WriteAllBytesAsync(abs, Encoding.UTF8.GetBytes("line 1\nline 2"));
            var read = new ReadLedger();
            read.Add(new ReadRecord(@"src\Program.cs", 1, 2, abs));
            var search = LedgerWith((@"src\Program.cs", 1, "line 1"));

            var result = await ExpandAsync(search, @"@src\Program.cs:1", read, fs);

            Assert.That(result.Content, Does.StartWith(@"File: src\Program.cs, lines 1-1"));
        }

        [Test]
        public async Task Expand_WithoutSearchLedger_KeepsReaderBehavior()
        {
            var expander = new ReadRefExpander(new ReadLedger(), new InMemoryFileSystem());

            var result = await expander.ExpandAsync("@" + FilePath + ":19", CancellationToken.None);

            Assert.That(result.UnresolvedRefCount, Is.EqualTo(1));
            Assert.That(result.Content, Is.EqualTo("@" + FilePath + ":19"));
        }
    }
}
