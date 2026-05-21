using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Vs;
using LMLocal.Infrastructure.Vs.Implementations;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Vs
{
    [TestFixture]
    public class VsToolFactoryTests
    {
        [Test]
        public void GetAllToolDefinitions_ReturnsDefinitionsFromTools()
        {
            var searchTool = new Mock<ISolutionSearchTool>();
            var activeTool = new Mock<IActiveDocumentTool>();
            var linesTool = new Mock<IFileLinesReaderTool>();
            var findFilesTool = new Mock<IFindFilesByNameTool>();
            var solutionOverviewTool = new Mock<IGetSolutionOverviewTool>();
            var findSymbolReferencesTool = new Mock<IFindSymbolReferencesTool>();

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

            var factory = new VsToolFactory(searchTool.Object, activeTool.Object, linesTool.Object, findFilesTool.Object, solutionOverviewTool.Object, findSymbolReferencesTool.Object);

            var defs = factory.GetAllToolDefinitions();

            Assert.That(defs, Is.Not.Null);
            Assert.That(defs.Count, Is.EqualTo(6));
            Assert.That(defs[0].Name, Is.EqualTo("search_in_files"));
            Assert.That(defs[1].Name, Is.EqualTo("get_active_document"));
            Assert.That(defs[2].Name, Is.EqualTo("read_file_lines"));
            Assert.That(defs[3].Name, Is.EqualTo("find_files_by_name"));
            Assert.That(defs[4].Name, Is.EqualTo("get_solution_overview"));
            Assert.That(defs[5].Name, Is.EqualTo("find_symbol_references"));
        }

        [Test]
        public void GetTool_ReturnsCorrectToolOrThrows()
        {
            var searchTool = new Mock<ISolutionSearchTool>();
            var activeTool = new Mock<IActiveDocumentTool>();
            var linesTool = new Mock<IFileLinesReaderTool>();
            var findFilesTool = new Mock<IFindFilesByNameTool>();
            var solutionOverviewTool = new Mock<IGetSolutionOverviewTool>();
            var findSymbolReferencesTool = new Mock<IFindSymbolReferencesTool>();

            searchTool.SetupGet(s => s.ToolName).Returns("search_in_files");
            activeTool.SetupGet(s => s.ToolName).Returns("get_active_document");
            linesTool.SetupGet(s => s.ToolName).Returns("read_file_lines");
            findFilesTool.SetupGet(s => s.ToolName).Returns("find_files_by_name");
            solutionOverviewTool.SetupGet(s => s.ToolName).Returns("get_solution_overview");
            findSymbolReferencesTool.SetupGet(s => s.ToolName).Returns("find_symbol_references");

            var factory = new VsToolFactory(searchTool.Object, activeTool.Object, linesTool.Object, findFilesTool.Object, solutionOverviewTool.Object, findSymbolReferencesTool.Object);

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

            Assert.Throws<ArgumentException>(() => factory.GetTool("nonexistent_tool"));
        }

        [Test]
        public async Task ExecuteAsync_DispatchesToCorrectTool()
        {
            var searchTool = new Mock<ISolutionSearchTool>();
            var activeTool = new Mock<IActiveDocumentTool>();
            var linesTool = new Mock<IFileLinesReaderTool>();
            var findFilesTool = new Mock<IFindFilesByNameTool>();
            var solutionOverviewTool = new Mock<IGetSolutionOverviewTool>();
            var findSymbolReferencesTool = new Mock<IFindSymbolReferencesTool>();

            searchTool.SetupGet(s => s.ToolName).Returns("search_in_files");
            activeTool.SetupGet(s => s.ToolName).Returns("get_active_document");
            linesTool.SetupGet(s => s.ToolName).Returns("read_file_lines");
            findFilesTool.SetupGet(s => s.ToolName).Returns("find_files_by_name");
            solutionOverviewTool.SetupGet(s => s.ToolName).Returns("get_solution_overview");
            findSymbolReferencesTool.SetupGet(s => s.ToolName).Returns("find_symbol_references");

            var expectedSearchResult = new List<LMLocal.Infrastructure.Vs.Implementations.SearchResult> { new LMLocal.Infrastructure.Vs.Implementations.SearchResult { FilePath = "a.cs", Matches = new System.Collections.Generic.List<LMLocal.Infrastructure.Vs.Implementations.SearchMatch> { new LMLocal.Infrastructure.Vs.Implementations.SearchMatch { LineNumber = 1, LineText = "x" } }, MatchCount = 1 } };
            searchTool.Setup(s => s.ExecuteAsync(It.IsAny<IServiceProvider>(), "needle", ".cs", It.IsAny<CancellationToken>(), null)).ReturnsAsync(expectedSearchResult);

            var expectedActive = new LMLocal.Infrastructure.Vs.Implementations.ActiveDocumentResponse { FilePath = "a.cs", Content = "content" };
            activeTool.Setup(s => s.ExecuteAsync(It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedActive);

            var expectedLines = new LMLocal.Infrastructure.Vs.Implementations.FileLinesResponse { FilePath = "a.cs", Lines = new System.Collections.Generic.List<LMLocal.Infrastructure.Vs.Implementations.FileLineInfo>() };
            linesTool.Setup(s => s.ExecuteAsync(It.IsAny<IServiceProvider>(), "a.cs", 1, 2, It.IsAny<CancellationToken>())).ReturnsAsync(expectedLines);

            var expectedFindFilesResult = new List<LMLocal.Infrastructure.Vs.Implementations.FileSearchResult> { new LMLocal.Infrastructure.Vs.Implementations.FileSearchResult { FilePath = "config.cs" } };
            findFilesTool.Setup(s => s.ExecuteAsync(It.IsAny<IServiceProvider>(), "config", ".cs", It.IsAny<CancellationToken>(), null)).ReturnsAsync(expectedFindFilesResult);

            var expectedSolutionResult = new LMLocal.Infrastructure.Vs.Implementations.SolutionOverviewResponse { SolutionName = "Test", TotalProjects = 2, TotalFiles = 100 };
            solutionOverviewTool.Setup(s => s.ExecuteAsync(It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedSolutionResult);

            var expectedSymbolResult = new LMLocal.Infrastructure.Vs.Implementations.SymbolReferencesResponse 
            { 
                SymbolName = "TestSymbol", 
                Results = new System.Collections.Generic.List<LMLocal.Infrastructure.Vs.Implementations.FileReferencesGroup>
                {
                    new LMLocal.Infrastructure.Vs.Implementations.FileReferencesGroup
                    {
                        FilePath = "test.cs",
                        Matches = new System.Collections.Generic.List<LMLocal.Infrastructure.Vs.Implementations.ReferenceItem>
                        {
                            new LMLocal.Infrastructure.Vs.Implementations.ReferenceItem { LineNumber = 10, LineText = "var test = TestSymbol;" }
                        }
                    }
                },
                TotalReferences = 1,
                HasMoreResults = false
            };
            findSymbolReferencesTool.Setup(s => s.ExecuteAsync(It.IsAny<IServiceProvider>(), "TestSymbol", It.IsAny<CancellationToken>())).ReturnsAsync(expectedSymbolResult);

            var factory = new VsToolFactory(searchTool.Object, activeTool.Object, linesTool.Object, findFilesTool.Object, solutionOverviewTool.Object, findSymbolReferencesTool.Object);

            var searchParams = new Dictionary<string, object> { { "query", "needle" }, { "extension_filter", ".cs" } };
            var searchRes = await factory.ExecuteAsync("search_in_files", null, searchParams, CancellationToken.None);
            Assert.That(searchRes, Is.SameAs(expectedSearchResult));

            var activeRes = await factory.ExecuteAsync("get_active_document", null, new Dictionary<string, object>(), CancellationToken.None);
            Assert.That(activeRes, Is.SameAs(expectedActive));

            var linesParams = new Dictionary<string, object> { { "file_path", "a.cs" }, { "start_line", 1 }, { "end_line", 2 } };
            var linesRes = await factory.ExecuteAsync("read_file_lines", null, linesParams, CancellationToken.None);
            Assert.That(linesRes, Is.SameAs(expectedLines));

            var findFilesParams = new Dictionary<string, object> { { "file_name", "config" }, { "file_extension", ".cs" } };
            var findFilesRes = await factory.ExecuteAsync("find_files_by_name", null, findFilesParams, CancellationToken.None);
            Assert.That(findFilesRes, Is.SameAs(expectedFindFilesResult));

            var solutionRes = await factory.ExecuteAsync("get_solution_overview", null, new Dictionary<string, object>(), CancellationToken.None);
            Assert.That(solutionRes, Is.SameAs(expectedSolutionResult));

            var symbolParams = new Dictionary<string, object> { { "symbol_name", "TestSymbol" } };
            var symbolRes = await factory.ExecuteAsync("find_symbol_references", null, symbolParams, CancellationToken.None);
            Assert.That(symbolRes, Is.SameAs(expectedSymbolResult));
        }
    }
}
