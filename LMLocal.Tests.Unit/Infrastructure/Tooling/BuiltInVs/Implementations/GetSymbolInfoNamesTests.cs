using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// get_symbol_info with 'symbol_names': definitions of several symbols in one call, one block per name.
    /// Lookups that reach Roslyn need Visual Studio, so execution tests use cached definitions.
    /// </summary>
    [TestFixture]
    public class GetSymbolInfoNamesTests
    {
        private static GetSymbolInfo Tool(ISearchResultCache cache = null)
        {
            return new GetSymbolInfo(Mock.Of<IPathResolver>(), cache ?? new SearchResultCache(), Mock.Of<IFileSystem>());
        }

        private static void CacheDefinitions(ISearchResultCache cache, string name, params (string file, int line, int endLine)[] definitions)
        {
            cache.Set(GetSymbolInfo.BuildCacheKey(name, null, false), "", new CachedToolResults<GetSymbolInfo.SymbolInfo>
            {
                AllResults = new List<GetSymbolInfo.SymbolInfo>
                {
                    new GetSymbolInfo.SymbolInfo
                    {
                        Definitions = definitions.Select(d => new GetSymbolInfo.DefinitionItem { FilePath = d.file, Line = d.line, EndLine = d.endLine, SymbolKind = "NamedType" }).ToList()
                    }
                },
                ItemsScanned = 1
            });
        }

        // ---- parameter parsing ---------------------------------------------------------------

        [Test]
        public void Parse_KeepsOrderDropsEmptyAndExactDuplicates_CaseMatters()
        {
            var (names, error) = GetSymbolInfo.ExtractSymbolNames(new Dictionary<string, object>(), new JArray("Alpha", "", "beta", "Alpha", "alpha"));

            Assert.That(error, Is.Null);
            Assert.That(names, Is.EqualTo(new[] { "Alpha", "beta", "alpha" }));
        }

        [Test]
        public void Parse_TopLevelSymbolNameGoesFirst()
        {
            var (names, error) = GetSymbolInfo.ExtractSymbolNames(new Dictionary<string, object> { ["symbol_name"] = "Zeta" }, "[\"Alpha\", \"Zeta\"]");

            Assert.That(error, Is.Null);
            Assert.That(names, Is.EqualTo(new[] { "Zeta", "Alpha" }));
        }

        [Test]
        public void Parse_Errors()
        {
            Assert.That(GetSymbolInfo.ExtractSymbolNames(new Dictionary<string, object>(), new JArray(1, 2)).error, Does.Contain("array of strings"));
            Assert.That(GetSymbolInfo.ExtractSymbolNames(new Dictionary<string, object>(), new JArray()).error, Does.Contain("no non-empty name"));

            var many = new JArray(Enumerable.Range(1, GetSymbolInfo.MaxSymbolNames + 1).Select(i => "S" + i));
            Assert.That(GetSymbolInfo.ExtractSymbolNames(new Dictionary<string, object>(), many).error,
                Does.StartWith($"At most {GetSymbolInfo.MaxSymbolNames} symbol names per call (got {GetSymbolInfo.MaxSymbolNames + 1})."));

            var (names, error) = GetSymbolInfo.ExtractSymbolNames(new Dictionary<string, object> { ["page_token"] = "50" }, new JArray("A", "B"));
            Assert.That(error, Does.Contain("'symbol_name'"));
        }

        // ---- execution -----------------------------------------------------------------------

        [Test]
        public async Task SymbolNames_ReturnsDefinitionsBlocksInOrder()
        {
            var cache = new SearchResultCache();
            CacheDefinitions(cache, "ToolPagination", ("A\\ToolPagination.cs", 5, 88));
            CacheDefinitions(cache, "Missing");
            CacheDefinitions(cache, "Partial", ("A\\Partial.cs", 1, 10), ("A\\Partial.More.cs", 3, 20));
            var tool = Tool(cache);

            var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["symbol_names"] = new JArray("ToolPagination", "Missing", "Partial") });

            Assert.That(result, Is.TypeOf<GetSymbolInfo.SymbolInfoMultiResponse>());
            var response = (GetSymbolInfo.SymbolInfoMultiResponse)result;
            Assert.That(response.Success, Is.True);
            Assert.That(response.Symbols.Select(b => b.SymbolName), Is.EqualTo(new[] { "ToolPagination", "Missing", "Partial" }));
            Assert.That(response.Symbols[0].Definitions.Single().EndLine, Is.EqualTo(88));
            Assert.That(response.Symbols[1].Definitions, Is.Empty);
            Assert.That(response.Symbols[2].Definitions.Count, Is.EqualTo(2));
            Assert.That(tool.GetCompletionMessage(response), Is.EqualTo("Found 3 definitions for 3 symbols ('ToolPagination': 1, 'Missing': 0, 'Partial': 2)."));
        }

        [Test]
        public async Task SymbolNames_JsonHasSymbolsBlocksWithoutReferences()
        {
            var cache = new SearchResultCache();
            CacheDefinitions(cache, "Alpha", ("A\\Alpha.cs", 1, 9));
            var result = await Tool(cache).ExecuteAsync(new Dictionary<string, object> { ["symbol_names"] = new JArray("Alpha") });

            var json = JObject.Parse(JsonConvert.SerializeObject(result));
            var block = (JObject)json["symbols"][0];

            Assert.That(block.Properties().Select(p => p.Name), Is.EqualTo(new[] { "symbol_name", "success", "definitions" }));
            Assert.That((int)block["definitions"][0]["end_line"], Is.EqualTo(9));
            Assert.That(json.Property("references"), Is.Null);
        }

        [Test]
        public async Task SymbolNames_ValidationError_IsTopLevelError()
        {
            var tool = Tool();
            var result = (GetSymbolInfo.SymbolInfoMultiResponse)await tool.ExecuteAsync(new Dictionary<string, object> { ["symbol_names"] = new JArray() });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Symbols, Is.Empty);
            Assert.That(tool.GetCompletionMessage(result), Does.StartWith("Failed: Parameter 'symbol_names' has no non-empty name"));
        }

        [Test]
        public async Task NoSymbolNameAndNoSymbolNames_SingleErrorNamesBoth()
        {
            var result = (GetSymbolInfo.SymbolInfoResponse)await Tool().ExecuteAsync(new Dictionary<string, object> { ["include_references"] = false });

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("'symbol_name'").And.Contain("'symbol_names'"));
        }

        // ---- messages and schema -------------------------------------------------------------

        [Test]
        public void ProcessingMessage_ListsTheNames()
        {
            var message = Tool().GetProcessingMessage(new Dictionary<string, object> { ["symbol_names"] = new JArray("A", "B") });

            Assert.That(message, Is.EqualTo("Getting symbol info for 'A', 'B'... "));
        }

        [Test]
        public void CompletionMessage_FailedBlockIsMarked()
        {
            var response = new GetSymbolInfo.SymbolInfoMultiResponse
            {
                Success = true,
                Symbols = new List<GetSymbolInfo.SymbolDefinitionsBlock>
                {
                    new GetSymbolInfo.SymbolDefinitionsBlock { SymbolName = "A", Success = true, Definitions = new List<GetSymbolInfo.DefinitionItem> { new GetSymbolInfo.DefinitionItem() } },
                    new GetSymbolInfo.SymbolDefinitionsBlock { SymbolName = "B", Success = false, ErrorMessage = "boom", Definitions = new List<GetSymbolInfo.DefinitionItem>() }
                }
            };

            Assert.That(Tool().GetCompletionMessage(response), Is.EqualTo("Found 1 definition for 2 symbols ('A': 1, 'B': failed)."));
        }

        [Test]
        public void Schema_SymbolNamesIsLastAndNothingIsRequired()
        {
            var info = Tool().GetToolInfo();

            Assert.That(info.Parameters.Required, Is.Empty);
            Assert.That(info.Parameters.Properties.Keys.Last(), Is.EqualTo("symbol_names"));
            Assert.That(info.Parameters.Properties["symbol_names"].Items.Type, Is.EqualTo("string"));
        }
    }
}
