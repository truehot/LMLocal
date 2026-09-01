using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.SubAgents;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Infrastructure.Tooling.Mcp;
using LMLocal.Infrastructure.Tooling.Mcp.Abstractions;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Vs
{
    [TestFixture]
    public class ToolRouterTests
    {
        private Mock<IBuiltInVsToolProvider> _builtInFactoryMock;
        private Mock<IMcpToolManager> _mcpToolManagerMock;
        private Mock<ISettingsManager> _settingsManagerMock;
        private Mock<LMLocal.Infrastructure.SubAgents.ISubAgentsToolSource> _subAgentsToolSourceMock;
        private ToolRouter _router;

        [SetUp]
        public void SetUp()
        {
            _builtInFactoryMock = new Mock<IBuiltInVsToolProvider>();
            _mcpToolManagerMock = new Mock<IMcpToolManager>();
            _settingsManagerMock = new Mock<ISettingsManager>();
            _subAgentsToolSourceMock = new Mock<LMLocal.Infrastructure.SubAgents.ISubAgentsToolSource>();

            // By default, AI tools are enabled
            var settings = new AppSettings { EnableAiTools = true };
            _settingsManagerMock.SetupGet(s => s.Current).Returns(settings);

            _router = new ToolRouter(
                _builtInFactoryMock.Object,
                _mcpToolManagerMock.Object,
                _settingsManagerMock.Object,
                _subAgentsToolSourceMock.Object);
        }

        [Test]
        public void ToolExists_ChecksBuiltInFirst()
        {
            _builtInFactoryMock.Setup(f => f.ToolExists("search_in_files")).Returns(true);
            _mcpToolManagerMock.Setup(m => m.ToolExists("search_in_files")).Returns(false);

            var exists = _router.ToolExists("search_in_files");

            Assert.That(exists, Is.True);
            _builtInFactoryMock.Verify(f => f.ToolExists("search_in_files"), Times.Once);
        }

        [Test]
        public void ToolExists_ChecksMcpIfNotInBuiltIn()
        {
            _builtInFactoryMock.Setup(f => f.ToolExists("external_tool")).Returns(false);
            _mcpToolManagerMock.Setup(m => m.ToolExists("external_tool")).Returns(true);

            var exists = _router.ToolExists("external_tool");

            Assert.That(exists, Is.True);
        }

        [Test]
        public async Task ExecuteAsync_ExecutesBuiltInTool()
        {
            var expectedResult = "built_in_result";
            _builtInFactoryMock.Setup(f => f.ToolExists("search_in_files")).Returns(true);
            _builtInFactoryMock.Setup(f => f.ExecuteAsync("search_in_files",
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);

            var result = await _router.ExecuteAsync("search_in_files", new Dictionary<string, object>(), CancellationToken.None);

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

            var result = await _router.ExecuteAsync("external_tool", new Dictionary<string, object>(), CancellationToken.None);

            Assert.That(result, Is.EqualTo(expectedResult));
        }

        [Test]
        public void GetProcessingMessage_UsesBuiltInFactory()
        {
            _builtInFactoryMock.Setup(f => f.ToolExists("search_in_files")).Returns(true);
            _builtInFactoryMock.Setup(f => f.GetProcessingMessage("search_in_files", It.IsAny<Dictionary<string, object>>()))
                .Returns("Searching...");

            var message = _router.GetProcessingMessage("search_in_files", new Dictionary<string, object>());

            Assert.That(message, Is.EqualTo("Searching..."));
        }

        [Test]
        public void GetProcessingMessage_UsesMcpGenericMessage()
        {
            _builtInFactoryMock.Setup(f => f.ToolExists("external_tool")).Returns(false);
            _mcpToolManagerMock.Setup(m => m.ToolExists("external_tool")).Returns(true);

            var message = _router.GetProcessingMessage("external_tool", new Dictionary<string, object>());

            Assert.That(message, Is.EqualTo("Executing tool 'external_tool'..."));
        }

        [Test]
        public void GetProcessingMessage_UsesSubAgentDisplayName()
        {
            _builtInFactoryMock.Setup(f => f.ToolExists("researcher")).Returns(false);
            _mcpToolManagerMock.Setup(m => m.ToolExists("researcher")).Returns(false);
            _subAgentsToolSourceMock.Setup(s => s.ToolExists("researcher")).Returns(true);
            _subAgentsToolSourceMock.Setup(s => s.GetDisplayName("researcher")).Returns("Researcher");

            var message = _router.GetProcessingMessage("researcher", new Dictionary<string, object>());

            Assert.That(message, Is.EqualTo("Running Researcher ..."));
        }

        [Test]
        public void GetCompletionMessage_UsesBuiltInFactory()
        {
            _builtInFactoryMock.Setup(f => f.ToolExists("search_in_files")).Returns(true);
            _builtInFactoryMock.Setup(f => f.GetCompletionMessage("search_in_files", It.IsAny<object>()))
                .Returns("Search completed.");

            var message = _router.GetCompletionMessage("search_in_files", null);

            Assert.That(message, Is.EqualTo("Search completed."));
        }

        [Test]
        public void GetCompletionMessage_UsesMcpGenericMessage()
        {
            _builtInFactoryMock.Setup(f => f.ToolExists("external_tool")).Returns(false);
            _mcpToolManagerMock.Setup(m => m.ToolExists("external_tool")).Returns(true);

            var message = _router.GetCompletionMessage("external_tool", null);

            Assert.That(message, Is.EqualTo("Tool 'external_tool' execution completed."));
        }

        [Test]
        public void ToolExists_ExcludesBuiltInWhenDisabled()
        {
            // Disable built-in tools
            var settings = new AppSettings { EnableAiTools = false };
            _settingsManagerMock.SetupGet(s => s.Current).Returns(settings);

            _builtInFactoryMock.Setup(f => f.ToolExists("search_in_files")).Returns(true);
            _mcpToolManagerMock.Setup(m => m.ToolExists("search_in_files")).Returns(false);

            var exists = _router.ToolExists("search_in_files");

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

            var exists = _router.ToolExists("external_tool");

            Assert.That(exists, Is.True);
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
                () => _router.ExecuteAsync("search_in_files", new Dictionary<string, object>(), CancellationToken.None));
        }

        [Test]
        public void ToolExists_ChecksSubAgentsAfterMcp()
        {
            _subAgentsToolSourceMock.Setup(s => s.ToolExists("researcher")).Returns(true);

            Assert.That(_router.ToolExists("researcher"), Is.True);
        }

        [Test]
        public void ExecuteAsync_ExecutesSubAgent()
        {
            var expectedResult = new SubAgentsRunResponse { Success = true, Content = "done" };

            _subAgentsToolSourceMock.Setup(s => s.ToolExists("researcher")).Returns(true);
            _subAgentsToolSourceMock.Setup(s => s.ExecuteAsync("researcher", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedResult);

            var result = _router.ExecuteAsync("researcher", new Dictionary<string, object>(), CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result, Is.SameAs(expectedResult));
        }

        [Test]
        public void GetCompletionMessage_UsesSubAgent()
        {
            _subAgentsToolSourceMock.Setup(s => s.ToolExists("researcher")).Returns(true);
            _subAgentsToolSourceMock.Setup(s => s.GetToolTimeout("researcher")).Returns((TimeSpan?)null);
            _subAgentsToolSourceMock.Setup(s => s.GetDisplayName("researcher")).Returns("Researcher");

            var message = _router.GetCompletionMessage(
                "researcher",
                new SubAgentsRunResponse
                {
                    Success = true,
                    Content = "ok",
                    Rounds = 3,
                    TotalTokens = 1400,
                    DurationMs = 2100
                });

            Assert.That(message, Does.Contain("Done (3 steps, 1.4k tokens, 2.1s)"));
        }

        [Test]
        public void GetToolTimeout_ForSubAgent_ReturnsSourceTimeout()
        {
            var expected = TimeSpan.FromSeconds(125);
            _subAgentsToolSourceMock.Setup(s => s.ToolExists("researcher")).Returns(true);
            _subAgentsToolSourceMock.Setup(s => s.GetToolTimeout("researcher")).Returns(expected);

            var result = _router.GetToolTimeout("researcher");

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void GetToolTimeout_ForBuiltIn_ReturnsNull()
        {
            _builtInFactoryMock.Setup(f => f.ToolExists("search_in_files")).Returns(true);

            var result = _router.GetToolTimeout("search_in_files");

            Assert.That(result, Is.Null);
        }
    }
}
