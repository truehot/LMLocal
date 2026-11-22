using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// search_file_content pages by a cursor token ("r7:50"): the id names the cache entry (and so its grouping), so a page call needs
    /// only the token and a dropped text / path / extension_filter / group_by cannot send it to another result. Units: a match for
    /// group_by=file, a distinct text for group_by=text.
    /// </summary>
    [TestFixture]
    public class SearchFileContentPagingTests
    {
        private const string SolutionDir = @"C:\sol";

        private SearchResultCache _cache;
        private SearchFileContent _tool;

        [SetUp]
        public void SetUp()
        {
            _cache = new SearchResultCache();

            var vs = new Mock<IVsDependencies>();
            vs.SetupGet(v => v.IsSolutionOpen).Returns(true);
            vs.Setup(v => v.GetSolutionDirectory()).Returns(SolutionDir);
            _tool = new SearchFileContent(vs.Object, Mock.Of<IPathResolver>(), Mock.Of<IVsSolutionFilesScanner>(), _cache, Mock.Of<IFileSystem>());
        }

        /// <summary>Files of 10 matches each: file i holds lines 1..10.</summary>
        private string CacheFiles(string text, int files, SearchGrouping grouping = SearchGrouping.File)
        {
            string key = SearchFileContent.BuildCacheKey(text, null, null, grouping);
            _cache.Set(key, SolutionDir, new CachedToolResults<SearchResult>
            {
                AllResults = Enumerable.Range(1, files).Select(i => new SearchResult
                {
                    FilePath = $"f{i}.cs",
                    Matches = Enumerable.Range(1, 10).Select(l => new SearchMatch { LineNumber = l, LineText = "t" }).ToList(),
                    MatchCount = 10
                }).ToList(),
                ItemsScanned = files,
                Subject = text
            });
            return key;
        }

        private string CacheGroups(string text, int groups)
        {
            string key = SearchFileContent.BuildCacheKey(text, null, null, SearchGrouping.Text);
            _cache.Set(key, SolutionDir, new CachedToolResults<SearchTextGroupResult>
            {
                AllResults = Enumerable.Range(1, groups).Select(i => new SearchTextGroupResult
                {
                    Text = "line " + i,
                    OccurrenceCount = 2,
                    Files = new List<SearchTextGroupFileInfo> { new SearchTextGroupFileInfo { FilePath = "f.cs", Lines = new List<int> { i } } }
                }).ToList(),
                ItemsScanned = 1,
                Subject = text
            });
            return key;
        }

        private async Task<SearchResultsResponse> RunFileAsync(Dictionary<string, object> parameters)
        {
            var result = await _tool.ExecuteAsync(parameters);
            Assert.That(result, Is.TypeOf<SearchResultsResponse>());
            return (SearchResultsResponse)result;
        }

        private async Task<SearchTextGroupResponse> RunTextAsync(Dictionary<string, object> parameters)
        {
            var result = await _tool.ExecuteAsync(parameters);
            Assert.That(result, Is.TypeOf<SearchTextGroupResponse>());
            return (SearchTextGroupResponse)result;
        }

        [Test]
        public async Task ByFile_FirstPage_HasACursorTokenAndTheRangeInMatches()
        {
            string key = CacheFiles("Alpha", 12); // 120 matches

            var page = await RunFileAsync(new Dictionary<string, object> { ["text"] = "Alpha", ["max_results"] = 50 });

            Assert.That(page.Results.Sum(r => r.MatchCount), Is.EqualTo(50));
            Assert.That(page.NextPageToken, Is.EqualTo(_cache.GetId(key) + ":50"));
            Assert.That(page.Showing, Is.EqualTo("page 1 of 3"));
            Assert.That(page.Warning, Is.Null);
        }

        [Test]
        public async Task ByFile_NextPage_WithTheTokenOnly_ComesFromTheSameEntry()
        {
            CacheFiles("Alpha", 12);
            var first = await RunFileAsync(new Dictionary<string, object> { ["text"] = "Alpha", ["max_results"] = 50 });

            // The model drops text on page 2.
            var second = await RunFileAsync(new Dictionary<string, object> { ["page_token"] = first.NextPageToken, ["max_results"] = 50 });

            Assert.That(second.Results.First().FilePath, Is.EqualTo("f6.cs"));
            Assert.That(second.Results.First().Matches.First().LineNumber, Is.EqualTo(1));
            Assert.That(second.Showing, Is.EqualTo("page 2 of 3"));
            Assert.That(second.Warning, Is.Null);
        }

        [Test]
        public async Task ByFile_NextPage_WithTheSameTextButAnotherPath_StillComesFromTheTokenEntry()
        {
            CacheFiles("Alpha", 12);
            var first = await RunFileAsync(new Dictionary<string, object> { ["text"] = "Alpha", ["max_results"] = 50 });

            var second = await RunFileAsync(new Dictionary<string, object> { ["text"] = "Alpha", ["path"] = "Other", ["page_token"] = first.NextPageToken, ["max_results"] = 50 });

            Assert.That(second.TotalMatches, Is.EqualTo(120));
            Assert.That(second.Results.First().FilePath, Is.EqualTo("f6.cs"));
            Assert.That(second.Warning, Is.Null);
        }

        [Test]
        public async Task ByText_TokenOnly_KeepsTheGroupingOfTheEntry()
        {
            string key = CacheGroups("Alpha", 80);
            var first = await RunTextAsync(new Dictionary<string, object> { ["text"] = "Alpha", ["group_by"] = "text", ["max_results"] = 25 });

            Assert.That(first.NextPageToken, Is.EqualTo(_cache.GetId(key) + ":25"));
            Assert.That(first.Showing, Is.EqualTo("page 1 of 4"));

            // group_by is dropped as well: the entry knows its grouping.
            var second = await RunTextAsync(new Dictionary<string, object> { ["page_token"] = first.NextPageToken, ["max_results"] = 25 });

            Assert.That(second.Results.First().Text, Is.EqualTo("line 26"));
            Assert.That(second.Showing, Is.EqualTo("page 2 of 4"));
        }

        [Test]
        public async Task OtherTextInTheCall_IgnoresTheTokenAndWarns()
        {
            CacheFiles("Alpha", 12);
            CacheFiles("Beta", 1);
            string token = _cache.GetId(SearchFileContent.BuildCacheKey("Alpha", null, null, SearchGrouping.File)) + ":50";

            var page = await RunFileAsync(new Dictionary<string, object> { ["text"] = "Beta", ["page_token"] = token });

            Assert.That(page.Warning, Is.EqualTo(PageSelection.ForeignTokenWarning));
            Assert.That(page.TotalMatches, Is.EqualTo(10));
            Assert.That(page.Showing, Is.EqualTo("page 1 of 1"));
        }

        [Test]
        public async Task ByFile_PageSizeChangedBetweenCalls_LosesAndRepeatsNothing()
        {
            CacheFiles("Alpha", 12);
            var first = await RunFileAsync(new Dictionary<string, object> { ["text"] = "Alpha", ["max_results"] = 25 });
            var second = await RunFileAsync(new Dictionary<string, object> { ["page_token"] = first.NextPageToken, ["max_results"] = 500 });

            int firstMatches = first.Results.Sum(r => r.MatchCount);
            int secondMatches = second.Results.Sum(r => r.MatchCount);
            Assert.That(firstMatches + secondMatches, Is.EqualTo(120));
            Assert.That(second.NextPageToken, Is.Null);
        }

        [Test]
        public async Task ByFile_Last_WithTheTokenOnly_ReturnsTheLastMatches()
        {
            string key = CacheFiles("Alpha", 12);

            var page = await RunFileAsync(new Dictionary<string, object> { ["page_token"] = _cache.GetId(key) + ":last", ["max_results"] = 50 });

            Assert.That(page.Results.Sum(r => r.MatchCount), Is.EqualTo(50));
            Assert.That(page.Results.Last().FilePath, Is.EqualTo("f12.cs"));
            Assert.That(page.NextPageToken, Is.Null);
            Assert.That(page.Showing, Is.EqualTo("page 3 of 3"));
        }

        [Test]
        public async Task ByText_Last_WithTheTextAndGrouping_ReturnsTheLastGroups()
        {
            CacheGroups("Alpha", 80);

            var page = await RunTextAsync(new Dictionary<string, object> { ["text"] = "Alpha", ["group_by"] = "text", ["page_token"] = "last", ["max_results"] = 25 });

            Assert.That(page.Results.First().Text, Is.EqualTo("line 56"));
            Assert.That(page.Results.Last().Text, Is.EqualTo("line 80"));
            Assert.That(page.Showing, Is.EqualTo("page 4 of 4"));
            Assert.That(page.Warning, Is.Null);
        }

        [Test]
        public async Task Last_WithoutAnyParameters_SaysToRepeatTheFirstRequest()
        {
            var page = await RunFileAsync(new Dictionary<string, object> { ["page_token"] = "last" });

            Assert.That(page.Success, Is.False);
            Assert.That(page.ErrorMessage, Does.Contain("repeat the first request"));
        }

        [Test]
        public async Task TokenOfAClearedCache_WithoutText_SaysToRepeatTheFirstRequest()
        {
            string key = CacheFiles("Alpha", 12);
            string token = _cache.GetId(key) + ":50";
            _cache.Clear();

            var page = await RunFileAsync(new Dictionary<string, object> { ["page_token"] = token });

            Assert.That(page.Success, Is.False);
            Assert.That(page.ErrorMessage, Does.Contain("repeat the first request"));
        }

        [Test]
        public async Task PlainNumber_PagesByTheParametersAndWarns()
        {
            CacheFiles("Alpha", 12);

            var page = await RunFileAsync(new Dictionary<string, object> { ["text"] = "Alpha", ["page_token"] = "50", ["max_results"] = 50 });

            Assert.That(page.Results.First().FilePath, Is.EqualTo("f6.cs"));
            Assert.That(page.Warning, Does.Contain("not a next_page_token"));
        }
    }
}