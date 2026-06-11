using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Moq;
using NUnit.Framework;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations.FileLinesReader;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations.ActiveDocument;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations.FindFilesByName;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations.FindSymbolReferences;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations.GetSolutionOverview;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations.SolutionSearch;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations.ListDirectoryContents;

namespace LMLocal.Tests.Unit.Infrastructure.Vs
{
    [TestFixture]
    public class VsToolFactoryTests
    {
        [Test]
        public void GetAllToolDefinitions_ReturnsDefinitionsFromTools()
        {
            var searchTool = new Mock<ISolutionSearch>();
            var activeTool = new Mock<IActiveDocument>();
            var linesTool = new Mock<IFileLinesReader>();
            var findFilesTool = new Mock<IFindFilesByName>();
            var solutionOverviewTool = new Mock<IGetSolutionOverview>();
            var findSymbolReferencesTool = new Mock<IFindSymbolReferences>();
            var listDirTool = new Mock<IListDirectoryContents>();

            searchTool.Setup(s => s.GetToolInfo()).Returns(new ToolDefinition { Name = "search_in_files" });
            searchTool.SetupGet(s => s.ToolName).Returns("search_in_files");

            activeTool.Setup(s => s.GetToolInfo()).Returns(new ToolDefinition { Name = "get_active_document" });
            activeTool.SetupGet(s => s.ToolName).Returns("get_active_document");

            linesTool.Setup(s => s.GetToolInfo()).Returns(new ToolDefinition { Name = "read_file_lines" });
            linesTool.SetupGet(s => s.ToolName).Returns("read_file_lines");

            findFilesTool.Setup(s => s.GetToolInfo()).Returns(new ToolDefinition { Name = "find_files_by_name" });
            findFilesTool.SetupGet(s => s.ToolName).Returns("find_files_by_name");

            solutionOverviewTool.Setup(s => s.GetToolInfo()).Returns(new ToolDefinition { Name = "get_solution_overview" });
            solutionOverviewTool.SetupGet(s => s.ToolName).Returns("get_solution_overview");

            findSymbolReferencesTool.Setup(s => s.GetToolInfo()).Returns(new ToolDefinition { Name = "find_symbol_references" });
            findSymbolReferencesTool.SetupGet(s => s.ToolName).Returns("find_symbol_references");

            listDirTool.Setup(s => s.GetToolInfo()).Returns(new ToolDefinition { Name = "list_directory_contents" });
            listDirTool.SetupGet(s => s.ToolName).Returns("list_directory_contents");

            var factory = new BuiltInVsToolProvider(searchTool.Object, activeTool.Object, linesTool.Object, findFilesTool.Object, solutionOverviewTool.Object, findSymbolReferencesTool.Object, listDirTool.Object);

            var defs = factory.GetAllToolDefinitions();

            Assert.That(defs, Is.Not.Null);
            Assert.That(defs.Count, Is.EqualTo(7));
            Assert.That(defs[0].Name, Is.EqualTo("search_in_files"));
            Assert.That(defs[1].Name, Is.EqualTo("get_active_document"));
            Assert.That(defs[2].Name, Is.EqualTo("read_file_lines"));
            Assert.That(defs[3].Name, Is.EqualTo("find_files_by_name"));
            Assert.That(defs[4].Name, Is.EqualTo("get_solution_overview"));
            Assert.That(defs[5].Name, Is.EqualTo("find_symbol_references"));
            Assert.That(defs[6].Name, Is.EqualTo("list_directory_contents"));
        }

        [Test]
        public void GetTool_ReturnsCorrectToolOrThrows()
        {
            var searchTool = new Mock<ISolutionSearch>();
            var activeTool = new Mock<IActiveDocument>();
            var linesTool = new Mock<IFileLinesReader>();
            var findFilesTool = new Mock<IFindFilesByName>();
            var solutionOverviewTool = new Mock<IGetSolutionOverview>();
            var findSymbolReferencesTool = new Mock<IFindSymbolReferences>();
            var listDirTool = new Mock<IListDirectoryContents>();

            searchTool.SetupGet(s => s.ToolName).Returns("search_in_files");
            activeTool.SetupGet(s => s.ToolName).Returns("get_active_document");
            linesTool.SetupGet(s => s.ToolName).Returns("read_file_lines");
            findFilesTool.SetupGet(s => s.ToolName).Returns("find_files_by_name");
            solutionOverviewTool.SetupGet(s => s.ToolName).Returns("get_solution_overview");
            findSymbolReferencesTool.SetupGet(s => s.ToolName).Returns("find_symbol_references");
            listDirTool.SetupGet(s => s.ToolName).Returns("list_directory_contents");

            var factory = new BuiltInVsToolProvider(searchTool.Object, activeTool.Object, linesTool.Object, findFilesTool.Object, solutionOverviewTool.Object, findSymbolReferencesTool.Object, listDirTool.Object);

            var t1 = factory.GetTool("search_in_files");
            Assert.That(t1, Is.SameAs(searchTool.Object));

            var t2 = factory.GetTool("get_active_document");
            Assert.That(t2, Is.SameAs(activeTool.Object));

            var t3 = factory.GetTool("read_file_lines");
            Assert.That(t3, Is.SameAs(linesTool.Object));

            var t4 = factory.GetTool("find_files_by_name");
            Assert.That(t4, Is.SameAs(findFilesTool.Object));

            var t5 = factory.GetTool("get_solution_overview");
            Assert.That(t5, Is.SameAs(solutionOverviewTool.Object));

            var t6 = factory.GetTool("find_symbol_references");
            Assert.That(t6, Is.SameAs(findSymbolReferencesTool.Object));

            var t7 = factory.GetTool("list_directory_contents");
            Assert.That(t7, Is.SameAs(listDirTool.Object));

            Assert.Throws<ArgumentException>(() => factory.GetTool("nonexistent_tool"));
        }

        [Test]
        public async Task ExecuteAsync_DispatchesToCorrectTool()
        {
            var searchTool = new Mock<ISolutionSearch>();
            var activeTool = new Mock<IActiveDocument>();
            var linesTool = new Mock<IFileLinesReader>();
            var findFilesTool = new Mock<IFindFilesByName>();
            var solutionOverviewTool = new Mock<IGetSolutionOverview>();
            var findSymbolReferencesTool = new Mock<IFindSymbolReferences>();
            var listDirTool = new Mock<IListDirectoryContents>();

            searchTool.SetupGet(s => s.ToolName).Returns("search_in_files");
            activeTool.SetupGet(s => s.ToolName).Returns("get_active_document");
            linesTool.SetupGet(s => s.ToolName).Returns("read_file_lines");
            findFilesTool.SetupGet(s => s.ToolName).Returns("find_files_by_name");
            solutionOverviewTool.SetupGet(s => s.ToolName).Returns("get_solution_overview");
            findSymbolReferencesTool.SetupGet(s => s.ToolName).Returns("find_symbol_references");
            listDirTool.SetupGet(s => s.ToolName).Returns("list_directory_contents");

            var expectedSearchResult = new SearchResultsResponse { Success = true, Results = new List<SearchResult> { new SearchResult { FilePath = "a.cs", Matches = new System.Collections.Generic.List<SearchMatch> { new SearchMatch { LineNumber = 1, LineText = "x" } }, MatchCount = 1 } }, NextPageToken = null, TotalMatches = 1, TotalFiles = 1 };
            searchTool.Setup(s => s.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedSearchResult);

            var expectedActive = new ActiveDocumentResponse { FilePath = "a.cs", Content = "content" };
            activeTool.Setup(s => s.ExecuteAsync(It.IsAny<CancellationToken>())).ReturnsAsync(expectedActive);

            var expectedLines = new FileLinesResponse { FilePath = "a.cs", Lines = new System.Collections.Generic.List<FileLineInfo>() };
            linesTool.Setup(s => s.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedLines);

            var expectedFindFilesResult = new FileSearchResultsResponse { Results = new List<FileSearchResult> { new FileSearchResult { FilePath = "config.cs" } }, NextPageToken = null, TotalFiles = 1, Success = true, ErrorMessage = null };
            findFilesTool.Setup(s => s.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedFindFilesResult);

            var expectedSolutionResult = new SolutionOverviewResponse { SolutionName = "Test", TotalProjects = 2, TotalFiles = 100 };
            solutionOverviewTool.Setup(s => s.ExecuteAsync(It.IsAny<CancellationToken>())).ReturnsAsync(expectedSolutionResult);

            var expectedSymbolResult = new SymbolReferencesResponse 
            { 
                Success = true,
                ErrorMessage = null,
                SymbolName = "TestSymbol", 
                Results = new System.Collections.Generic.List<FileReferencesGroup>
                {
                    new FileReferencesGroup
                    {
                        FilePath = "test.cs",
                        Matches = new System.Collections.Generic.List<ReferenceItem>
                        {
                            new ReferenceItem { LineNumber = 10, LineText = "var test = TestSymbol;" }
                        }
                    }
                },
                TotalReferences = 1,
                NextPageToken = null
            };
            findSymbolReferencesTool.Setup(s => s.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedSymbolResult);

            var factory = new BuiltInVsToolProvider(searchTool.Object, activeTool.Object, linesTool.Object, findFilesTool.Object, solutionOverviewTool.Object, findSymbolReferencesTool.Object, listDirTool.Object);

            var searchParams = new Dictionary<string, object> { { "query", "needle" }, { "extension_filter", ".cs" } };
            var searchRes = await factory.ExecuteAsync("search_in_files", searchParams, CancellationToken.None);
            Assert.That(searchRes, Is.SameAs(expectedSearchResult));

            var activeRes = await factory.ExecuteAsync("get_active_document", new Dictionary<string, object>(), CancellationToken.None);
            Assert.That(activeRes, Is.SameAs(expectedActive));

            var linesParams = new Dictionary<string, object> { { "file_path", "a.cs" }, { "start_line", 1 }, { "end_line", 2 } };
            var linesRes = await factory.ExecuteAsync("read_file_lines", linesParams, CancellationToken.None);
            Assert.That(linesRes, Is.SameAs(expectedLines));

            var findFilesParams = new Dictionary<string, object> { { "file_name", "config" }, { "file_extension", ".cs" } };
            var findFilesRes = await factory.ExecuteAsync("find_files_by_name", findFilesParams, CancellationToken.None);
            Assert.That(findFilesRes, Is.SameAs(expectedFindFilesResult));

            var solutionRes = await factory.ExecuteAsync("get_solution_overview", new Dictionary<string, object>(), CancellationToken.None);
            Assert.That(solutionRes, Is.SameAs(expectedSolutionResult));

            var symbolParams = new Dictionary<string, object> { { "symbol_name", "TestSymbol" } };
            var symbolRes = await factory.ExecuteAsync("find_symbol_references", symbolParams, CancellationToken.None);
            Assert.That(symbolRes, Is.SameAs(expectedSymbolResult));
        }
    }
}
