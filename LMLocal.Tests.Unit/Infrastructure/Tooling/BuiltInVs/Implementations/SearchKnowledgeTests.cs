using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    [TestFixture]
    public class SearchKnowledgeTests
    {
        private Mock<IVsDependencies> _vsMock;
        private Mock<IPathResolver> _pathResolverMock;
        private Mock<IFileSystem> _fileSystemMock;
        private Mock<ISettingsManager> _settingsManagerMock;
        private SearchResultCache _searchCache;
        private SearchKnowledge _tool;

        private const string SolutionDir = @"C:\solution";
        private const string DocsDir = @"C:\solution\docs";
        private const string DocPath = @"C:\solution\docs\ChatHistory.md";
        private const string RootMdPath = @"C:\solution\README.md";

        private static readonly DateTime DocDate = new DateTime(2024, 1, 5);
        private static readonly DateTime RootDate = new DateTime(2024, 2, 10);

        private const string ChatHistoryContent =
            "# ChatHistoryMigration\n\n" +
            "## ChatHistory Overview\n\n" +
            "The ChatHistory component implements the migration details.\n\n" +
            "## Storage\n\n" +
            "ChatHistory records are stored in a relational database.";

        [SetUp]
        public void SetUp()
        {
            _vsMock = new Mock<IVsDependencies>();
            _pathResolverMock = new Mock<IPathResolver>();
            _fileSystemMock = new Mock<IFileSystem>();
            _settingsManagerMock = new Mock<ISettingsManager>();
            _searchCache = new SearchResultCache();

            _settingsManagerMock.Setup(s => s.Current).Returns(new AppSettings { KnowledgeBasePaths = "./; ./docs" });

            _vsMock.Setup(v => v.IsSolutionOpen).Returns(true);
            _vsMock.Setup(v => v.GetSolutionDirectory()).Returns(SolutionDir);

            // Resolve "./" -> SolutionDir
            string resolvedRoot = SolutionDir;
            _pathResolverMock
                .Setup(p => p.TryResolveFilePath("./", SolutionDir, out resolvedRoot))
                .Returns(true);

            // Resolve "./docs" -> DocsDir
            string resolvedDocs = DocsDir;
            _pathResolverMock
                .Setup(p => p.TryResolveFilePath("./docs", SolutionDir, out resolvedDocs))
                .Returns(true);

            _fileSystemMock.Setup(f => f.DirectoryExists(SolutionDir)).Returns(true);
            _fileSystemMock.Setup(f => f.DirectoryExists(DocsDir)).Returns(true);

            _fileSystemMock.Setup(f => f.GetFiles(SolutionDir, "*.md")).Returns(new[] { RootMdPath });
            _fileSystemMock.Setup(f => f.GetFiles(DocsDir, "*.md")).Returns(new[] { DocPath });

            _fileSystemMock.Setup(f => f.GetFileInfo(DocPath)).Returns((100L, DocDate));
            _fileSystemMock.Setup(f => f.GetFileInfo(RootMdPath)).Returns((200L, RootDate));

            _fileSystemMock.Setup(f => f.ReadAllTextAsync(DocPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ChatHistoryContent);
            _fileSystemMock.Setup(f => f.ReadAllTextAsync(RootMdPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync("");

            // Relative path resolution for results.
            string relativeDocs = "docs/ChatHistory.md";
            _pathResolverMock
                .Setup(p => p.TryGetRelativePath(DocPath, SolutionDir, out relativeDocs))
                .Returns(true);
            string relativeRoot = "README.md";
            _pathResolverMock
                .Setup(p => p.TryGetRelativePath(RootMdPath, SolutionDir, out relativeRoot))
                .Returns(true);

            _tool = new SearchKnowledge(_vsMock.Object, _pathResolverMock.Object, _fileSystemMock.Object, _settingsManagerMock.Object, _searchCache);
        }

        [Test]
        public async Task GetToolInfo_ReturnsCorrectMetadata()
        {
            var info = _tool.GetToolInfo();
            Assert.That(info.Name, Is.EqualTo("search_solution_knowledge"));
            Assert.That(_tool.AccessLevel, Is.EqualTo(ToolAccessLevel.ReadOnly));
            Assert.That(info.Parameters.Properties, Contains.Key("query"));
            Assert.That(info.Parameters.Properties, Contains.Key("max_results"));
        }

        [Test]
        public async Task Execute_WithMatchingQuery_ReturnsResults()
        {
            var parameters = new Dictionary<string, object> { { "query", "ChatHistory" } };

            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(result, Is.InstanceOf<KnowledgeSearchResponse>());
            var resp = (KnowledgeSearchResponse)result;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.Results, Is.Not.Empty);
            Assert.That(resp.Results[0].FilePath, Is.EqualTo("docs/ChatHistory.md"));
            Assert.That(resp.Results[0].LineNumber, Is.EqualTo(3));
        }

        [Test]
        public async Task Execute_WithNoSolutionOpen_ReturnsError()
        {
            _vsMock.Setup(v => v.IsSolutionOpen).Returns(false);
            var parameters = new Dictionary<string, object> { { "query", "ChatHistory" } };

            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            var resp = (KnowledgeSearchResponse)result;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("solution"));
        }

        [Test]
        public async Task Execute_WithNoFiles_ReturnsEmptyNoFilesOutcome()
        {
            _fileSystemMock.Setup(f => f.GetFiles(SolutionDir, "*.md")).Returns(Array.Empty<string>());
            _fileSystemMock.Setup(f => f.GetFiles(DocsDir, "*.md")).Returns(Array.Empty<string>());

            var parameters = new Dictionary<string, object> { { "query", "ChatHistory" } };

            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            var resp = (KnowledgeSearchResponse)result;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.TotalMatches, Is.EqualTo(0));
            Assert.That(resp.Outcome, Is.EqualTo(KnowledgeSearchOutcome.NoFiles));
        }

        [Test]
        public async Task Execute_WithFilesButNoMatches_ReturnsNoMatchesOutcome()
        {
            _fileSystemMock.Setup(f => f.ReadAllTextAsync(DocPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync("# Title\n\nNothing here about SwiftUI.");
            _fileSystemMock.Setup(f => f.ReadAllTextAsync(RootMdPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync("");

            var parameters = new Dictionary<string, object> { { "query", "Flibbertygibbet" } };

            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            var resp = (KnowledgeSearchResponse)result;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.TotalMatches, Is.EqualTo(0));
            Assert.That(resp.Outcome, Is.EqualTo(KnowledgeSearchOutcome.NoMatches));
        }

        [Test]
        public async Task Execute_RanksByScoreDescending()
        {
            _fileSystemMock.Setup(f => f.ReadAllTextAsync(RootMdPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync("Some info about ChatHistory Project.\n");

            var parameters = new Dictionary<string, object> { { "query", "ChatHistory" } };

            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            var resp = (KnowledgeSearchResponse)result;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.Results.Count, Is.EqualTo(2));
            Assert.That(resp.Results[0].FilePath, Is.EqualTo("docs/ChatHistory.md"));
        }

        [Test]
        public async Task Execute_RespectsMaxResults()
        {
            _fileSystemMock.Setup(f => f.ReadAllTextAsync(RootMdPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync("ChatHistory Project info.\n");

            var parameters = new Dictionary<string, object> { { "query", "ChatHistory" }, { "max_results", 1 } };

            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            var resp = (KnowledgeSearchResponse)result;
            Assert.That(resp.Results.Count, Is.EqualTo(1));
            Assert.That(resp.TotalMatches, Is.EqualTo(2));
        }

        [Test]
        public async Task Execute_WithNullParameters_ReturnsError()
        {
            var result = await _tool.ExecuteAsync(null, CancellationToken.None);

            var resp = (KnowledgeSearchResponse)result;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("Parameters"));
        }

        [Test]
        public async Task Execute_WithEmptyQuery_ReturnsError()
        {
            var parameters = new Dictionary<string, object> { { "query", "" } };

            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            var resp = (KnowledgeSearchResponse)result;
            Assert.That(resp.Success, Is.False);
            Assert.That(resp.ErrorMessage, Does.Contain("query"));
        }

        [Test]
        public void GetCompletionMessage_ForSuccess_ReturnsCount()
        {
            var resp = new KnowledgeSearchResponse
            {
                Success = true,
                Outcome = KnowledgeSearchOutcome.Success,
                TotalMatches = 3,
                Results = new List<KnowledgeMatchResult>()
            };

            var msg = _tool.GetCompletionMessage(resp);
            Assert.That(msg, Does.Contain("Found 3"));
        }

        [Test]
        public void GetCompletionMessage_ForNoMatches_ReturnsNoOccurrences()
        {
            var resp = new KnowledgeSearchResponse
            {
                Success = true,
                Outcome = KnowledgeSearchOutcome.NoMatches,
                TotalMatches = 0,
                Results = new List<KnowledgeMatchResult>()
            };

            var msg = _tool.GetCompletionMessage(resp);
            Assert.That(msg, Does.Contain("No occurrences"));
        }

        [Test]
        public void GetProcessingMessage_ContainsQuery()
        {
            var parameters = new Dictionary<string, object> { { "query", "migration" } };
            var msg = _tool.GetProcessingMessage(parameters);
            Assert.That(msg, Does.Contain("migration"));
        }

        [Test]
        public async Task Execute_WithEmptyKnowledgeBasePaths_ReturnsNoFiles()
        {
            _settingsManagerMock
                .Setup(s => s.Current)
                .Returns(new AppSettings { KnowledgeBasePaths = "" });

            var parameters = new Dictionary<string, object> { { "query", "ChatHistory" } };

            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            var resp = (KnowledgeSearchResponse)result;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.TotalMatches, Is.EqualTo(0));
            Assert.That(resp.Outcome, Is.EqualTo(KnowledgeSearchOutcome.NoFiles));
        }

        [Test]
        public async Task Execute_WithWhitespaceKnowledgeBasePaths_ReturnsNoFiles()
        {
            _settingsManagerMock
                .Setup(s => s.Current)
                .Returns(new AppSettings { KnowledgeBasePaths = "   " });

            var parameters = new Dictionary<string, object> { { "query", "ChatHistory" } };

            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            var resp = (KnowledgeSearchResponse)result;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.TotalMatches, Is.EqualTo(0));
            Assert.That(resp.Outcome, Is.EqualTo(KnowledgeSearchOutcome.NoFiles));
        }

        [Test]
        public async Task Execute_TrimsAndSkipsEmptySegments_WhenParsingPaths()
        {
            _settingsManagerMock
                .Setup(s => s.Current)
                .Returns(new AppSettings { KnowledgeBasePaths = " ./;  ; ./docs " });

            var parameters = new Dictionary<string, object> { { "query", "ChatHistory" } };

            var result = await _tool.ExecuteAsync(parameters, CancellationToken.None);

            var resp = (KnowledgeSearchResponse)result;
            Assert.That(resp.Success, Is.True);
            Assert.That(resp.TotalMatches, Is.GreaterThan(0));
        }

        [Test]
        public async Task Execute_CachesFileEnumerationAcrossCalls()
        {
            var parameters = new Dictionary<string, object> { { "query", "ChatHistory" } };

            await _tool.ExecuteAsync(parameters, CancellationToken.None);
            await _tool.ExecuteAsync(parameters, CancellationToken.None);

            _fileSystemMock.Verify(f => f.GetFiles(SolutionDir, "*.md"), Times.Once);
            _fileSystemMock.Verify(f => f.GetFiles(DocsDir, "*.md"), Times.Once);
        }

        [Test]
        public async Task Execute_SearchCacheClear_RescansFiles()
        {
            var parameters = new Dictionary<string, object> { { "query", "ChatHistory" } };

            await _tool.ExecuteAsync(parameters, CancellationToken.None);
            _searchCache.Clear();
            await _tool.ExecuteAsync(parameters, CancellationToken.None);

            _fileSystemMock.Verify(f => f.GetFiles(SolutionDir, "*.md"), Times.Exactly(2));
            _fileSystemMock.Verify(f => f.GetFiles(DocsDir, "*.md"), Times.Exactly(2));
        }
    }
}
