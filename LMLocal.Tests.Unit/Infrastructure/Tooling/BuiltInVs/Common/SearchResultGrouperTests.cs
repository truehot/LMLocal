using System.Collections.Generic;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common
{
    [TestFixture]
    public class SearchResultGrouperTests
    {
        private static SearchResult File(string path, params string[] lines)
        {
            var matches = new List<SearchMatch>();
            int lineNumber = 1;
            foreach (var text in lines)
            {
                matches.Add(new SearchMatch { LineNumber = lineNumber, LineText = text });
                lineNumber++;
            }

            return new SearchResult
            {
                FilePath = path,
                Matches = matches,
                MatchCount = matches.Count
            };
        }

        [Test]
        public void GroupByText_SameLineInDifferentFiles_GroupsTogether()
        {
            var results = new List<SearchResult>
            {
                File("a.cs", "var x = 1;"),
                File("b.cs", "var x = 1;")
            };

            var groups = SearchResultGrouper.GroupByText(results);

            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].Text, Is.EqualTo("var x = 1;"));
            Assert.That(groups[0].OccurrenceCount, Is.EqualTo(2));
            Assert.That(groups[0].Files, Has.Count.EqualTo(2));
        }

        [Test]
        public void GroupByText_KeepsLineNumbersPerFile_Sorted()
        {
            var results = new List<SearchResult>
            {
                File("a.cs", "dup", "other", "dup"),
                File("b.cs", "dup")
            };

            var groups = SearchResultGrouper.GroupByText(results);
            var dup = groups.Find(g => g.Text == "dup");

            Assert.That(dup.OccurrenceCount, Is.EqualTo(3));

            var a = dup.Files.Find(f => f.FilePath == "a.cs");
            var b = dup.Files.Find(f => f.FilePath == "b.cs");

            Assert.That(a.Lines, Is.EqualTo(new List<int> { 1, 3 }));
            Assert.That(b.Lines, Is.EqualTo(new List<int> { 1 }));
        }

        [Test]
        public void GroupByText_FilesWithinGroup_SortedByPath()
        {
            var results = new List<SearchResult>
            {
                File("z.cs", "shared"),
                File("a.cs", "shared"),
                File("m.cs", "shared")
            };

            var groups = SearchResultGrouper.GroupByText(results);
            var files = groups[0].Files;

            Assert.That(files[0].FilePath, Is.EqualTo("a.cs"));
            Assert.That(files[1].FilePath, Is.EqualTo("m.cs"));
            Assert.That(files[2].FilePath, Is.EqualTo("z.cs"));
        }

        [Test]
        public void GroupByText_OrdersByOccurrenceDescending_ThenTextAscending()
        {
            var results = new List<SearchResult>
            {
                File("a.cs", "rare", "common", "common"),
                File("b.cs", "common")
            };

            var groups = SearchResultGrouper.GroupByText(results);

            Assert.That(groups[0].Text, Is.EqualTo("common"));
            Assert.That(groups[0].OccurrenceCount, Is.EqualTo(3));
            Assert.That(groups[1].Text, Is.EqualTo("rare"));
        }

        [Test]
        public void GroupByText_TieBreak_ByTextOrdinal()
        {
            var results = new List<SearchResult>
            {
                File("a.cs", "bbb", "aaa")
            };

            var groups = SearchResultGrouper.GroupByText(results);

            Assert.That(groups[0].Text, Is.EqualTo("aaa"));
            Assert.That(groups[1].Text, Is.EqualTo("bbb"));
        }

        [Test]
        public void CountDistinctFiles_CountsEachFileOnce()
        {
            var groups = new List<SearchTextGroupResult>
            {
                new SearchTextGroupResult
                {
                    Text = "x",
                    OccurrenceCount = 2,
                    Files = new List<SearchTextGroupFileInfo>
                    {
                        new SearchTextGroupFileInfo { FilePath = "a.cs", Lines = new List<int> { 1 } },
                        new SearchTextGroupFileInfo { FilePath = "b.cs", Lines = new List<int> { 2 } }
                    }
                },
                new SearchTextGroupResult
                {
                    Text = "y",
                    OccurrenceCount = 1,
                    Files = new List<SearchTextGroupFileInfo>
                    {
                        new SearchTextGroupFileInfo { FilePath = "a.cs", Lines = new List<int> { 5 } }
                    }
                }
            };

            Assert.That(SearchResultGrouper.CountDistinctFiles(groups), Is.EqualTo(2));
        }

        [Test]
        public void CountOccurrences_SumsAllGroups()
        {
            var groups = new List<SearchTextGroupResult>
            {
                new SearchTextGroupResult { Text = "x", OccurrenceCount = 2, Files = new List<SearchTextGroupFileInfo>() },
                new SearchTextGroupResult { Text = "y", OccurrenceCount = 3, Files = new List<SearchTextGroupFileInfo>() }
            };

            Assert.That(SearchResultGrouper.CountOccurrences(groups), Is.EqualTo(5));
        }

        [Test]
        public void GroupByText_EmptyInput_ReturnsEmpty()
        {
            Assert.That(SearchResultGrouper.GroupByText(new List<SearchResult>()), Is.Empty);
        }
    }
}
