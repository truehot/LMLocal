using System.Collections.Generic;
using System.Linq;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common
{
    [TestFixture]
    public class SearchResultPaginatorTests
    {
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

        #region SliceByMatches / SliceByGroups (offset addressing)

        /// <summary>Builds files whose match line numbers are unique and increasing across the whole result.</summary>
        private static List<SearchResult> NumberedFiles(params int[] matchesPerFile)
        {
            var results = new List<SearchResult>();
            int line = 1;

            for (int f = 0; f < matchesPerFile.Length; f++)
            {
                var matches = new List<SearchMatch>();
                for (int i = 0; i < matchesPerFile[f]; i++)
                {
                    matches.Add(new SearchMatch { LineNumber = line, LineText = "line" + line });
                    line++;
                }

                results.Add(new SearchResult
                {
                    FilePath = "f" + f + ".cs",
                    Matches = matches,
                    MatchCount = matches.Count
                });
            }

            return results;
        }

        private static List<int> Flatten(IEnumerable<SearchResult> results)
        {
            return results.SelectMany(r => r.Matches).Select(m => m.LineNumber).ToList();
        }

        [Test]
        public void SliceByMatches_OffsetOnFileBoundary_StartsAtThatFile()
        {
            var all = NumberedFiles(3, 3, 3);

            var page = SearchResultPaginator.SliceByMatches(all, 3, 3);

            Assert.That(page.Select(r => r.FilePath), Is.EqualTo(new[] { "f1.cs" }));
            Assert.That(page[0].MatchCount, Is.EqualTo(3));
            Assert.That(Flatten(page), Is.EqualTo(new[] { 4, 5, 6 }));
        }

        [Test]
        public void SliceByMatches_OffsetInsideFile_RemainderOfFileComesFirst()
        {
            // f0.cs: matches 1..2, f1.cs: matches 3..7, f2.cs: matches 8..9.
            var all = NumberedFiles(2, 5, 2);

            // Offset 3 skips matches 1..3, so the page starts with the remainder of f1.cs (4,5,6),
            // not with f2.cs — and it never exceeds the requested page size.
            var page = SearchResultPaginator.SliceByMatches(all, 3, 3);

            Assert.That(page.Select(r => r.FilePath), Is.EqualTo(new[] { "f1.cs" }));
            Assert.That(page[0].MatchCount, Is.EqualTo(3));
            Assert.That(Flatten(page), Is.EqualTo(new[] { 4, 5, 6 }));

            // The next page continues with the last match of f1.cs, then f2.cs: nothing lost or repeated.
            var next = SearchResultPaginator.SliceByMatches(all, 6, 10);
            Assert.That(Flatten(next), Is.EqualTo(new[] { 7, 8, 9 }));
        }

        [Test]
        public void SliceByMatches_WholeFile_ReturnsOriginalEntry()
        {
            var all = NumberedFiles(2, 2);

            var page = SearchResultPaginator.SliceByMatches(all, 0, 2);

            Assert.That(page, Has.Count.EqualTo(1));
            Assert.That(page[0], Is.SameAs(all[0]));
        }

        [Test]
        public void SliceByMatches_OffsetPastTheEnd_ReturnsEmptyPage()
        {
            var all = NumberedFiles(2, 2);

            Assert.That(SearchResultPaginator.SliceByMatches(all, 4, 10), Is.Empty);
            Assert.That(SearchResultPaginator.SliceByMatches(all, int.MaxValue, 10), Is.Empty);
        }

        [Test]
        public void SliceByMatches_PageSizeChangeBetweenCalls_LosesAndRepeatsNothing()
        {
            // Benchmark A3.7: 79 references; the second call asked for 200 after a page of 50.
            var all = NumberedFiles(30, 30, 19);
            int total = all.Sum(r => r.MatchCount);

            var firstPage = SearchResultPaginator.SliceByMatches(all, 0, 50);
            string token = ToolPagination.NextToken(firstPage.Sum(r => r.MatchCount), total);
            Assert.That(token, Is.EqualTo("50"));

            var secondPage = SearchResultPaginator.SliceByMatches(all, ToolPagination.ParseOffset(token, 200), 200);

            var seen = Flatten(firstPage).Concat(Flatten(secondPage)).ToList();
            Assert.That(seen, Has.Count.EqualTo(79));
            Assert.That(seen.Distinct().Count(), Is.EqualTo(79));
            Assert.That(seen, Is.EqualTo(Enumerable.Range(1, 79).ToList()));
            Assert.That(ToolPagination.NextToken(50 + secondPage.Sum(r => r.MatchCount), total), Is.Null);
        }

        [Test]
        public void SliceByGroups_OffsetAndSize_ReturnsRequestedWindow()
        {
            var groups = new List<SearchTextGroupResult>
            {
                Group("a", 1), Group("b", 1), Group("c", 1), Group("d", 1), Group("e", 1)
            };

            var page = SearchResultPaginator.SliceByGroups(groups, 1, 2);

            Assert.That(page.Select(g => g.Text), Is.EqualTo(new[] { "b", "c" }));
        }

        [Test]
        public void SliceByGroups_OffsetPastTheEnd_ReturnsEmptyPage()
        {
            var groups = new List<SearchTextGroupResult> { Group("a", 1) };

            Assert.That(SearchResultPaginator.SliceByGroups(groups, 1, 10), Is.Empty);
            Assert.That(SearchResultPaginator.SliceByGroups(groups, int.MaxValue, 10), Is.Empty);
        }

        #endregion
    }
}
