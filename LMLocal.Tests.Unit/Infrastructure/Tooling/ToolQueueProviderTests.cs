using System;
using System.Collections.Generic;
using System.Linq;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.SubAgents;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Infrastructure.Tooling.Mcp.Abstractions;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling
{
    [TestFixture]
    public class ToolQueueProviderTests
    {
        private Mock<ISettingsManager> _settingsManagerMock;
        private Mock<IBuiltInVsToolProvider> _builtInMock;
        private Mock<IMcpToolManager> _mcpToolManagerMock;
        private Mock<ISubAgentsCatalog> _catalogMock;
        private ToolQueueProvider _provider;

        private static SubAgentsConfig Config(params SubAgentDefinition[] agents)
        {
            var cfg = new SubAgentsConfig();
            cfg.Agents.AddRange(agents);
            return cfg;
        }

        private static SubAgentDefinition Agent(string id, List<string> allowedTools, bool enabled = true)
        {
            return new SubAgentDefinition
            {
                Id = id,
                Description = $"Agent {id}",
                CustomBaseUrl = "http://localhost:1234",
                Model = "m",
                Enabled = enabled,
                AllowedTools = allowedTools
            };
        }

        [SetUp]
        public void SetUp()
        {
            _settingsManagerMock = new Mock<ISettingsManager>();
            _builtInMock = new Mock<IBuiltInVsToolProvider>();
            _mcpToolManagerMock = new Mock<IMcpToolManager>();
            _catalogMock = new Mock<ISubAgentsCatalog>();

            _settingsManagerMock.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = true, EnableSubAgents = true, EnableAiWriteTools = false });
            _mcpToolManagerMock.Setup(m => m.GetMcpToolDefinitions())
                .Returns(new List<ToolDefinition>().AsReadOnly());

            _provider = new ToolQueueProvider(
                _settingsManagerMock.Object,
                _builtInMock.Object,
                _mcpToolManagerMock.Object,
                _catalogMock.Object);
        }

        private void SetBuiltIn(params ToolDefinition[] tools)
        {
            _builtInMock.Setup(b => b.GetAllToolDefinitions())
                .Returns(tools.ToList().AsReadOnly());
            foreach (var t in tools)
            {
                _builtInMock.Setup(b => b.GetToolAccessLevel(t.Name))
                    .Returns(ToolAccessLevel.ReadOnly);
            }
        }

        private void SetSnapshot(SubAgentsConfig config)
        {
            _catalogMock.Setup(c => c.TryGetSnapshot()).Returns(config);
        }

        // =====================================================================
        // GetMainQueue
        // =====================================================================

        [Test]
        public void GetMainQueue_WhenSubAgentsDisabled_IncludesBuiltInAndMcp()
        {
            _settingsManagerMock.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = true, EnableSubAgents = false });
            SetBuiltIn(new ToolDefinition { Name = "read_file_lines" });
            _mcpToolManagerMock.Setup(m => m.GetMcpToolDefinitions())
                .Returns(new List<ToolDefinition> { new ToolDefinition { Name = "mcp_tool" } }.AsReadOnly());

            var names = _provider.GetMainQueue().Definitions.Select(d => d.Name);

            Assert.That(names, Is.EquivalentTo(new[] { "read_file_lines", "mcp_tool" }));
        }

        [Test]
        public void GetMainQueue_WhenAiToolsDisabled_ReturnsEmpty()
        {
            _settingsManagerMock.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = false, EnableSubAgents = false });

            Assert.That(_provider.GetMainQueue().Definitions, Is.Empty);
        }

        [Test]
        public void GetMainQueue_ReasoningOnlyAgent_AlwaysVisible()
        {
            SetBuiltIn(new ToolDefinition { Name = "read_file_lines" });
            SetSnapshot(Config(Agent("reasoner", new List<string>())));

            var names = _provider.GetMainQueue().Definitions.Select(d => d.Name);

            Assert.That(names, Is.EquivalentTo(new[] { "read_file_lines", "reasoner" }));
        }

        [Test]
        public void GetMainQueue_Ownership_HidesOwnedBuiltIn()
        {
            SetBuiltIn(
                new ToolDefinition { Name = "read_file_lines" },
                new ToolDefinition { Name = "search_file_content" });
            SetSnapshot(Config(Agent("research", new List<string> { "read_file_lines" })));

            var names = _provider.GetMainQueue().Definitions.Select(d => d.Name);

            Assert.That(names, Is.EquivalentTo(new[] { "search_file_content", "research" }));
        }

        [Test]
        public void GetMainQueue_NonEmptyAllowlistAllUnavailable_ExcludesAgent()
        {
            SetBuiltIn();
            SetSnapshot(Config(Agent("research", new List<string> { "read_file_lines" })));

            Assert.That(_provider.GetMainQueue().Definitions, Is.Empty);
        }

        [Test]
        public void GetMainQueue_WriteMode_AllowsWriteToolsAndShowsAgent()
        {
            _settingsManagerMock.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = true, EnableSubAgents = true, EnableAiWriteTools = true });

            var writeTool = new ToolDefinition { Name = "replace_file_content" };
            SetBuiltIn(writeTool);
            _builtInMock.Setup(b => b.GetToolAccessLevel("replace_file_content"))
                .Returns(ToolAccessLevel.FullAccess);
            SetSnapshot(Config(Agent("editor", new List<string> { "replace_file_content" })));

            var names = _provider.GetMainQueue().Definitions.Select(d => d.Name);

            Assert.That(names, Is.EquivalentTo(new[] { "editor" }));
        }

        [Test]
        public void GetMainQueue_IncludesMcpTools()
        {
            SetBuiltIn(new ToolDefinition { Name = "read_file_lines" });
            _mcpToolManagerMock.Setup(m => m.GetMcpToolDefinitions())
                .Returns(new List<ToolDefinition> { new ToolDefinition { Name = "mcp_tool" } }.AsReadOnly());
            SetSnapshot(Config(Agent("reasoner", new List<string>())));

            var names = _provider.GetMainQueue().Definitions.Select(d => d.Name);

            Assert.That(names, Is.EquivalentTo(new[] { "read_file_lines", "reasoner", "mcp_tool" }));
        }

        [Test]
        public void GetMainQueue_CachesUntilSnapshotChanges()
        {
            var config1 = Config(Agent("reasoner", new List<string>()));
            SetSnapshot(config1);

            var first = _provider.GetMainQueue();
            var second = _provider.GetMainQueue();
            Assert.That(first, Is.SameAs(second));

            var config2 = Config(Agent("reasoner", new List<string>()));
            SetSnapshot(config2);

            var third = _provider.GetMainQueue();
            Assert.That(third, Is.Not.SameAs(first));
        }

        // =====================================================================
        // GetSubAgentQueue
        // =====================================================================

        [Test]
        public void GetSubAgentQueue_IntersectsAllowedWithBuiltIn()
        {
            SetBuiltIn(
                new ToolDefinition { Name = "read_file_lines" },
                new ToolDefinition { Name = "search_file_content" });

            var request = new SubAgentRunRequest
            {
                AgentName = "research",
                AllowedTools = new List<string> { "read_file_lines", "ghost" }
            };

            var queue = _provider.GetSubAgentQueue(request);

            Assert.That(queue.IsSubAgent, Is.True);
            Assert.That(queue.SubAgentName, Is.EqualTo("research"));
            Assert.That(queue.Definitions.Select(d => d.Name), Is.EquivalentTo(new[] { "read_file_lines" }));
        }

        [Test]
        public void GetSubAgentQueue_WriteGate_BlocksWriteToolsInReadMode()
        {
            var writeTool = new ToolDefinition { Name = "replace_file_content" };
            SetBuiltIn(writeTool);
            _builtInMock.Setup(b => b.GetToolAccessLevel("replace_file_content"))
                .Returns(ToolAccessLevel.FullAccess);

            var request = new SubAgentRunRequest
            {
                AgentName = "editor",
                AllowedTools = new List<string> { "replace_file_content" }
            };

            Assert.That(_provider.GetSubAgentQueue(request).Definitions, Is.Empty);
        }

        [Test]
        public void GetSubAgentQueue_NeverIncludesAgentsOrMcp()
        {
            SetBuiltIn(new ToolDefinition { Name = "read_file_lines" });
            _mcpToolManagerMock.Setup(m => m.GetMcpToolDefinitions())
                .Returns(new List<ToolDefinition> { new ToolDefinition { Name = "mcp_tool" } }.AsReadOnly());
            _catalogMock.Setup(c => c.TryGetSnapshot())
                .Returns(Config(Agent("research", new List<string>())));

            var request = new SubAgentRunRequest
            {
                AgentName = "research",
                AllowedTools = new List<string> { "research", "mcp_tool", "read_file_lines" }
            };

            Assert.That(
                _provider.GetSubAgentQueue(request).Definitions.Select(d => d.Name),
                Is.EquivalentTo(new[] { "read_file_lines" }));
        }

        [Test]
        public void GetSubAgentQueue_EmptyAllowed_ReturnsEmpty()
        {
            var request = new SubAgentRunRequest
            {
                AgentName = "reasoner",
                AllowedTools = new List<string>()
            };

            Assert.That(_provider.GetSubAgentQueue(request).Definitions, Is.Empty);
        }
    }
}
