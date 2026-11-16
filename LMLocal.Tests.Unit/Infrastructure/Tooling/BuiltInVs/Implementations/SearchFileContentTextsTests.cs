using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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
    /// search_file_content with 'texts': several literal texts in one call, one block per text.
    /// </summary>
    [TestFixture]
    public class SearchFileContentTextsTests
    {
        private const string SolutionDir = @"C:\sol";

        // ---- parameter parsing ---------------------------------------------------------------

        [Test]
        public void Parse_TextOnly_IsSingleAndKeepsTextAsIs()
        {
            var request = SearchFileContent.ExtractAndValidateParameters(new Dictionary<string, object> { ["text"] = " Foo " });

            Assert.That(request.Error, Is.Null);
            Assert.That(request.IsMulti, Is.False);
            Assert.That(request.Terms, Is.EqualTo(new[] { " Foo " }));
        }

        [Test]
        public void Parse_NoTextAndNoTexts_IsError()
        {
            var request = SearchFileContent.ExtractAndValidateParameters(new Dictionary<string, object> { ["path"] = "x" });

            Assert.That(request.Error, Does.Contain("'text'").And.Contain("'texts'"));
        }

        [Test]
        public void Parse_TextsAsJArray_KeepsOrderDropsEmptyAndCaseDuplicates()
        {
            var request = SearchFileContent.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["texts"] = new JArray("Alpha", "", "beta", "ALPHA", "a, b")
            });

            Assert.That(request.Error, Is.Null);
            Assert.That(request.IsMulti, Is.True);
            Assert.That(request.Terms, Is.EqualTo(new[] { "Alpha", "beta", "a, b" }));
        }

        [Test]
        public void Parse_TextsAsObjectArray_IsAccepted()
        {
            var request = SearchFileContent.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["texts"] = new object[] { "One", "Two" }
            });

            Assert.That(request.Terms, Is.EqualTo(new[] { "One", "Two" }));
        }

        [Test]
        public void Parse_TextsAsJsonString_IsParsed()
        {
            var request = SearchFileContent.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["texts"] = "[\"One\", \"Two\"]"
            });

            Assert.That(request.Terms, Is.EqualTo(new[] { "One", "Two" }));
        }

        [Test]
        public void Parse_TextsAsPlainStringWithComma_IsOneText()
        {
            var request = SearchFileContent.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["texts"] = "Foo(a, b)"
            });

            Assert.That(request.IsMulti, Is.True);
            Assert.That(request.Terms, Is.EqualTo(new[] { "Foo(a, b)" }));
        }

        [Test]
        public void Parse_TextAndTexts_TextGoesFirst()
        {
            var request = SearchFileContent.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["text"] = "First",
                ["texts"] = new JArray("Second", "first")
            });

            Assert.That(request.Terms, Is.EqualTo(new[] { "First", "Second" }));
        }

        [Test]
        public void Parse_TextsWithNonString_IsError()
        {
            var request = SearchFileContent.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["texts"] = new JArray("One", 5)
            });

            Assert.That(request.Error, Does.Contain("array of strings"));
        }

        [Test]
        public void Parse_TextsOnlyEmpty_IsError()
        {
            var request = SearchFileContent.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["texts"] = new JArray("", "")
            });

            Assert.That(request.Error, Is.Not.Null);
        }

        [Test]
        public void Parse_TooManyTexts_IsError()
        {
            var texts = new JArray(Enumerable.Range(1, SearchFileContent.MaxTexts + 1).Select(i => "t" + i));
            var request = SearchFileContent.ExtractAndValidateParameters(new Dictionary<string, object> { ["texts"] = texts });

            Assert.That(request.Error, Does.Contain("At most " + SearchFileContent.MaxTexts));
        }

        [Test]
        public void Parse_PageTokenWithSeveralTexts_IsError()
        {
            var request = SearchFileContent.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["texts"] = new JArray("One", "Two"),
                ["page_token"] = "25"
            });

            Assert.That(request.Error, Does.Contain("'text'").And.Contain("page_token"));
        }

        [Test]
        public void Parse_PageTokenWithOneText_IsAllowed()
        {
            var request = SearchFileContent.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["texts"] = new JArray("One"),
                ["page_token"] = "25"
            });

            Assert.That(request.Error, Is.Null);
        }

        // ---- scanner -------------------------------------------------------------------------

        [Test]
        public async Task Scan_EachTextHasItsOwnResultsAndRanking()
        {
            var fs = await FileSystemWith(
                (@"C:\sol\A.cs", "class Widget {}\nvar w = new Widget();\n// gadget"),
                (@"C:\sol\B.cs", "Widget x;\nclass Gadget {}"));

            var results = await Scan(fs, new[] { @"C:\sol\A.cs", @"C:\sol\B.cs" }, "Widget", "Gadget", "Missing");

            Assert.That(results.Select(r => r.Term), Is.EqualTo(new[] { "Widget", "Gadget", "Missing" }));

            // Widget: A.cs has the declaration and two matches, so it ranks first.
            Assert.That(results[0].Results.Select(r => r.FilePath), Is.EqualTo(new[] { "A.cs", "B.cs" }));
            Assert.That(results[0].Results[0].Matches.Select(m => m.LineNumber), Is.EqualTo(new[] { 1, 2 }));

            // Gadget: B.cs has the class declaration and ranks above the comment in A.cs.
            Assert.That(results[1].Results.Select(r => r.FilePath), Is.EqualTo(new[] { "B.cs", "A.cs" }));

            Assert.That(results[2].Results, Is.Empty);
            Assert.That(results[2].FallbackNote, Is.Null);
        }

        [Test]
        public async Task Scan_MatchesSameAsSingleTextScans()
        {
            var fs = await FileSystemWith(
                (@"C:\sol\A.cs", "alpha beta\nbeta\nalpha"),
                (@"C:\sol\B.cs", "gamma\nalpha gamma"));
            var files = new[] { @"C:\sol\A.cs", @"C:\sol\B.cs" };

            var together = await Scan(fs, files, "alpha", "beta", "gamma");
            foreach (var term in together)
            {
                var alone = (await Scan(fs, files, term.Term))[0];
                Assert.That(Describe(term.Results), Is.EqualTo(Describe(alone.Results)), term.Term);
            }
        }

        [Test]
        public async Task Scan_FallbackIsPerText()
        {
            var fs = await FileSystemWith((@"C:\sol\A.cs", "Foo here\nBar there"));

            var results = await Scan(fs, new[] { @"C:\sol\A.cs" }, "Foo", "Foo OR Bar");

            Assert.That(results[0].FallbackNote, Is.Null);
            Assert.That(results[0].Results[0].Matches.Select(m => m.LineNumber), Is.EqualTo(new[] { 1 }));

            Assert.That(results[1].FallbackNote, Does.Contain("split"));
            Assert.That(results[1].Results[0].Matches.Select(m => m.MatchedTerm), Is.EqualTo(new[] { "Foo", "Bar" }));
        }

        [Test]
        public async Task Scan_MissingFileIsSkipped()
        {
            var fs = await FileSystemWith((@"C:\sol\A.cs", "Foo"));

            var results = await Scan(fs, new[] { @"C:\sol\Gone.cs", @"C:\sol\A.cs" }, "Foo");

            Assert.That(results[0].Results.Select(r => r.FilePath), Is.EqualTo(new[] { "A.cs" }));
        }

        [Test]
        public async Task Scan_ResultHasFileLineCount()
        {
            var fs = await FileSystemWith(
                (@"C:\sol\A.cs", "Foo\nbar\nbaz"),
                (@"C:\sol\B.cs", "x\ny\nFoo"));

            var results = await Scan(fs, new[] { @"C:\sol\A.cs", @"C:\sol\B.cs" }, "Foo");

            Assert.That(results[0].Results.ToDictionary(r => r.FilePath, r => r.TotalLines),
                Is.EqualTo(new Dictionary<string, int?> { ["A.cs"] = 3, ["B.cs"] = 3 }));

            // A fallback result carries it too.
            var fallback = (await Scan(fs, new[] { @"C:\sol\A.cs" }, "Bar OR Baz"))[0];
            Assert.That(fallback.FallbackNote, Is.Not.Null);
            Assert.That(fallback.Results.Single().TotalLines, Is.EqualTo(3));
        }

        [Test]
        public void FilePage_SplitFileKeepsLineCount()
        {
            var result = Result("A.cs", Enumerable.Range(1, 30).ToArray());
            result.TotalLines = 120;

            var page = SearchFileContent.BuildFilePageResponse(new[] { result }, 25, 25, null);

            Assert.That(page.Results.Single().MatchCount, Is.EqualTo(5));
            Assert.That(page.Results.Single().TotalLines, Is.EqualTo(120));
            Assert.That((int)JObject.FromObject(page)["results"][0]["total_lines"], Is.EqualTo(120));
        }

        // ---- ExecuteAsync from the cache (no file enumeration) -------------------------------

        [Test]
        public async Task Execute_Texts_ReturnsOneBlockPerTextInOrder()
        {
            var cache = new SearchResultCache();
            Cache(cache, "Alpha", Result("A.cs", 1, 2), Result("B.cs", 3));
            Cache(cache, "Beta");
            var tool = Tool(cache);

            var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["texts"] = new JArray("Alpha", "Beta") });

            var multi = result as SearchMultiTermResponse;
            Assert.That(multi, Is.Not.Null);
            Assert.That(multi.Success, Is.True);
            var blocks = multi.Terms.Cast<SearchResultsResponse>().ToList();
            Assert.That(blocks.Select(b => b.Text), Is.EqualTo(new[] { "Alpha", "Beta" }));
            Assert.That(blocks[0].TotalMatches, Is.EqualTo(3));
            Assert.That(blocks[1].TotalMatches, Is.EqualTo(0));
            Assert.That(tool.GetCompletionMessage(result), Is.EqualTo("Found 3 matches for 2 texts ('Alpha': 3, 'Beta': 0)."));
        }

        [Test]
        public async Task Execute_Texts_SplitsThePageBudgetBetweenTexts()
        {
            var cache = new SearchResultCache();
            var many = Enumerable.Range(1, 400).ToArray();
            Cache(cache, "Alpha", Result("A.cs", many));
            Cache(cache, "Beta", Result("B.cs", many));
            var tool = Tool(cache);

            var result = (SearchMultiTermResponse)await tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["texts"] = new JArray("Alpha", "Beta"),
                ["max_results"] = 500
            });

            foreach (SearchResultsResponse block in result.Terms.Cast<SearchResultsResponse>())
            {
                Assert.That(block.Results.Sum(r => r.MatchCount), Is.EqualTo(250), block.Text);
                Assert.That(block.NextPageToken, Is.EqualTo("250"), block.Text);
            }
        }

        [Test]
        public async Task Execute_TextOnly_ReturnsTheSingleResponseWithoutText()
        {
            var cache = new SearchResultCache();
            Cache(cache, "Alpha", Result("A.cs", 1));
            var tool = Tool(cache);

            var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["text"] = "Alpha" });

            Assert.That(result, Is.TypeOf<SearchResultsResponse>());
            Assert.That(((SearchResultsResponse)result).Text, Is.Null);
            Assert.That(JObject.FromObject(result).Property("text"), Is.Null);
        }

        [Test]
        public async Task Execute_BlockJson_StartsWithText()
        {
            var cache = new SearchResultCache();
            Cache(cache, "Alpha", Result("A.cs", 1));
            var tool = Tool(cache);

            var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["texts"] = new JArray("Alpha") });

            var json = JObject.Parse(JsonConvert.SerializeObject(result));
            var block = (JObject)json["terms"][0];
            Assert.That(block.Properties().First().Name, Is.EqualTo("text"));
            Assert.That((string)block["text"], Is.EqualTo("Alpha"));
        }

        [Test]
        public async Task Execute_PageTokenContinuesOneBlockWithText()
        {
            var cache = new SearchResultCache();
            Cache(cache, "Alpha", Result("A.cs", Enumerable.Range(1, 30).ToArray()));
            Cache(cache, "Beta");
            var tool = Tool(cache);

            var first = (SearchMultiTermResponse)await tool.ExecuteAsync(new Dictionary<string, object> { ["texts"] = new JArray("Alpha", "Beta") });
            var block = first.Terms.Cast<SearchResultsResponse>().First();
            Assert.That(block.NextPageToken, Is.EqualTo("25"));

            var next = (SearchResultsResponse)await tool.ExecuteAsync(new Dictionary<string, object> { ["text"] = "Alpha", ["page_token"] = block.NextPageToken });
            Assert.That(next.Results.Sum(r => r.MatchCount), Is.EqualTo(5));
        }

        [Test]
        public void ProcessingMessage_ListsTheTexts()
        {
            var tool = Tool(new SearchResultCache());

            Assert.That(tool.GetProcessingMessage(new Dictionary<string, object> { ["texts"] = new JArray("A", "B") }),
                Does.StartWith("Searching for 'A', 'B' in all files"));
        }

        // ---- helpers -------------------------------------------------------------------------

        private static SearchFileContent Tool(ISearchResultCache cache)
        {
            var vs = new Mock<IVsDependencies>();
            vs.SetupGet(v => v.IsSolutionOpen).Returns(true);
            vs.Setup(v => v.GetSolutionDirectory()).Returns(SolutionDir);
            return new SearchFileContent(vs.Object, Mock.Of<IPathResolver>(), Mock.Of<IVsSolutionFilesScanner>(), cache, Mock.Of<IFileSystem>());
        }

        /// <summary>Every text of a test is cached, so ExecuteAsync never enumerates solution files.</summary>
        private static void Cache(ISearchResultCache cache, string text, params SearchResult[] results)
        {
            cache.Set(SearchFileContent.BuildCacheKey(text, null, null, SearchGrouping.File), SolutionDir,
                new CachedToolResults<SearchResult> { AllResults = results.ToList(), ItemsScanned = 1 });
        }

        private static SearchResult Result(string path, params int[] lines)
        {
            return new SearchResult
            {
                FilePath = path,
                Matches = lines.Select(l => new SearchMatch { LineNumber = l, LineText = "line" + l }).ToList(),
                MatchCount = lines.Length
            };
        }

        private static async Task<InMemoryFileSystem> FileSystemWith(params (string Path, string Content)[] files)
        {
            var fs = new InMemoryFileSystem();
            foreach (var (path, content) in files)
                await fs.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(content));
            return fs;
        }

        private static Task<List<TermScanResult>> Scan(IFileSystem fs, IReadOnlyList<string> files, params string[] terms)
        {
            return ContentTermScanner.ScanAsync(fs, files, terms, needRanking: true, toRelativePath: p => p.Substring(SolutionDir.Length + 1), CancellationToken.None);
        }

        private static string Describe(IEnumerable<SearchResult> results)
        {
            return string.Join(" | ", results.Select(r => r.FilePath + ":" + r.Score + ":" + string.Join(",", r.Matches.Select(m => m.LineNumber + (m.IsExactWord ? "e" : "") + m.DeclarationKind))));
        }
    }
}
