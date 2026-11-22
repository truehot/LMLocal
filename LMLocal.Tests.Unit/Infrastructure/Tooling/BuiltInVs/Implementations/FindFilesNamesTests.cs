using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// find_files with 'file_names': several name patterns in one call, one block per name.
    /// </summary>
    [TestFixture]
    public class FindFilesNamesTests
    {
        private const string SolutionDir = @"C:\sol";

        // ---- parameter parsing ---------------------------------------------------------------

        [Test]
        public void Parse_FileNameOnly_IsSingleAndKeepsNameAsIs()
        {
            var request = FindFiles.ExtractAndValidateParameters(new Dictionary<string, object> { ["file_name"] = "Foo*" });

            Assert.That(request.Error, Is.Null);
            Assert.That(request.IsMulti, Is.False);
            Assert.That(request.Names, Is.EqualTo(new[] { "Foo*" }));
        }

        [Test]
        public void Parse_FileNameAsArray_IsReadAsFileNames()
        {
            // Seen in subagent logs: {"file_name":["SnapshotManager.cs"]} cost a round on a "must be a string" error.
            var request = FindFiles.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["file_name"] = new JArray("SnapshotManager.cs", "IFileSystem.cs")
            });

            Assert.That(request.Error, Is.Null);
            Assert.That(request.IsMulti, Is.True);
            Assert.That(request.Names, Is.EqualTo(new[] { "SnapshotManager.cs", "IFileSystem.cs" }));
        }

        [Test]
        public void Parse_FileNameAsArrayWithFileNames_IsError()
        {
            // With 'file_names' present the list in 'file_name' is not reinterpreted.
            var request = FindFiles.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["file_name"] = new JArray("A.cs"),
                ["file_names"] = new JArray("B.cs")
            });

            Assert.That(request.Error, Does.Contain("'file_name' must be a string"));
        }

        [Test]
        public void Parse_NoFileNameAndNoFilters_IsError()
        {
            var request = FindFiles.ExtractAndValidateParameters(new Dictionary<string, object> { ["max_results"] = 10 });

            Assert.That(request.Error, Does.Contain("'file_name'").And.Contain("'file_names'").And.Contain("'.'"));
        }

        [TestCase("extension_filter", ".cs")]
        [TestCase("project_filter", "LMLocal.Tests.Unit")]
        public void Parse_OnlyFilter_ListsAllFiles(string filter, string value)
        {
            var request = FindFiles.ExtractAndValidateParameters(new Dictionary<string, object> { [filter] = value });

            Assert.That(request.Error, Is.Null);
            Assert.That(request.IsMulti, Is.False);
            Assert.That(request.Names, Is.EqualTo(new[] { FindFiles.ListAllFilesName }));
        }

        [Test]
        public void Parse_BlankFilterOnly_IsError()
        {
            var request = FindFiles.ExtractAndValidateParameters(new Dictionary<string, object> { ["project_filter"] = " " });

            Assert.That(request.Error, Does.Contain("'file_name'"));
        }

        [Test]
        public void Parse_FileNamesAsJArray_KeepsOrderDropsEmptyAndCaseDuplicates()
        {
            var request = FindFiles.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["file_names"] = new JArray("Alpha.cs", "", "beta*", "ALPHA.CS")
            });

            Assert.That(request.Error, Is.Null);
            Assert.That(request.IsMulti, Is.True);
            Assert.That(request.Names, Is.EqualTo(new[] { "Alpha.cs", "beta*" }));
        }

        [Test]
        public void Parse_FileNamesAsJsonString_IsParsed()
        {
            var request = FindFiles.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["file_names"] = "[\"One.cs\", \"Two.cs\"]"
            });

            Assert.That(request.Names, Is.EqualTo(new[] { "One.cs", "Two.cs" }));
        }

        [Test]
        public void Parse_FileNamesAsPlainStringWithComma_IsOneName()
        {
            var request = FindFiles.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["file_names"] = "One.cs, Two.cs"
            });

            Assert.That(request.IsMulti, Is.True);
            Assert.That(request.Names, Is.EqualTo(new[] { "One.cs, Two.cs" }));
        }

        [Test]
        public void Parse_FileNameAndFileNames_FileNameGoesFirst()
        {
            var request = FindFiles.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["file_name"] = "First.cs",
                ["file_names"] = new JArray("Second.cs", "first.cs")
            });

            Assert.That(request.Names, Is.EqualTo(new[] { "First.cs", "Second.cs" }));
        }

        [Test]
        public void Parse_FileNamesWithNonString_IsError()
        {
            var request = FindFiles.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["file_names"] = new JArray("One.cs", 5)
            });

            Assert.That(request.Error, Does.Contain("array of strings"));
        }

        [Test]
        public void Parse_FileNamesOnlyEmpty_IsError()
        {
            var request = FindFiles.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["file_names"] = new JArray("", "")
            });

            Assert.That(request.Error, Does.Contain("no non-empty name"));
        }

        [Test]
        public void Parse_FileNamesNull_FallsBackToFileName()
        {
            var request = FindFiles.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["file_name"] = "One.cs",
                ["file_names"] = JValue.CreateNull()
            });

            Assert.That(request.Error, Is.Null);
            Assert.That(request.IsMulti, Is.False);
        }

        [Test]
        public void Parse_MoreThanLimit_IsError()
        {
            var names = Enumerable.Range(1, FindFiles.MaxFileNames + 1).Select(i => "F" + i + ".cs").ToArray();
            var request = FindFiles.ExtractAndValidateParameters(new Dictionary<string, object> { ["file_names"] = new JArray(names) });

            Assert.That(request.Error, Does.StartWith($"At most {FindFiles.MaxFileNames} file names per call (got {FindFiles.MaxFileNames + 1})."));
            Assert.That(request.Names, Has.Count.EqualTo(FindFiles.MaxFileNames + 1));
        }

        [Test]
        public void ProcessingMessage_TooManyNames_ListsThemAndCountsTheRest()
        {
            var tool = Tool(new SearchResultCache());
            var names = Enumerable.Range(1, FindFiles.MaxFileNames + 2).Select(i => "F" + i + ".cs").ToArray();

            var message = tool.GetProcessingMessage(new Dictionary<string, object> { ["file_names"] = new JArray(names) });

            Assert.That(message, Does.StartWith("Finding 'F1.cs', 'F2.cs'"));
            Assert.That(message, Does.EndWith(" and 2 more... "));
        }

        [Test]
        public void Parse_PageTokenWithSeveralNames_IsError()
        {
            var request = FindFiles.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["file_names"] = new JArray("A.cs", "B.cs"),
                ["page_token"] = "50"
            });

            Assert.That(request.Error, Does.Contain("continues one name"));
        }

        [Test]
        public void Parse_PageTokenWithOneNameInFileNames_IsAllowed()
        {
            var request = FindFiles.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["file_names"] = new JArray("A.cs"),
                ["page_token"] = "50"
            });

            Assert.That(request.Error, Is.Null);
        }

        // ---- ExecuteAsync from the cache (no solution enumeration) ---------------------------

        [Test]
        public async Task Execute_FileNames_ReturnsOneBlockPerNameInOrder()
        {
            var cache = new SearchResultCache();
            Cache(cache, "Alpha*", "A\\Alpha.cs", "B\\AlphaTests.cs");
            Cache(cache, "Missing.cs");
            var tool = Tool(cache);

            var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["file_names"] = new JArray("Alpha*", "Missing.cs") });

            var multi = result as FindFiles.FindFilesMultiResponse;
            Assert.That(multi, Is.Not.Null);
            Assert.That(multi.Success, Is.True);
            Assert.That(multi.Terms.Select(b => b.FileName), Is.EqualTo(new[] { "Alpha*", "Missing.cs" }));
            Assert.That(multi.Terms[0].Results.Select(r => r.FilePath), Is.EqualTo(new[] { "A\\Alpha.cs", "B\\AlphaTests.cs" }));
            Assert.That(multi.Terms[1].TotalFiles, Is.EqualTo(0));
            Assert.That(tool.GetCompletionMessage(result), Is.EqualTo("Found 2 files for 2 names ('Alpha*': 2, 'Missing.cs': 0)."));
        }

        [Test]
        public async Task Execute_FileNames_SplitsThePageBudgetBetweenNames()
        {
            var cache = new SearchResultCache();
            var many = Enumerable.Range(1, 400).Select(i => "F" + i + ".cs").ToArray();
            Cache(cache, "A*", many);
            Cache(cache, "B*", many);
            var tool = Tool(cache);

            var result = (FindFiles.FindFilesMultiResponse)await tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["file_names"] = new JArray("A*", "B*"),
                ["max_results"] = 500
            });

            foreach (var block in result.Terms)
            {
                Assert.That(block.Results.Count, Is.EqualTo(250), block.FileName);
                Assert.That(block.NextPageToken, Does.Match(@"^r\d+:250$"), block.FileName);
            }
        }

        [Test]
        public async Task Execute_FileNameOnly_ReturnsTheSingleResponseWithoutFileName()
        {
            var cache = new SearchResultCache();
            Cache(cache, "Alpha*", "Alpha.cs");
            var tool = Tool(cache);

            var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["file_name"] = "Alpha*" });

            Assert.That(result, Is.TypeOf<FindFiles.FileSearchResultsResponse>());
            Assert.That(JObject.FromObject(result).Property("file_name"), Is.Null);
            Assert.That(tool.GetCompletionMessage(result), Is.EqualTo("Found 1 file."));
        }

        [Test]
        public async Task Execute_BlockJson_StartsWithFileName()
        {
            var cache = new SearchResultCache();
            Cache(cache, "Alpha*", "Alpha.cs");
            var tool = Tool(cache);

            var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["file_names"] = new JArray("Alpha*") });

            var json = JObject.Parse(JsonConvert.SerializeObject(result));
            var block = (JObject)json["terms"][0];
            Assert.That(block.Properties().First().Name, Is.EqualTo("file_name"));
            Assert.That((string)block["file_name"], Is.EqualTo("Alpha*"));
        }

        [Test]
        public async Task Execute_PageTokenContinuesOneBlockWithFileName()
        {
            var cache = new SearchResultCache();
            Cache(cache, "A*", Enumerable.Range(1, 60).Select(i => "A" + i + ".cs").ToArray());
            Cache(cache, "B*");
            var tool = Tool(cache);

            var first = (FindFiles.FindFilesMultiResponse)await tool.ExecuteAsync(new Dictionary<string, object> { ["file_names"] = new JArray("A*", "B*") });
            var block = first.Terms[0];
            Assert.That(block.NextPageToken, Does.Match(@"^r\d+:50$"));

            var next = (FindFiles.FileSearchResultsResponse)await tool.ExecuteAsync(new Dictionary<string, object> { ["file_name"] = "A*", ["page_token"] = block.NextPageToken });
            Assert.That(next.Results.Count, Is.EqualTo(10));
        }

        [Test]
        public void ProcessingMessage_ListsTheNames()
        {
            var tool = Tool(new SearchResultCache());

            Assert.That(tool.GetProcessingMessage(new Dictionary<string, object> { ["file_names"] = new JArray("A.cs", "B.cs") }),
                Is.EqualTo("Finding 'A.cs', 'B.cs'... "));
            Assert.That(tool.GetProcessingMessage(new Dictionary<string, object> { ["file_name"] = "A.cs", ["extension_filter"] = ".cs" }),
                Is.EqualTo("Finding 'A.cs', with extension '.cs'... "));
        }

        // ---- helpers -------------------------------------------------------------------------

        private static FindFiles Tool(ISearchResultCache cache)
        {
            var vs = new Mock<IVsDependencies>();
            vs.SetupGet(v => v.IsSolutionOpen).Returns(true);
            vs.Setup(v => v.GetSolutionDirectory()).Returns(SolutionDir);
            return new FindFiles(vs.Object, Mock.Of<IVsSolutionFilesScanner>(), cache);
        }

        /// <summary>Every name of a test is cached, so ExecuteAsync never enumerates solution files.</summary>
        private static void Cache(ISearchResultCache cache, string name, params string[] paths)
        {
            cache.Set(FindFiles.BuildCacheKey(name, null, null), SolutionDir, new CachedToolResults<FindFiles.FileSearchResult>
            {
                AllResults = paths.Select(p => new FindFiles.FileSearchResult { FilePath = p }).ToList(),
                ItemsScanned = paths.Length
            });
        }
    }
}
