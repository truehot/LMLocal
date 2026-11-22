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
    /// get_symbol_info pages by a cursor token ("r7:50"): the id names the cache entry, so a page call needs only the token and
    /// cannot be sent to another symbol by a dropped file_path (A1.29). Lookups that reach Roslyn need Visual Studio, so the
    /// tests seed the cache.
    /// </summary>
    [TestFixture]
    public class GetSymbolInfoPagingTests
    {
        private const string Symbol = "ToolCalls";
        private const string File = @"LMLocal\Core\Models\ChatMessage.cs";

        private SearchResultCache _cache;
        private GetSymbolInfo _tool;
        private string _key;
        private string _id;

        [SetUp]
        public void SetUp()
        {
            _cache = new SearchResultCache();
            _tool = new GetSymbolInfo(Mock.Of<IPathResolver>(), _cache, Mock.Of<IFileSystem>());
            _key = GetSymbolInfo.BuildCacheKey(Symbol, File, true);

            _cache.Set(_key, "", new CachedToolResults<GetSymbolInfo.SymbolInfo>
            {
                AllResults = new List<GetSymbolInfo.SymbolInfo>
                {
                    new GetSymbolInfo.SymbolInfo
                    {
                        SymbolName = Symbol,
                        Definitions = new List<GetSymbolInfo.DefinitionItem>
                        {
                            new GetSymbolInfo.DefinitionItem { FilePath = File, Line = 25, EndLine = 25, SymbolKind = "Property" }
                        },
                        References = Enumerable.Range(1, 120)
                            .Select(i => new GetSymbolInfo.ReferenceItem { FilePath = "Use.cs", LineNumber = i, LineText = "x" })
                            .ToList()
                    }
                },
                ItemsScanned = 1
            });
            _id = _cache.GetId(_key);
        }

        private async Task<GetSymbolInfo.SymbolInfoResponse> RunAsync(Dictionary<string, object> parameters)
        {
            var result = await _tool.ExecuteAsync(parameters);
            Assert.That(result, Is.TypeOf<GetSymbolInfo.SymbolInfoResponse>());
            return (GetSymbolInfo.SymbolInfoResponse)result;
        }

        [Test]
        public async Task FirstPage_ReturnsACursorTokenOfItsOwnEntryAndTheRange()
        {
            var page = await RunAsync(new Dictionary<string, object> { ["symbol_name"] = Symbol, ["file_path"] = File, ["max_references"] = 50 });

            Assert.That(page.Success, Is.True);
            Assert.That(page.References.Count, Is.EqualTo(50));
            Assert.That(page.TotalReferences, Is.EqualTo(120));
            Assert.That(page.NextPageToken, Is.EqualTo(_id + ":50"));
            Assert.That(page.Showing, Is.EqualTo("page 1 of 3"));
            Assert.That(page.Warning, Is.Null);
        }

        [Test]
        public async Task NextPage_WithTheTokenOnly_ComesFromTheSameEntry()
        {
            var first = await RunAsync(new Dictionary<string, object> { ["symbol_name"] = Symbol, ["file_path"] = File, ["max_references"] = 50 });

            // The model drops symbol_name and file_path on page 2.
            var second = await RunAsync(new Dictionary<string, object> { ["page_token"] = first.NextPageToken, ["max_references"] = 50 });

            Assert.That(second.Success, Is.True);
            Assert.That(second.SymbolName, Is.EqualTo(Symbol));
            Assert.That(second.References.Select(r => r.LineNumber), Is.EqualTo(Enumerable.Range(51, 50)));
            Assert.That(second.Definitions.Single().FilePath, Is.EqualTo(File));
            Assert.That(second.NextPageToken, Is.EqualTo(_id + ":100"));
            Assert.That(second.Showing, Is.EqualTo("page 2 of 3"));
            Assert.That(second.Warning, Is.Null);
        }

        [Test]
        public async Task NextPage_WithTheSameSymbolNameButNoFilePath_StillComesFromTheTokenEntry()
        {
            var page = await RunAsync(new Dictionary<string, object> { ["symbol_name"] = Symbol, ["page_token"] = _id + ":50", ["max_references"] = 50 });

            Assert.That(page.References.First().LineNumber, Is.EqualTo(51));
            Assert.That(page.Definitions.Single().FilePath, Is.EqualTo(File));
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
        public async Task PageSizeChangedBetweenCalls_LosesAndRepeatsNothing()
        {
            var first = await RunAsync(new Dictionary<string, object> { ["symbol_name"] = Symbol, ["file_path"] = File, ["max_references"] = 50 });
            var second = await RunAsync(new Dictionary<string, object> { ["page_token"] = first.NextPageToken, ["max_references"] = 200 });

            var all = first.References.Concat(second.References).Select(r => r.LineNumber).ToList();
            Assert.That(all, Is.EqualTo(Enumerable.Range(1, 120)));
            Assert.That(second.NextPageToken, Is.Null);
        }

        [Test]
        public async Task OtherSymbolNameInTheCall_IgnoresTheTokenAndWarns()
        {
            // The token belongs to ToolCalls, the call asks for another symbol (cached, so that no Visual Studio is needed):
            // nothing of the token entry may be returned for it, and the page starts at the beginning.
            _cache.Set(GetSymbolInfo.BuildCacheKey("SomethingElse", null, true), "", new CachedToolResults<GetSymbolInfo.SymbolInfo>
            {
                AllResults = new List<GetSymbolInfo.SymbolInfo>
                {
                    new GetSymbolInfo.SymbolInfo
                    {
                        SymbolName = "SomethingElse",
                        References = Enumerable.Range(1, 10).Select(i => new GetSymbolInfo.ReferenceItem { FilePath = "Other.cs", LineNumber = i, LineText = "y" }).ToList()
                    }
                },
                ItemsScanned = 1
            });

            var page = await RunAsync(new Dictionary<string, object>
            {
                ["symbol_name"] = "SomethingElse",
                ["page_token"] = _id + ":50",
                ["max_references"] = 50
            });

            Assert.That(page.Warning, Is.EqualTo(PageSelection.ForeignTokenWarning));
            Assert.That(page.SymbolName, Is.EqualTo("SomethingElse"));
            Assert.That(page.References.Select(r => r.LineNumber), Is.EqualTo(Enumerable.Range(1, 10)));
        }

        [Test]
        public async Task PlainNumberToken_PagesByTheCallParametersAndWarns()
        {
            var page = await RunAsync(new Dictionary<string, object>
            {
                ["symbol_name"] = Symbol,
                ["file_path"] = File,
                ["page_token"] = "50",
                ["max_references"] = 50
            });

            Assert.That(page.References.First().LineNumber, Is.EqualTo(51));
            Assert.That(page.Warning, Does.Contain("not a next_page_token"));
        }

        [Test]
        public async Task PlainNumberToken_WithoutSymbolName_SaysToRepeatTheFirstRequest()
        {
            var page = await RunAsync(new Dictionary<string, object> { ["page_token"] = "50" });

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
        public async Task TokenOfAClearedCache_WithoutSymbolName_SaysToRepeatTheFirstRequest()
        {
            string token = _id + ":50";
            _cache.Clear(); // a file was edited

            var page = await RunAsync(new Dictionary<string, object> { ["page_token"] = token });

            Assert.That(page.Success, Is.False);
            Assert.That(page.ErrorMessage, Does.Contain("page_token cannot be used"));
            Assert.That(page.ErrorMessage, Does.Contain("without page_token"));
        }

        [Test]
        public async Task TokenOfAClearedCache_IsNotMetByTheEntryThatTheSameRequestGetsAfterwards()
        {
            string token = _id + ":50";
            _cache.Clear();
            _cache.Set(_key, "", new CachedToolResults<GetSymbolInfo.SymbolInfo>
            {
                AllResults = new List<GetSymbolInfo.SymbolInfo> { new GetSymbolInfo.SymbolInfo { SymbolName = Symbol } },
                ItemsScanned = 1
            });

            // The same key got a new entry with a new id: the old token must not read it.
            Assert.That(_cache.GetId(_key), Is.Not.EqualTo(_id));

            var page = await RunAsync(new Dictionary<string, object> { ["page_token"] = token });

            Assert.That(page.Success, Is.False);
        }

        [Test]
        public async Task UnknownTokenId_WithTheParameters_PagesByTheParametersAndWarns()
        {
            var page = await RunAsync(new Dictionary<string, object>
            {
                ["symbol_name"] = Symbol,
                ["file_path"] = File,
                ["page_token"] = "r99999:50",
                ["max_references"] = 50
            });

            Assert.That(page.Success, Is.True);
            Assert.That(page.References.First().LineNumber, Is.EqualTo(51));
            Assert.That(page.Warning, Is.EqualTo(PageSelection.StaleTokenWarning));
        }

        [Test]
        public async Task Last_WithTheTokenOnly_ReturnsTheLastPageOfTheEntry()
        {
            var page = await RunAsync(new Dictionary<string, object> { ["page_token"] = _id + ":last", ["max_references"] = 50 });

            Assert.That(page.References.Select(r => r.LineNumber), Is.EqualTo(Enumerable.Range(71, 50)));
            Assert.That(page.NextPageToken, Is.Null);
            Assert.That(page.Showing, Is.EqualTo("page 3 of 3"));
            Assert.That(page.Warning, Is.Null);
        }

        [Test]
        public async Task Last_WithTheParameters_ReturnsTheLastPageWithoutWarning()
        {
            var page = await RunAsync(new Dictionary<string, object> { ["symbol_name"] = Symbol, ["file_path"] = File, ["page_token"] = "last", ["max_references"] = 50 });

            Assert.That(page.References.Select(r => r.LineNumber), Is.EqualTo(Enumerable.Range(71, 50)));
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
        public async Task Last_WithAPageSizeLargerThanTheResult_ReturnsEverything()
        {
            var page = await RunAsync(new Dictionary<string, object> { ["page_token"] = _id + ":last", ["max_references"] = 200 });

            Assert.That(page.References.Count, Is.EqualTo(120));
            Assert.That(page.Showing, Is.EqualTo("page 1 of 1"));
        }

        [Test]
        public async Task First_WithTheToken_ReturnsTheFirstPage()
        {
            var page = await RunAsync(new Dictionary<string, object> { ["page_token"] = _id + ":first", ["max_references"] = 50 });

            Assert.That(page.References.First().LineNumber, Is.EqualTo(1));
            Assert.That(page.NextPageToken, Is.EqualTo(_id + ":50"));
        }

        [Test]
        public async Task TokenCopiedWithQuotesAndUpperCase_IsStillRead()
        {
            var page = await RunAsync(new Dictionary<string, object> { ["page_token"] = "\"" + _id.ToUpperInvariant() + ":50\"", ["max_references"] = 50 });

            Assert.That(page.References.First().LineNumber, Is.EqualTo(51));
            Assert.That(page.Warning, Is.Null);
        }

        // ---- Several symbols with one name, and a project or folder in file_path ----

        [Test]
        public async Task Note_OfTheEntry_IsReturnedWithEveryPage()
        {
            string key = GetSymbolInfo.BuildCacheKey("Name", null, true);
            _cache.Set(key, "", new CachedToolResults<GetSymbolInfo.SymbolInfo>
            {
                AllResults = new List<GetSymbolInfo.SymbolInfo>
                {
                    new GetSymbolInfo.SymbolInfo
                    {
                        SymbolName = "Name",
                        Note = "2 different symbols named 'Name' were found",
                        References = Enumerable.Range(1, 60).Select(i => new GetSymbolInfo.ReferenceItem { FilePath = "Use.cs", LineNumber = i, LineText = "x" }).ToList()
                    }
                },
                ItemsScanned = 1
            });

            var first = await RunAsync(new Dictionary<string, object> { ["symbol_name"] = "Name", ["max_references"] = 50 });
            var second = await RunAsync(new Dictionary<string, object> { ["page_token"] = first.NextPageToken, ["max_references"] = 50 });

            Assert.That(first.Note, Does.Contain("2 different symbols"));
            Assert.That(second.Note, Does.Contain("2 different symbols"));
        }

        [Test]
        public void ReferenceTargetNote_NamesTheTargetAndHowToChooseAnother()
        {
            string note = GetSymbolInfo.BuildReferenceTargetNote("Name", 3, "global::Src.Models.Foo.Name", @"Src\Models\Foo.cs", 12);

            Assert.That(note, Does.Contain("3 different symbols named 'Name'"));
            Assert.That(note, Does.Contain(@"global::Src.Models.Foo.Name declared at Src\Models\Foo.cs:12"));
            Assert.That(note, Does.Contain("file_path"));
        }

        [Test]
        public void ReferenceTargetNote_OneSymbol_IsNull()
        {
            Assert.That(GetSymbolInfo.BuildReferenceTargetNote("Name", 1, "global::Src.Models.Foo.Name", @"Src\Models\Foo.cs", 12), Is.Null);
        }

        [TestCase(true, "is a project")]
        [TestCase(false, "is a folder")]
        public void ScopeNote_SaysTheDefinitionsAreFromThatPlace(bool isProject, string kind)
        {
            string note = GetSymbolInfo.BuildScopeNote("Src", isProject);

            Assert.That(note, Does.Contain("'Src' " + kind));
            Assert.That(note, Does.Contain("declarations inside it"));
            Assert.That(note, Does.Contain("whole solution"));
        }

        [TestCase("ToolCalls", "ToolCalls", null)]
        [TestCase("ChatMessage.ToolCalls", "ToolCalls", "ChatMessage")]
        [TestCase("LMLocal.Core.Models.ChatMessage.ToolCalls", "ToolCalls", "ChatMessage")]
        [TestCase("global::LMLocal.Core.Models.ChatMessage.ToolCalls", "ToolCalls", "ChatMessage")]
        [TestCase(" ChatMessage.Clone() ", "Clone", "ChatMessage")]
        [TestCase("ToolCalls.", "ToolCalls", null)]
        [TestCase(".ToolCalls", "ToolCalls", null)]
        public void SplitQualifiedName_TakesTheMemberAndItsType(string name, string member, string container)
        {
            var (m, c) = GetSymbolInfo.SplitQualifiedName(name);

            Assert.That(m, Is.EqualTo(member));
            Assert.That(c, Is.EqualTo(container));
        }

        [Test]
        public void QualifiedNameNote_PlainName_IsNull_AndANotFoundMemberSaysWhatToDo()
        {
            Assert.That(GetSymbolInfo.BuildQualifiedNameNote("ToolCalls", "ToolCalls", null, true), Is.Null);
            Assert.That(GetSymbolInfo.BuildQualifiedNameNote("ChatMessage.ToolCalls", "ToolCalls", "ChatMessage", true), Does.Contain("member 'ToolCalls' of 'ChatMessage'"));
            Assert.That(GetSymbolInfo.BuildQualifiedNameNote("Chat.ToolCalls", "ToolCalls", "Chat", false), Does.Contain("no such member was found").And.Contain("symbol_name 'ToolCalls' alone"));
        }

        // A1.29: symbol_name + symbol_names + include_references went to the definitions-only mode silently, and the empty references read as "0 usages".
        [Test]
        public void NoReferencesWarning_OnlyWhenReferencesWereAskedFor()
        {
            Assert.That(GetSymbolInfo.BuildNoReferencesWarning(new Dictionary<string, object> { ["symbol_names"] = new[] { "A" } }), Is.Null);
            Assert.That(GetSymbolInfo.BuildNoReferencesWarning(new Dictionary<string, object> { ["include_references"] = false }), Is.Null);
            Assert.That(GetSymbolInfo.BuildNoReferencesWarning(new Dictionary<string, object> { ["include_references"] = true }), Does.Contain("References were NOT computed"));
            Assert.That(GetSymbolInfo.BuildNoReferencesWarning(new Dictionary<string, object> { ["max_references"] = 200 }), Does.Contain("symbol_name' alone"));
        }
    }
}