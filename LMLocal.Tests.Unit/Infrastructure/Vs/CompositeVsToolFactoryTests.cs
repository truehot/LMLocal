using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Mcp;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Vs
{
    [TestFixture]
    public class CompositeVsToolFactoryTests
    {
        private Mock<IBuiltInVsToolProvider> _builtInFactoryMock;
        private Mock<IMcpToolManager> _mcpToolManagerMock;
        private Mock<ISettingsManager> _settingsManagerMock;
        private CompositeToolFactory _compositeFactory;

        [SetUp]
        public void SetUp()
        {
            _builtInFactoryMock = new Mock<IBuiltInVsToolProvider>();
            _mcpToolManagerMock = new Mock<IMcpToolManager>();
            _settingsManagerMock = new Mock<ISettingsManager>();

            // By default, AI tools are enabled
            var settings = new AppSettings { EnableAiTools = true };
            _settingsManagerMock.SetupGet(s => s.Current).Returns(settings);

            _compositeFactory = new CompositeToolFactory(
                _builtInFactoryMock.Object, 
                _mcpToolManagerMock.Object,
                _settingsManagerMock.Object);
        }

        [Test]
        public void GetAllToolDefinitions_CombinesBuiltInAndMcpTools()
        {
            var builtInDef = new ToolDefinition { Name = "search_in_files" };
            var mcpDef = new ToolDefinition { Name = "external_tool" };

            _builtInFactoryMock.Setup(f => f.GetAllToolDefinitions())
                .Returns(new List<ToolDefinition> { builtInDef }.AsReadOnly());
            _mcpToolManagerMock.Setup(m => m.GetMcpToolDefinitions())
                .Returns(new List<ToolDefinition> { mcpDef }.AsReadOnly());

            var result = _compositeFactory.GetAllToolDefinitions();

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].Name, Is.EqualTo("search_in_files"));
            Assert.That(result[1].Name, Is.EqualTo("external_tool"));
        }

        [Test]
        public void ToolExists_ChecksBuiltInFirst()
        {
            _builtInFactoryMock.Setup(f => f.ToolExists("search_in_files")).Returns(true);
            _mcpToolManagerMock.Setup(m => m.ToolExists("search_in_files")).Returns(false);

            var exists = _compositeFactory.ToolExists("search_in_files");

            Assert.That(exists, Is.True);
            _builtInFactoryMock.Verify(f => f.ToolExists("search_in_files"), Times.Once);
        }

        [Test]
        public void ToolExists_ChecksMcpIfNotInBuiltIn()
        {
            _builtInFactoryMock.Setup(f => f.ToolExists("external_tool")).Returns(false);
            _mcpToolManagerMock.Setup(m => m.ToolExists("external_tool")).Returns(true);

            var exists = _compositeFactory.ToolExists("external_tool");

            Assert.That(exists, Is.True);
        }

        [Test]
        public void GetTool_ReturnsBuiltInTool()
        {
            var toolMock = new Mock<ITool>();
            _builtInFactoryMock.Setup(f => f.ToolExists("search_in_files")).Returns(true);
            _builtInFactoryMock.Setup(f => f.GetTool("search_in_files")).Returns(toolMock.Object);

            var tool = _compositeFactory.GetTool("search_in_files");

            Assert.That(tool, Is.SameAs(toolMock.Object));
        }

        [Test]
        public void GetTool_ReturnsMcpTool()
        {
            var mcpToolMock = new Mock<ITool>();
            mcpToolMock.Setup(t => t.ToolName).Returns("external_tool");

            _builtInFactoryMock.Setup(f => f.ToolExists("external_tool")).Returns(false);
            _mcpToolManagerMock.Setup(m => m.ToolExists("external_tool")).Returns(true);

            var expectedMcpTool = new McpDynamicTool(
                "server1",
                new ToolDefinition { Name = "external_tool" },
                async (p, ct) => { await Task.Delay(0, ct); return "result"; });

            _mcpToolManagerMock.Setup(m => m.GetTool("external_tool"))
                .Returns(expectedMcpTool);

            var tool = _compositeFactory.GetTool("external_tool");

            Assert.That(tool, Is.SameAs(expectedMcpTool));
        }

        [Test]
        public void GetTool_ThrowsIfToolNotFound()
        {
            _builtInFactoryMock.Setup(f => f.ToolExists("unknown")).Returns(false);
            _mcpToolManagerMock.Setup(m => m.ToolExists("unknown")).Returns(false);

            Assert.Throws<ArgumentException>(() => _compositeFactory.GetTool("unknown"));
        }

        [Test]
        public async Task ExecuteAsync_ExecutesBuiltInTool()
        {
            var expectedResult = "built_in_result";
            _builtInFactoryMock.Setup(f => f.ToolExists("search_in_files")).Returns(true);
            _builtInFactoryMock.Setup(f => f.ExecuteAsync("search_in_files", It.IsAny<IServiceProvider>(), 
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);

            var result = await _compositeFactory.ExecuteAsync("search_in_files", null, new Dictionary<string, object>(), CancellationToken.None);

            Assert.That(result, Is.EqualTo(expectedResult));
        }

        [Test]
        public async Task ExecuteAsync_ExecutesMcpTool()
        {
            var expectedResult = "mcp_result";

            var expectedMcpTool = new McpDynamicTool(
                "server1",
                new ToolDefinition { Name = "external_tool" },
                async (p, ct) => { await Task.Delay(0, ct); return expectedResult; });

            _builtInFactoryMock.Setup(f => f.ToolExists("external_tool")).Returns(false);
            _mcpToolManagerMock.Setup(m => m.ToolExists("external_tool")).Returns(true);
            _mcpToolManagerMock.Setup(m => m.GetTool("external_tool"))
                .Returns(expectedMcpTool);

            var result = await _compositeFactory.ExecuteAsync("external_tool", null, new Dictionary<string, object>(), CancellationToken.None);

            Assert.That(result, Is.EqualTo(expectedResult));
        }

        [Test]
        public void GetProcessingMessage_UsesBuiltInFactory()
        {
            _builtInFactoryMock.Setup(f => f.ToolExists("search_in_files")).Returns(true);
            _builtInFactoryMock.Setup(f => f.GetProcessingMessage("search_in_files", It.IsAny<Dictionary<string, object>>()))
                .Returns("Searching...");

            var message = _compositeFactory.GetProcessingMessage("search_in_files", new Dictionary<string, object>());

            Assert.That(message, Is.EqualTo("Searching..."));
        }

        [Test]
        public void GetProcessingMessage_UsesMcpGenericMessage()
        {
            _builtInFactoryMock.Setup(f => f.ToolExists("external_tool")).Returns(false);
            _mcpToolManagerMock.Setup(m => m.ToolExists("external_tool")).Returns(true);

            var message = _compositeFactory.GetProcessingMessage("external_tool", new Dictionary<string, object>());

            Assert.That(message, Is.EqualTo("Executing tool 'external_tool'..."));
        }

        [Test]
        public void GetCompletionMessage_UsesBuiltInFactory()
        {
            _builtInFactoryMock.Setup(f => f.ToolExists("search_in_files")).Returns(true);
            _builtInFactoryMock.Setup(f => f.GetCompletionMessage("search_in_files", It.IsAny<object>()))
                .Returns("Search completed.");

            var message = _compositeFactory.GetCompletionMessage("search_in_files", null);

            Assert.That(message, Is.EqualTo("Search completed."));
        }

        [Test]
        public void GetCompletionMessage_UsesMcpGenericMessage()
        {
            _builtInFactoryMock.Setup(f => f.ToolExists("external_tool")).Returns(false);
            _mcpToolManagerMock.Setup(m => m.ToolExists("external_tool")).Returns(true);

            var message = _compositeFactory.GetCompletionMessage("external_tool", null);

            Assert.That(message, Is.EqualTo("Tool 'external_tool' execution completed."));
        }

        [Test]
        public void GetAllToolDefinitions_ExcludesBuiltInWhenDisabled()
        {
            // Disable built-in tools
            var settings = new AppSettings { EnableAiTools = false };
            _settingsManagerMock.SetupGet(s => s.Current).Returns(settings);

            var builtInDef = new ToolDefinition { Name = "search_in_files" };
            var mcpDef = new ToolDefinition { Name = "external_tool" };

            _builtInFactoryMock.Setup(f => f.GetAllToolDefinitions())
                .Returns(new List<ToolDefinition> { builtInDef }.AsReadOnly());
            _mcpToolManagerMock.Setup(m => m.GetMcpToolDefinitions())
                .Returns(new List<ToolDefinition> { mcpDef }.AsReadOnly());

            var result = _compositeFactory.GetAllToolDefinitions();

            // Should only have MCP tool, not built-in
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0].Name, Is.EqualTo("external_tool"));
        }

        [Test]
        public void ToolExists_ExcludesBuiltInWhenDisabled()
        {
            // Disable built-in tools
            var settings = new AppSettings { EnableAiTools = false };
            _settingsManagerMock.SetupGet(s => s.Current).Returns(settings);

            _builtInFactoryMock.Setup(f => f.ToolExists("search_in_files")).Returns(true);
            _mcpToolManagerMock.Setup(m => m.ToolExists("search_in_files")).Returns(false);

            var exists = _compositeFactory.ToolExists("search_in_files");

            Assert.That(exists, Is.False);
        }

        [Test]
        public void ToolExists_FindsMcpWhenBuiltInDisabled()
        {
            // Disable built-in tools
            var settings = new AppSettings { EnableAiTools = false };
            _settingsManagerMock.SetupGet(s => s.Current).Returns(settings);

            _builtInFactoryMock.Setup(f => f.ToolExists("external_tool")).Returns(false);
            _mcpToolManagerMock.Setup(m => m.ToolExists("external_tool")).Returns(true);

            var exists = _compositeFactory.ToolExists("external_tool");

            Assert.That(exists, Is.True);
        }

        [Test]
        public void GetTool_ThrowsForBuiltInWhenDisabled()
        {
            // Disable built-in tools
            var settings = new AppSettings { EnableAiTools = false };
            _settingsManagerMock.SetupGet(s => s.Current).Returns(settings);

            _builtInFactoryMock.Setup(f => f.ToolExists("search_in_files")).Returns(true);
            _mcpToolManagerMock.Setup(m => m.ToolExists("search_in_files")).Returns(false);

            Assert.Throws<ArgumentException>(() => _compositeFactory.GetTool("search_in_files"));
        }

        [Test]
        public void ExecuteAsync_ThrowsForBuiltInWhenDisabled()
        {
            // Disable built-in tools
            var settings = new AppSettings { EnableAiTools = false };
            _settingsManagerMock.SetupGet(s => s.Current).Returns(settings);

            _builtInFactoryMock.Setup(f => f.ToolExists("search_in_files")).Returns(true);
            _mcpToolManagerMock.Setup(m => m.ToolExists("search_in_files")).Returns(false);

            Assert.ThrowsAsync<ArgumentException>(
                () => _compositeFactory.ExecuteAsync("search_in_files", null, new Dictionary<string, object>(), CancellationToken.None));
        }
    }
}
