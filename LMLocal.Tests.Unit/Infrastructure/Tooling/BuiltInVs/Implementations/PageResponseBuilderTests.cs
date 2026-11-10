using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Covers the response builders that turn a full result list into one page:
    /// page slice + totals over the whole list + next_page_token.
    /// </summary>
    [TestFixture]
    public class PageResponseBuilderTests
    {
        private static SearchResult File(string path, params int[] lineNumbers)
        {
            var matches = new List<SearchMatch>();
            foreach (int line in lineNumbers)
                matches.Add(new SearchMatch { LineNumber = line, LineText = "line" + line });

            return new SearchResult
            {
                FilePath = path,
                Matches = matches,
                MatchCount = matches.Count
            };
        }

        private static SearchTextGroupResult TextGroup(string text, int occurrences, params string[] files)
        {
            var infos = new List<SearchTextGroupFileInfo>();
            foreach (string file in files)
                infos.Add(new SearchTextGroupFileInfo { FilePath = file, Lines = new List<int> { 1 } });

            return new SearchTextGroupResult
            {
                Text = text,
                OccurrenceCount = occurrences,
                Files = infos
            };
        }

        private static List<FindFiles.FileSearchResult> Files(params string[] paths)
        {
            return paths.Select(p => new FindFiles.FileSearchResult { FilePath = p }).ToList();
        }

        #region search_file_content, group_by = file

        [Test]
        public void BuildFilePageResponse_FirstPage_TotalsFromWholeListAndNextToken()
        {
            var all = new List<SearchResult> { File("a.cs", 1, 2), File("b.cs", 3, 4, 5) };

            var response = SearchFileContent.BuildFilePageResponse(all, 0, 2, "note");

            Assert.That(response.Success, Is.True);
            Assert.That(response.Results.Select(r => r.FilePath), Is.EqualTo(new[] { "a.cs" }));
            Assert.That(response.Results[0].Matches.Select(m => m.LineNumber), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(response.TotalMatches, Is.EqualTo(5));
            Assert.That(response.TotalFiles, Is.EqualTo(2));
            Assert.That(response.NextPageToken, Is.EqualTo("2"));
            Assert.That(response.Note, Is.EqualTo("note"));
        }

        [Test]
        public void BuildFilePageResponse_OffsetInsideFile_PageStartsAtOffsetAndLastPageHasNoToken()
        {
            var all = new List<SearchResult> { File("a.cs", 1, 2), File("b.cs", 3, 4, 5) };

            // Offset 3 skips lines 1..3, so the page is the remainder of b.cs (4,5) and nothing is left.
            var response = SearchFileContent.BuildFilePageResponse(all, 3, 10, null);

            Assert.That(response.Results.Select(r => r.FilePath), Is.EqualTo(new[] { "b.cs" }));
            Assert.That(response.Results[0].MatchCount, Is.EqualTo(2));
            Assert.That(response.TotalMatches, Is.EqualTo(5));
            Assert.That(response.NextPageToken, Is.Null);
        }

        [Test]
        public void BuildFilePageResponse_OffsetPastTheEnd_ReturnsEmptyPageWithTotalsAndNoToken()
        {
            var all = new List<SearchResult> { File("a.cs", 1, 2), File("b.cs", 3, 4, 5) };

            var response = SearchFileContent.BuildFilePageResponse(all, 10, 5, null);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Results, Is.Empty);
            Assert.That(response.NextPageToken, Is.Null);
            Assert.That(response.TotalMatches, Is.EqualTo(5));
            Assert.That(response.TotalFiles, Is.EqualTo(2));
        }

        [Test]
        public void BuildFilePageResponse_EmptyList_ReturnsEmptyPageWithoutToken()
        {
            var response = SearchFileContent.BuildFilePageResponse(new List<SearchResult>(), 0, 10, null);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Results, Is.Empty);
            Assert.That(response.NextPageToken, Is.Null);
            Assert.That(response.TotalMatches, Is.Zero);
            Assert.That(response.TotalFiles, Is.Zero);
        }

        [Test]
        public void BuildFilePageResponse_PageSizeChangeBetweenCalls_LosesAndRepeatsNothing()
        {
            var all = new List<SearchResult> { File("a.cs", 1, 2, 3), File("b.cs", 4, 5, 6), File("c.cs", 7, 8, 9) };
            int[] pageSizes = { 2, 7, 3, 10 };

            var seen = new List<int>();
            int offset = 0;
            int call = 0;

            while (true)
            {
                int pageSize = pageSizes[System.Math.Min(call, pageSizes.Length - 1)];
                var response = SearchFileContent.BuildFilePageResponse(all, offset, pageSize, null);

                foreach (var result in response.Results)
                    foreach (var match in result.Matches)
                        seen.Add(match.LineNumber);

                Assert.That(response.TotalMatches, Is.EqualTo(9));

                if (response.NextPageToken == null)
                    break;

                // The token is the offset of the first match that was not on this page.
                int delivered = response.Results.Sum(r => r.MatchCount);
                Assert.That(response.NextPageToken, Is.EqualTo((offset + delivered).ToString(CultureInfo.InvariantCulture)));

                call++;
                int nextPageSize = pageSizes[System.Math.Min(call, pageSizes.Length - 1)];
                offset = ToolPagination.ParseOffset(response.NextPageToken, nextPageSize);
            }

            Assert.That(seen, Is.EqualTo(Enumerable.Range(1, 9).ToList()));
        }

        #endregion

        #region search_file_content, group_by = text

        [Test]
        public void BuildTextPageResponse_FirstPage_TotalsFromAllGroupsAndNextToken()
        {
            var groups = new List<SearchTextGroupResult>
            {
                TextGroup("a", 3, "x.cs"),
                TextGroup("b", 2, "y.cs"),
                TextGroup("c", 1, "x.cs")
            };

            var response = SearchFileContent.BuildTextPageResponse(groups, 0, 2, "note");

            Assert.That(response.Success, Is.True);
            Assert.That(response.Results.Select(g => g.Text), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(response.TotalMatches, Is.EqualTo(6));
            Assert.That(response.TotalFiles, Is.EqualTo(2));
            Assert.That(response.NextPageToken, Is.EqualTo("2"));
            Assert.That(response.Note, Is.EqualTo("note"));
        }

        [Test]
        public void BuildTextPageResponse_LastPage_OmitsNextToken()
        {
            var groups = new List<SearchTextGroupResult>
            {
                TextGroup("a", 3, "x.cs"),
                TextGroup("b", 2, "y.cs"),
                TextGroup("c", 1, "x.cs")
            };

            var response = SearchFileContent.BuildTextPageResponse(groups, 2, 2, null);

            Assert.That(response.Results.Select(g => g.Text), Is.EqualTo(new[] { "c" }));
            Assert.That(response.NextPageToken, Is.Null);
            Assert.That(response.TotalMatches, Is.EqualTo(6));
        }

        [Test]
        public void BuildTextPageResponse_OffsetPastTheEnd_ReturnsEmptyPageWithTotalsAndNoToken()
        {
            var groups = new List<SearchTextGroupResult> { TextGroup("a", 3, "x.cs") };

            var response = SearchFileContent.BuildTextPageResponse(groups, 5, 2, null);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Results, Is.Empty);
            Assert.That(response.NextPageToken, Is.Null);
            Assert.That(response.TotalMatches, Is.EqualTo(3));
            Assert.That(response.TotalFiles, Is.EqualTo(1));
        }

        #endregion

        #region find_files

        [Test]
        public void FindFilesBuildPageResponse_FirstPage_TotalsFromWholeListAndNextToken()
        {
            var all = Files("a.cs", "b.cs", "c.cs");

            var response = FindFiles.BuildPageResponse(all, 0, 2, "note");

            Assert.That(response.Success, Is.True);
            Assert.That(response.Results.Select(r => r.FilePath), Is.EqualTo(new[] { "a.cs", "b.cs" }));
            Assert.That(response.TotalFiles, Is.EqualTo(3));
            Assert.That(response.NextPageToken, Is.EqualTo("2"));
            Assert.That(response.Note, Is.EqualTo("note"));
        }

        [Test]
        public void FindFilesBuildPageResponse_LastPage_OmitsNextToken()
        {
            var all = Files("a.cs", "b.cs", "c.cs");

            var response = FindFiles.BuildPageResponse(all, 2, 2, null);

            Assert.That(response.Results.Select(r => r.FilePath), Is.EqualTo(new[] { "c.cs" }));
            Assert.That(response.TotalFiles, Is.EqualTo(3));
            Assert.That(response.NextPageToken, Is.Null);
        }

        [Test]
        public void FindFilesBuildPageResponse_OffsetPastTheEnd_ReturnsEmptyPageWithTotalsAndNoToken()
        {
            var all = Files("a.cs", "b.cs", "c.cs");

            var response = FindFiles.BuildPageResponse(all, 5, 2, null);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Results, Is.Empty);
            Assert.That(response.NextPageToken, Is.Null);
            Assert.That(response.TotalFiles, Is.EqualTo(3));
        }

        [Test]
        public void FindFilesBuildPageResponse_EmptyList_ReturnsEmptyPageWithoutToken()
        {
            var response = FindFiles.BuildPageResponse(new List<FindFiles.FileSearchResult>(), 0, 10, null);

            Assert.That(response.Results, Is.Empty);
            Assert.That(response.NextPageToken, Is.Null);
            Assert.That(response.TotalFiles, Is.Zero);
        }

        #endregion
    }
}
