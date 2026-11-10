using System.Collections.Generic;
using System.Linq;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.SubAgents
{
    [TestFixture]
    public class SubAgentsParallelPolicyTests
    {
        private Mock<ISubAgentsCatalog> _catalogMock;
        private Mock<ISettingsManager> _settingsManagerMock;
        private Mock<IBuiltInVsToolProvider> _builtInToolsMock;
        private SubAgentsParallelPolicy _policy;

        [SetUp]
        public void SetUp()
        {
            _catalogMock = new Mock<ISubAgentsCatalog>();
            _settingsManagerMock = new Mock<ISettingsManager>();
            _builtInToolsMock = new Mock<IBuiltInVsToolProvider>();

            _settingsManagerMock.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = true, EnableSubAgents = true });

            // Allowed tools default to unknown / not registered.
            _builtInToolsMock.Setup(t => t.ToolExists(It.IsAny<string>())).Returns(false);

            _policy = new SubAgentsParallelPolicy(
                _catalogMock.Object,
                _settingsManagerMock.Object,
                _builtInToolsMock.Object);
        }

        private void SetAgents(params SubAgentDefinition[] agents)
            => _catalogMock.Setup(c => c.GetEnabledAgents()).Returns(agents.ToList());

        private void RegisterReadOnlyTool(string name)
        {
            _builtInToolsMock.Setup(t => t.ToolExists(name)).Returns(true);
            _builtInToolsMock.Setup(t => t.GetToolAccessLevel(name)).Returns(ToolAccessLevel.ReadOnly);
        }

        private void RegisterTool(string name, ToolAccessLevel access)
        {
            _builtInToolsMock.Setup(t => t.ToolExists(name)).Returns(true);
            _builtInToolsMock.Setup(t => t.GetToolAccessLevel(name)).Returns(access);
        }

        private static SubAgentDefinition Agent(string id, bool parallel, params string[] allowedTools)
        {
            return new SubAgentDefinition
            {
                Id = id,
                Description = id,
                CustomBaseUrl = "http://localhost:1234",
                Model = "m",
                Parallel = parallel,
                AllowedTools = allowedTools.ToList()
            };
        }

        // =====================================================================
        // IsParallelEnabled
        // =====================================================================

        [Test]
        public void IsParallelEnabled_ParallelAgentWithReadOnlyTools_ReturnsTrue()
        {
            RegisterReadOnlyTool("read_file_lines");
            SetAgents(Agent("researcher", parallel: true, "read_file_lines"));

            Assert.That(_policy.IsParallelEnabled("researcher"), Is.True);
        }

        [Test]
        public void IsParallelEnabled_TrimsName()
        {
            SetAgents(Agent("researcher", parallel: true));

            Assert.That(_policy.IsParallelEnabled("  researcher  "), Is.True);
        }

        [Test]
        public void IsParallelEnabled_ReasoningOnlyAgent_ReturnsTrue()
        {
            SetAgents(Agent("researcher", parallel: true));

            Assert.That(_policy.IsParallelEnabled("researcher"), Is.True);
        }

        [Test]
        public void IsParallelEnabled_AgentParallelFalse_ReturnsFalse()
        {
            SetAgents(Agent("researcher", parallel: false));

            Assert.That(_policy.IsParallelEnabled("researcher"), Is.False);
        }

        [Test]
        public void IsParallelEnabled_UnknownTool_ReturnsFalse()
        {
            SetAgents(Agent("researcher", parallel: true));

            Assert.That(_policy.IsParallelEnabled("ghost"), Is.False);
        }

        [Test]
        public void IsParallelEnabled_NullOrEmpty_ReturnsFalse()
        {
            Assert.That(_policy.IsParallelEnabled(null), Is.False);
            Assert.That(_policy.IsParallelEnabled("   "), Is.False);
        }

        [Test]
        public void IsParallelEnabled_AiToolsDisabled_ReturnsFalse()
        {
            _settingsManagerMock.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = false, EnableSubAgents = true });
            SetAgents(Agent("researcher", parallel: true));

            Assert.That(_policy.IsParallelEnabled("researcher"), Is.False);
        }

        [Test]
        public void IsParallelEnabled_SubAgentsDisabled_ReturnsFalse()
        {
            _settingsManagerMock.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = true, EnableSubAgents = false });
            SetAgents(Agent("researcher", parallel: true));

            Assert.That(_policy.IsParallelEnabled("researcher"), Is.False);
        }

        [Test]
        public void IsParallelEnabled_WriteToolInAllowedTools_ReturnsFalse()
        {
            RegisterTool("replace_file_content", ToolAccessLevel.FullAccess);
            SetAgents(Agent("editor", parallel: true, "replace_file_content"));

            Assert.That(_policy.IsParallelEnabled("editor"), Is.False);
        }

        [Test]
        public void IsParallelEnabled_ExecutionToolInAllowedTools_ReturnsFalse()
        {
            RegisterTool("run_tests", ToolAccessLevel.Execution);
            SetAgents(Agent("tester", parallel: true, "run_tests"));

            Assert.That(_policy.IsParallelEnabled("tester"), Is.False);
        }

        [Test]
        public void IsParallelEnabled_UnknownAllowedTool_IsIgnored()
        {
            RegisterReadOnlyTool("read_file_lines");
            SetAgents(Agent("researcher", parallel: true, "read_file_lines", "some_unknown_tool"));

            Assert.That(_policy.IsParallelEnabled("researcher"), Is.True);
        }

        // =====================================================================
        // ResolveMaxParallel — resolution order: agent -> top-level -> default
        // =====================================================================

        [Test]
        public void ResolveMaxParallel_AgentValueWins()
        {
            var agent = Agent("researcher", parallel: true);
            agent.MaxParallel = 4;
            SetAgents(agent);
            _catalogMock.Setup(c => c.TryGetSnapshot()).Returns(new SubAgentsConfig { MaxParallel = 8 });

            Assert.That(_policy.ResolveMaxParallel("researcher"), Is.EqualTo(4));
        }

        [Test]
        public void ResolveMaxParallel_NoAgentValue_UsesTopLevel()
        {
            SetAgents(Agent("researcher", parallel: true));
            _catalogMock.Setup(c => c.TryGetSnapshot()).Returns(new SubAgentsConfig { MaxParallel = 4 });

            Assert.That(_policy.ResolveMaxParallel("researcher"), Is.EqualTo(4));
        }

        [Test]
        public void ResolveMaxParallel_NeitherAgentNorTopLevel_UsesDefault()
        {
            SetAgents(Agent("researcher", parallel: true));
            _catalogMock.Setup(c => c.TryGetSnapshot()).Returns(new SubAgentsConfig());

            Assert.That(_policy.ResolveMaxParallel("researcher"), Is.EqualTo(SubAgentsConfig.DefaultMaxParallel));
        }

        [Test]
        public void ResolveMaxParallel_AgentNonPositive_FallsBackToTopLevel()
        {
            var agent = Agent("researcher", parallel: true);
            agent.MaxParallel = 0;
            SetAgents(agent);
            _catalogMock.Setup(c => c.TryGetSnapshot()).Returns(new SubAgentsConfig { MaxParallel = 3 });

            Assert.That(_policy.ResolveMaxParallel("researcher"), Is.EqualTo(3));
        }

        [Test]
        public void ResolveMaxParallel_AgentAboveLimit_IsClampedToLimit()
        {
            var agent = Agent("researcher", parallel: true);
            agent.MaxParallel = SubAgentsConfig.MaxParallelLimit + 4;
            SetAgents(agent);
            _catalogMock.Setup(c => c.TryGetSnapshot()).Returns(new SubAgentsConfig());

            Assert.That(_policy.ResolveMaxParallel("researcher"), Is.EqualTo(SubAgentsConfig.MaxParallelLimit));
        }

        [Test]
        public void ResolveMaxParallel_TopLevelAboveLimit_IsClampedToLimit()
        {
            SetAgents(Agent("researcher", parallel: true));
            _catalogMock.Setup(c => c.TryGetSnapshot()).Returns(new SubAgentsConfig { MaxParallel = 20 });

            Assert.That(_policy.ResolveMaxParallel("researcher"), Is.EqualTo(SubAgentsConfig.MaxParallelLimit));
        }

        [Test]
        public void ResolveMaxParallel_ExactlyLimit_IsKept()
        {
            var agent = Agent("researcher", parallel: true);
            agent.MaxParallel = SubAgentsConfig.MaxParallelLimit;
            SetAgents(agent);
            _catalogMock.Setup(c => c.TryGetSnapshot()).Returns(new SubAgentsConfig());

            Assert.That(_policy.ResolveMaxParallel("researcher"), Is.EqualTo(SubAgentsConfig.MaxParallelLimit));
        }

        [Test]
        public void ResolveMaxParallel_UnknownAgent_UsesDefault()
        {
            _catalogMock.Setup(c => c.TryGetSnapshot()).Returns(new SubAgentsConfig());

            Assert.That(_policy.ResolveMaxParallel("ghost"), Is.EqualTo(SubAgentsConfig.DefaultMaxParallel));
        }
    }
}
