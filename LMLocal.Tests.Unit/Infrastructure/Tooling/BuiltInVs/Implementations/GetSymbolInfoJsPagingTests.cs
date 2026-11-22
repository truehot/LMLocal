using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Js;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// get_symbol_info_js pages by a cursor token ("r7:50") like get_symbol_info: the id names the cache entry that holds the whole
    /// result, so a page call needs only the token. Only the cached paths are tested: a lookup that scans the solution needs Visual Studio.
    /// </summary>
    [TestFixture]
    public class GetSymbolInfoJsPagingTests
    {
        private const string Symbol = "renderChat";

        private SearchResultCache _cache;
        private GetSymbolInfoJs _tool;
        private string _id;

        [SetUp]
        public void SetUp()
        {
            _cache = new SearchResultCache();

            var vs = new Mock<IVsDependencies>();
            vs.SetupGet(v => v.IsSolutionOpen).Returns(true);
            _tool = new GetSymbolInfoJs(vs.Object, Mock.Of<IPathResolver>(), Mock.Of<IVsSolutionFilesScanner>(), _cache, Mock.Of<IFileSystem>(), Mock.Of<IJsConfigResolver>());

            string key = "js:" + Symbol;
            _cache.Set(key, @"C:\sol", new CachedToolResults<GetSymbolInfoJs.JsSymbolInfoResponse>
            {
                AllResults = new List<GetSymbolInfoJs.JsSymbolInfoResponse>
                {
                    new GetSymbolInfoJs.JsSymbolInfoResponse
                    {
                        Success = true,
                        SymbolName = Symbol,
                        Definitions = new List<GetSymbolInfoJs.JsDefinitionItem> { new GetSymbolInfoJs.JsDefinitionItem { FilePath = "app.js", Line = 3 } },
                        References = Enumerable.Range(1, 120)
                            .Select(i => new GetSymbolInfoJs.JsReferenceItem { FilePath = "use.js", LineNumber = i, LineText = "x" })
                            .ToList()
                    }
                },
                ItemsScanned = 1,
                Subject = Symbol
            });
            _id = _cache.GetId(key);
        }

        private async Task<GetSymbolInfoJs.JsSymbolInfoResponse> RunAsync(Dictionary<string, object> parameters)
        {
            var result = await _tool.ExecuteAsync(parameters);
            Assert.That(result, Is.TypeOf<GetSymbolInfoJs.JsSymbolInfoResponse>());
            return (GetSymbolInfoJs.JsSymbolInfoResponse)result;
        }

        [Test]
        public async Task NextPage_WithTheTokenOnly_ComesFromTheEntry()
        {
            var page = await RunAsync(new Dictionary<string, object> { ["page_token"] = _id + ":50", ["max_references"] = 50 });

            Assert.That(page.Success, Is.True);
            Assert.That(page.SymbolName, Is.EqualTo(Symbol));
            Assert.That(page.References.Select(r => r.LineNumber), Is.EqualTo(Enumerable.Range(51, 50)));
            Assert.That(page.TotalReferences, Is.EqualTo(120));
            Assert.That(page.NextPageToken, Is.EqualTo(_id + ":100"));
            Assert.That(page.Showing, Is.EqualTo("page 2 of 3"));
            Assert.That(page.Warning, Is.Null);
        }

        [Test]
        public async Task SameSymbolNameInAnotherCase_StillComesFromTheEntry()
        {
            // The tool matches symbol names case-insensitively.
            var page = await RunAsync(new Dictionary<string, object> { ["symbol_name"] = "RENDERCHAT", ["page_token"] = _id + ":50", ["max_references"] = 50 });

            Assert.That(page.References.First().LineNumber, Is.EqualTo(51));
            Assert.That(page.Warning, Is.Null);
        }

        [Test]
        public async Task LastPage_HasNoNextToken()
        {
            var page = await RunAsync(new Dictionary<string, object> { ["page_token"] = _id + ":100", ["max_references"] = 50 });

            Assert.That(page.References.Count, Is.EqualTo(20));
            Assert.That(page.NextPageToken, Is.Null);
            Assert.That(page.Showing, Is.EqualTo("page 3 of 3"));
        }

        [Test]
        public async Task Last_WithTheTokenOnly_ReturnsTheLastPage()
        {
            var page = await RunAsync(new Dictionary<string, object> { ["page_token"] = _id + ":last", ["max_references"] = 50 });

            Assert.That(page.References.Select(r => r.LineNumber), Is.EqualTo(Enumerable.Range(71, 50)));
            Assert.That(page.Showing, Is.EqualTo("page 3 of 3"));
        }

        [Test]
        public async Task TokenOfAClearedCache_WithoutSymbolName_SaysToRepeatTheFirstRequest()
        {
            string token = _id + ":50";
            _cache.Clear();

            var page = await RunAsync(new Dictionary<string, object> { ["page_token"] = token });

            Assert.That(page.Success, Is.False);
            Assert.That(page.ErrorMessage, Does.Contain("repeat the first request"));
        }

        [Test]
        public async Task IdWithoutOffset_IsAnError()
        {
            var page = await RunAsync(new Dictionary<string, object> { ["page_token"] = _id });

            Assert.That(page.Success, Is.False);
            Assert.That(page.ErrorMessage, Does.Contain("incomplete"));
        }

        [Test]
        public void SymbolNameIsNotRequiredBySchema_SoThatATokenAloneIsAValidCall()
        {
            Assert.That(_tool.GetToolInfo().Parameters.Required, Is.Empty);
        }
    }
}