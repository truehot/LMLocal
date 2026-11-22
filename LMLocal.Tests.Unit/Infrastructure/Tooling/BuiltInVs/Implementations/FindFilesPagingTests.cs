using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// find_files pages by a cursor token ("r7:50"): the id names the cache entry, so a page call needs only the token and a
    /// dropped file_name / extension_filter / project_filter cannot send it to another result. 'showing' says which files the page holds.
    /// </summary>
    [TestFixture]
    public class FindFilesPagingTests
    {
        private const string SolutionDir = @"C:\sol";

        private SearchResultCache _cache;
        private FindFiles _tool;

        [SetUp]
        public void SetUp()
        {
            _cache = new SearchResultCache();

            var vs = new Mock<IVsDependencies>();
            vs.SetupGet(v => v.IsSolutionOpen).Returns(true);
            vs.Setup(v => v.GetSolutionDirectory()).Returns(SolutionDir);
            _tool = new FindFiles(vs.Object, Mock.Of<IVsSolutionFilesScanner>(), _cache);
        }

        private void Cache(string name, string extension, int count, string project = null)
        {
            _cache.Set(FindFiles.BuildCacheKey(name, extension, project), SolutionDir, new CachedToolResults<FindFiles.FileSearchResult>
            {
                AllResults = Enumerable.Range(1, count).Select(i => new FindFiles.FileSearchResult { FilePath = name.TrimEnd('*') + i + ".cs" }).ToList(),
                ItemsScanned = count,
                Subject = name
            });
        }

        private async Task<FindFiles.FileSearchResultsResponse> RunAsync(Dictionary<string, object> parameters)
        {
            var result = await _tool.ExecuteAsync(parameters);
            Assert.That(result, Is.TypeOf<FindFiles.FileSearchResultsResponse>());
            return (FindFiles.FileSearchResultsResponse)result;
        }

        [Test]
        public async Task FirstPage_HasACursorTokenAndTheRange()
        {
            Cache("A*", ".cs", 120);

            var page = await RunAsync(new Dictionary<string, object> { ["file_name"] = "A*", ["extension_filter"] = ".cs", ["max_results"] = 50 });

            Assert.That(page.Results.Count, Is.EqualTo(50));
            Assert.That(page.NextPageToken, Is.EqualTo(_cache.GetId(FindFiles.BuildCacheKey("A*", ".cs", null)) + ":50"));
            Assert.That(page.Showing, Is.EqualTo("page 1 of 3"));
            Assert.That(page.Warning, Is.Null);
        }

        [Test]
        public async Task NextPage_WithTheTokenOnly_ComesFromTheSameEntry()
        {
            Cache("A*", ".cs", 120);
            var first = await RunAsync(new Dictionary<string, object> { ["file_name"] = "A*", ["extension_filter"] = ".cs", ["max_results"] = 50 });

            // The model drops file_name and extension_filter on page 2.
            var second = await RunAsync(new Dictionary<string, object> { ["page_token"] = first.NextPageToken, ["max_results"] = 50 });

            Assert.That(second.Success, Is.True);
            Assert.That(second.Results.First().FilePath, Is.EqualTo("A51.cs"));
            Assert.That(second.Showing, Is.EqualTo("page 2 of 3"));
            Assert.That(second.Warning, Is.Null);
        }

        [Test]
        public async Task NextPage_WithTheSameNameButDroppedFilters_ComesFromTheTokenEntry()
        {
            Cache("A*", ".cs", 120);
            Cache("A*", null, 7); // the same name without the filter is another entry
            var first = await RunAsync(new Dictionary<string, object> { ["file_name"] = "A*", ["extension_filter"] = ".cs", ["max_results"] = 50 });

            var second = await RunAsync(new Dictionary<string, object> { ["file_name"] = "A*", ["page_token"] = first.NextPageToken, ["max_results"] = 50 });

            Assert.That(second.TotalFiles, Is.EqualTo(120));
            Assert.That(second.Results.First().FilePath, Is.EqualTo("A51.cs"));
            Assert.That(second.Warning, Is.Null);
        }

        [Test]
        public async Task OtherNameInTheCall_IgnoresTheTokenAndWarns()
        {
            Cache("A*", null, 120);
            Cache("B*", null, 10);
            string token = _cache.GetId(FindFiles.BuildCacheKey("A*", null, null)) + ":50";

            var page = await RunAsync(new Dictionary<string, object> { ["file_name"] = "B*", ["page_token"] = token });

            Assert.That(page.Warning, Is.EqualTo(PageSelection.ForeignTokenWarning));
            Assert.That(page.TotalFiles, Is.EqualTo(10));
            Assert.That(page.Results.First().FilePath, Is.EqualTo("B1.cs"));
        }

        [Test]
        public async Task PageSizeChangedBetweenCalls_LosesAndRepeatsNothing()
        {
            Cache("A*", null, 120);
            var first = await RunAsync(new Dictionary<string, object> { ["file_name"] = "A*", ["max_results"] = 50 });
            var second = await RunAsync(new Dictionary<string, object> { ["page_token"] = first.NextPageToken, ["max_results"] = 500 });

            Assert.That(first.Results.Count + second.Results.Count, Is.EqualTo(120));
            Assert.That(second.NextPageToken, Is.Null);
            Assert.That(second.Showing, Is.EqualTo("page 2 of 2"));
        }

        [Test]
        public async Task Last_WithTheTokenOnly_ReturnsTheLastPage()
        {
            Cache("A*", null, 120);
            string id = _cache.GetId(FindFiles.BuildCacheKey("A*", null, null));

            var page = await RunAsync(new Dictionary<string, object> { ["page_token"] = id + ":last", ["max_results"] = 50 });

            Assert.That(page.Results.First().FilePath, Is.EqualTo("A71.cs"));
            Assert.That(page.Results.Last().FilePath, Is.EqualTo("A120.cs"));
            Assert.That(page.NextPageToken, Is.Null);
            Assert.That(page.Showing, Is.EqualTo("page 3 of 3"));
        }

        [Test]
        public async Task Last_WithTheName_ReturnsTheLastPageWithoutWarning()
        {
            Cache("A*", null, 120);

            var page = await RunAsync(new Dictionary<string, object> { ["file_name"] = "A*", ["page_token"] = "last", ["max_results"] = 50 });

            Assert.That(page.Results.First().FilePath, Is.EqualTo("A71.cs"));
            Assert.That(page.Warning, Is.Null);
        }

        [Test]
        public async Task Last_WithoutAnyParameters_SaysToRepeatTheFirstRequest()
        {
            var page = await RunAsync(new Dictionary<string, object> { ["page_token"] = "last" });

            Assert.That(page.Success, Is.False);
            Assert.That(page.ErrorMessage, Does.Contain("repeat the first request"));
        }

        [Test]
        public async Task TokenOfAClearedCache_WithTheName_ComputesAgainWithAWarning()
        {
            Cache("A*", null, 120);
            string token = _cache.GetId(FindFiles.BuildCacheKey("A*", null, null)) + ":50";
            _cache.Clear();
            Cache("A*", null, 120); // the scanner is not available in a unit test: the same request is cached again under a new id

            var page = await RunAsync(new Dictionary<string, object> { ["file_name"] = "A*", ["page_token"] = token, ["max_results"] = 50 });

            Assert.That(page.Warning, Is.EqualTo(PageSelection.StaleTokenWarning));
            Assert.That(page.Results.First().FilePath, Is.EqualTo("A51.cs"));
        }

        [Test]
        public async Task TokenOfAClearedCache_WithoutTheName_SaysToRepeatTheFirstRequest()
        {
            Cache("A*", null, 120);
            string token = _cache.GetId(FindFiles.BuildCacheKey("A*", null, null)) + ":50";
            _cache.Clear();

            var page = await RunAsync(new Dictionary<string, object> { ["page_token"] = token });

            Assert.That(page.Success, Is.False);
            Assert.That(page.ErrorMessage, Does.Contain("repeat the first request"));
        }

        [Test]
        public async Task PlainNumber_PagesByTheParametersAndWarns()
        {
            Cache("A*", null, 120);

            var page = await RunAsync(new Dictionary<string, object> { ["file_name"] = "A*", ["page_token"] = "50", ["max_results"] = 50 });

            Assert.That(page.Results.First().FilePath, Is.EqualTo("A51.cs"));
            Assert.That(page.Warning, Does.Contain("not a next_page_token"));
        }

        [Test]
        public async Task EverythingOnOnePage_ShowsTheWholeRangeWithoutAToken()
        {
            Cache("A*", null, 3);

            var page = await RunAsync(new Dictionary<string, object> { ["file_name"] = "A*" });

            Assert.That(page.Showing, Is.EqualTo("page 1 of 1"));
            Assert.That(page.NextPageToken, Is.Null);
        }
    }
}