using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.SubAgents.ReadRefs;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Tests.Unit.Infrastructure;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    [TestFixture]
    public class SubAgentRunEvidenceTests
    {
        private const string RelativePath = @"src\Program.cs";
        private const string AbsolutePath = @"C:\sol\src\Program.cs";
        private const string SearchPath = @"LMLocal\Application\Chat\HistoryCompactor.cs";

        private static ReadFileLines.FileLinesResponse ReadResponse(int startLine, int lineCount)
        {
            var content = new List<ReadFileLines.FileLineEntry>(lineCount);
            for (int i = 0; i < lineCount; i++)
                content.Add(new ReadFileLines.FileLineEntry { LineNumber = startLine + i, Text = "line " + (startLine + i) });

            return new ReadFileLines.FileLinesResponse
            {
                Success = true,
                FilePath = RelativePath,
                AbsolutePath = AbsolutePath,
                Content = content
            };
        }

        private static SearchResultsResponse SearchResponse()
        {
            return new SearchResultsResponse
            {
                Success = true,
                Results = new List<SearchResult>
                {
                    new SearchResult
                    {
                        FilePath = SearchPath,
                        Matches = new List<SearchMatch> { new SearchMatch { LineNumber = 19, LineText = "internal enum CompactionOutcome" } }
                    }
                }
            };
        }

        private static GetSymbolInfo.SymbolInfoResponse SymbolResponse()
        {
            return new GetSymbolInfo.SymbolInfoResponse
            {
                Success = true,
                References = new List<GetSymbolInfo.ReferenceItem>
                {
                    new GetSymbolInfo.ReferenceItem { FilePath = SearchPath, LineNumber = 52, LineText = "Task<CompactionOutcome> CompactForcedAsync(...);" }
                }
            };
        }

        private static InMemoryFileSystem FileWithLines(int lineCount)
        {
            var sb = new StringBuilder();
            for (int i = 1; i <= lineCount; i++)
            {
                if (i > 1)
                    sb.Append('\n');
                sb.Append("line " + i);
            }

            var fileSystem = new InMemoryFileSystem();
            fileSystem.WriteAllBytesAsync(AbsolutePath, Encoding.UTF8.GetBytes(sb.ToString())).Wait();
            return fileSystem;
        }

        [Test]
        public void New_IsEmpty()
        {
            Assert.That(new SubAgentRunEvidence().IsEmpty, Is.True);
        }

        [Test]
        public void Record_ReadFileLines_FillsEvidence()
        {
            var evidence = new SubAgentRunEvidence();

            evidence.Record("read_file_lines", ReadResponse(1, 10));

            Assert.That(evidence.IsEmpty, Is.False);
        }

        [Test]
        public void Record_SearchAndSymbolTools_FillEvidence_CaseInsensitive()
        {
            var search = new SubAgentRunEvidence();
            search.Record("SEARCH_FILE_CONTENT", SearchResponse());

            var symbol = new SubAgentRunEvidence();
            symbol.Record("get_symbol_info", SymbolResponse());

            Assert.That(search.IsEmpty, Is.False);
            Assert.That(symbol.IsEmpty, Is.False);
        }

        [Test]
        public void Record_OtherTool_IsIgnored()
        {
            var evidence = new SubAgentRunEvidence();

            evidence.Record("find_files", ReadResponse(1, 10));
            evidence.Record("list_directory", SearchResponse());

            Assert.That(evidence.IsEmpty, Is.True);
        }

        [Test]
        public async Task ExpandAsync_NoEvidence_MarkerIsUnresolved()
        {
            var result = await new SubAgentRunEvidence().ExpandAsync("Body:\n@src\\Program.cs:1-10", new InMemoryFileSystem(), CancellationToken.None);

            Assert.That(result, Is.Not.Null, "a marker written before any tool result must still be counted");
            Assert.That(result.ExpandedRefCount, Is.EqualTo(0));
            Assert.That(result.UnresolvedRefCount, Is.EqualTo(1));
            Assert.That(result.UnresolvedRefs, Is.EqualTo(new[] { "@src\\Program.cs:1-10" }));
            Assert.That(result.Content, Is.EqualTo("Body:\n@src\\Program.cs:1-10"));
        }

        [Test]
        public async Task ExpandAsync_NoEvidence_PlainAnswer_HasNothingUnresolved()
        {
            var result = await new SubAgentRunEvidence().ExpandAsync("Yes. 12 matches.", new InMemoryFileSystem(), CancellationToken.None);

            Assert.That(result.UnresolvedRefCount, Is.EqualTo(0));
            Assert.That(result.UnresolvedRefs, Is.Empty);
            Assert.That(result.Content, Is.EqualTo("Yes. 12 matches."));
        }

        [Test]
        public async Task ExpandAsync_EmptyContent_ReturnsNull()
        {
            var evidence = new SubAgentRunEvidence();
            evidence.Record("read_file_lines", ReadResponse(1, 10));

            Assert.That(await evidence.ExpandAsync(null, FileWithLines(10), CancellationToken.None), Is.Null);
            Assert.That(await evidence.ExpandAsync(string.Empty, FileWithLines(10), CancellationToken.None), Is.Null);
            Assert.That(await new SubAgentRunEvidence().ExpandAsync(null, new InMemoryFileSystem(), CancellationToken.None), Is.Null);
        }

        [Test]
        public async Task ExpandAsync_ReadRangeMarker_IsExpanded()
        {
            var evidence = new SubAgentRunEvidence();
            evidence.Record("read_file_lines", ReadResponse(1, 10));

            var result = await evidence.ExpandAsync("@src\\Program.cs:2-4", FileWithLines(10), CancellationToken.None);

            Assert.That(result.ExpandedRefCount, Is.EqualTo(1));
            Assert.That(result.UnresolvedRefCount, Is.EqualTo(0));
            Assert.That(result.Content, Does.Contain("File: src\\Program.cs, lines 2-4"));
        }

        [Test]
        public async Task ExpandAsync_SearchAndSymbolMarkers_AreExpanded()
        {
            var evidence = new SubAgentRunEvidence();
            evidence.Record("search_file_content", SearchResponse());
            evidence.Record("get_symbol_info", SymbolResponse());

            var result = await evidence.ExpandAsync("@" + SearchPath + ":19,52", new InMemoryFileSystem(), CancellationToken.None);

            Assert.That(result.ExpandedRefCount, Is.EqualTo(1));
            Assert.That(result.Content, Does.Contain(SearchPath + ":19:"));
            Assert.That(result.Content, Does.Contain(SearchPath + ":52:"));
        }

        [Test]
        public async Task ExpandAsync_UnreadRange_StaysUnresolved()
        {
            var evidence = new SubAgentRunEvidence();
            evidence.Record("read_file_lines", ReadResponse(1, 10));

            var result = await evidence.ExpandAsync("@src\\Program.cs:5-40", FileWithLines(40), CancellationToken.None);

            Assert.That(result.ExpandedRefCount, Is.EqualTo(0));
            Assert.That(result.UnresolvedRefCount, Is.EqualTo(1));
            Assert.That(result.UnresolvedRefs, Is.EqualTo(new[] { "@src\\Program.cs:5-40" }));
            Assert.That(result.Content, Does.Contain("@src\\Program.cs:5-40"));
        }
    }
}
