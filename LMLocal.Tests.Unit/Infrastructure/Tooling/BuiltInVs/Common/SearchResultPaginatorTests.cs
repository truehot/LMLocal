using System.Collections.Generic;
using System.Linq;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common
{
    [TestFixture]
    public class SearchResultPaginatorTests
    {
        private static SearchResult File(string path, int matchCount)
        {
            var matches = new List<SearchMatch>();
            for (int i = 1; i <= matchCount; i++)
                matches.Add(new SearchMatch { LineNumber = i, LineText = "line" + i });

            return new SearchResult
            {
                FilePath = path,
                Matches = matches,
                MatchCount = matchCount
            };
        }

        private static SearchTextGroupResult Group(string text, int occurrences)
        {
            return new SearchTextGroupResult
            {
                Text = text,
                OccurrenceCount = occurrences,
                Files = new List<SearchTextGroupFileInfo>
                {
                    new SearchTextGroupFileInfo { FilePath = "f.cs", Lines = new List<int> { 1 } }
                }
            };
        }

        [Test]
        public void PaginateByGroups_SplitsIntoPagesOfGivenSize()
        {
            var groups = new List<SearchTextGroupResult>
            {
                Group("a", 1), Group("b", 1), Group("c", 1), Group("d", 1), Group("e", 1)
            };

            var pages = SearchResultPaginator.PaginateByGroups(groups, 2);

            Assert.That(pages, Has.Count.EqualTo(3));
            Assert.That(pages[0].Results.Select(g => g.Text), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(pages[1].Results.Select(g => g.Text), Is.EqualTo(new[] { "c", "d" }));
            Assert.That(pages[2].Results.Select(g => g.Text), Is.EqualTo(new[] { "e" }));
        }

        [Test]
        public void PaginateByGroups_SetsTotalsFromAllGroups()
        {
            var groups = new List<SearchTextGroupResult>
            {
                Group("a", 2), Group("b", 3), Group("c", 1)
            };

            var pages = SearchResultPaginator.PaginateByGroups(groups, 2);

            Assert.That(pages[0].TotalMatches, Is.EqualTo(6));
            // All groups reference the same file "f.cs".
            Assert.That(pages[0].TotalFiles, Is.EqualTo(1));
        }

        [Test]
        public void PaginateByGroups_EmptyInput_ReturnsEmpty()
        {
            Assert.That(SearchResultPaginator.PaginateByGroups(new List<SearchTextGroupResult>(), 10), Is.Empty);
        }

        [Test]
        public void PaginateByMatches_SplitsByMatchCount()
        {
            var results = new List<SearchResult>
            {
                File("a.cs", 2),
                File("b.cs", 2),
                File("c.cs", 2)
            };

            var pages = SearchResultPaginator.PaginateByMatches(results, 4);

            Assert.That(pages, Has.Count.EqualTo(2));
            Assert.That(pages[0].Results.Sum(r => r.MatchCount), Is.EqualTo(4));
            Assert.That(pages[1].Results.Sum(r => r.MatchCount), Is.EqualTo(2));
        }

        [Test]
        public void PaginateByMatches_SplitsOversizedFileAcrossPages()
        {
            var results = new List<SearchResult>
            {
                File("big.cs", 5)
            };

            var pages = SearchResultPaginator.PaginateByMatches(results, 2);

            Assert.That(pages, Has.Count.EqualTo(3));
            Assert.That(pages[0].Results[0].Matches, Has.Count.EqualTo(2));
            Assert.That(pages[1].Results[0].Matches, Has.Count.EqualTo(2));
            Assert.That(pages[2].Results[0].Matches, Has.Count.EqualTo(1));
            Assert.That(pages[0].Results[0].FilePath, Is.EqualTo("big.cs"));
        }

        [Test]
        public void PaginateByMatches_SetsTotals()
        {
            var results = new List<SearchResult>
            {
                File("a.cs", 2),
                File("b.cs", 2)
            };

            var pages = SearchResultPaginator.PaginateByMatches(results, 2);

            Assert.That(pages[0].TotalMatches, Is.EqualTo(4));
            Assert.That(pages[0].TotalFiles, Is.EqualTo(2));
        }
        [Test]
        public void PaginateByMatches_NonPositivePageSize_TreatsAsOneMatchPerPage()
        {
            var results = new List<SearchResult>
            {
                File("a.cs", 2),
                File("b.cs", 2)
            };

            var pages = SearchResultPaginator.PaginateByMatches(results, 0);

            Assert.That(pages, Has.Count.EqualTo(4));
            Assert.That(pages.Sum(p => p.Results.Sum(r => r.MatchCount)), Is.EqualTo(4));
        }

        [Test]
        public void PaginateByGroups_NonPositivePageSize_TreatsAsOneGroupPerPage()
        {
            var groups = new List<SearchTextGroupResult>
            {
                Group("a", 1),
                Group("b", 1),
                Group("c", 1)
            };

            var pages = SearchResultPaginator.PaginateByGroups(groups, 0);

            Assert.That(pages, Has.Count.EqualTo(3));
            Assert.That(pages.Sum(p => p.Results.Count), Is.EqualTo(3));
        }
    }
}
