using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.SubAgents;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.SubAgents
{
    [TestFixture]
    public class SubAgentsToolSourceTests
    {
        private Mock<ISubAgentsCatalog> _catalogMock;
        private Mock<ISettingsManager> _settingsManagerMock;
        private Mock<ISubAgentsService> _subAgentsServiceMock;
        private SubAgentsToolSource _source;

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
            _catalogMock = new Mock<ISubAgentsCatalog>();
            _settingsManagerMock = new Mock<ISettingsManager>();
            _subAgentsServiceMock = new Mock<ISubAgentsService>();

            _settingsManagerMock.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = true, EnableSubAgents = true, EnableAiWriteTools = false });

            _source = new SubAgentsToolSource(
                _catalogMock.Object,
                _settingsManagerMock.Object,
                () => _subAgentsServiceMock.Object);
        }

        private void SetEnabledAgents(params SubAgentDefinition[] agents)
        {
            _catalogMock.Setup(c => c.GetEnabledAgents()).Returns(agents.ToList());
        }

        // =====================================================================
        // ToolExists
        // =====================================================================

        [Test]
        public void ToolExists_EnabledAgent_ReturnsTrue()
        {
            SetEnabledAgents(Agent("researcher", new List<string>()));

            Assert.That(_source.ToolExists("researcher"), Is.True);
        }

        [Test]
        public void ToolExists_TrimsName()
        {
            SetEnabledAgents(Agent("  researcher  ", new List<string>()));

            Assert.That(_source.ToolExists("researcher"), Is.True);
        }

        [Test]
        public void ToolExists_WhenSubAgentsDisabled_ReturnsFalse()
        {
            _settingsManagerMock.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = true, EnableSubAgents = false });
            SetEnabledAgents(Agent("researcher", new List<string>()));

            Assert.That(_source.ToolExists("researcher"), Is.False);
        }

        [Test]
        public void ToolExists_UnknownAgent_ReturnsFalse()
        {
            Assert.That(_source.ToolExists("ghost"), Is.False);
        }

        // =====================================================================
        // ExecuteAsync
        // =====================================================================

        [Test]
        public async Task ExecuteAsync_ValidTask_RunsServiceAndReturnsResponse()
        {
            var agent = Agent("researcher", new List<string> { "read_file_lines" });
            _catalogMock.Setup(c => c.TryGetSnapshot()).Returns(Config(agent));
            SetEnabledAgents(agent);

            var response = new SubAgentsRunResponse { Success = true, Content = "ok" };
            _subAgentsServiceMock
                .Setup(s => s.ExecutePromptAsync(It.IsAny<SubAgentRunRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            var result = await _source.ExecuteAsync(
                "researcher",
                new Dictionary<string, object> { { "task", "explore" } },
                CancellationToken.None);

            Assert.That(result, Is.SameAs(response));
            _subAgentsServiceMock.Verify(
                s => s.ExecutePromptAsync(
                    It.Is<SubAgentRunRequest>(r =>
                        r.AgentName == "researcher" &&
                        r.Prompt == "explore" &&
                        r.AllowedTools.Count == 1 &&
                        r.ExcludedAgentNames.Contains("researcher")),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ExecuteAsync_MissingTask_ReturnsFailure()
        {
            SetEnabledAgents(Agent("researcher", new List<string>()));

            var result = await _source.ExecuteAsync(
                "researcher",
                new Dictionary<string, object>(),
                CancellationToken.None);

            var response = result as SubAgentsRunResponse;
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.Contain("task"));
        }

        [Test]
        public async Task ExecuteAsync_UnknownAgent_ReturnsFailure()
        {
            var result = await _source.ExecuteAsync(
                "ghost",
                new Dictionary<string, object> { { "task", "x" } },
                CancellationToken.None);

            var response = result as SubAgentsRunResponse;
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.Contain("ghost"));
        }

        [Test]
        public async Task ExecuteAsync_WhenSubAgentsDisabled_ReturnsFailure()
        {
            _settingsManagerMock.SetupGet(s => s.Current)
                .Returns(new AppSettings { EnableAiTools = true, EnableSubAgents = false });
            SetEnabledAgents(Agent("researcher", new List<string>()));

            var result = await _source.ExecuteAsync(
                "researcher",
                new Dictionary<string, object> { { "task", "x" } },
                CancellationToken.None);

            var response = result as SubAgentsRunResponse;
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Success, Is.False);
        }

        // =====================================================================
        // GetToolTimeout
        // =====================================================================

        [Test]
        public void GetToolTimeout_ReturnsAgentTimeoutPlusGrace()
        {
            var agent = Agent("researcher", new List<string>());
            agent.TimeoutSeconds = 120;
            SetEnabledAgents(agent);

            var timeout = _source.GetToolTimeout("researcher");

            Assert.That(timeout, Is.EqualTo(TimeSpan.FromSeconds(125)));
        }

        [Test]
        public void GetToolTimeout_TimeoutZero_ReturnsNull()
        {
            var agent = Agent("researcher", new List<string>());
            agent.TimeoutSeconds = 0;
            SetEnabledAgents(agent);

            Assert.That(_source.GetToolTimeout("researcher"), Is.Null);
        }

        [Test]
        public void GetToolTimeout_UnknownAgent_ReturnsNull()
        {
            Assert.That(_source.GetToolTimeout("ghost"), Is.Null);
        }

        // =====================================================================
        // GetDisplayName
        // =====================================================================

        [Test]
        public void GetDisplayName_UsesDisplayNameWhenSet()
        {
            var agent = Agent("researcher", new List<string>());
            agent.DisplayName = "Researcher";
            SetEnabledAgents(agent);

            Assert.That(_source.GetDisplayName("researcher"), Is.EqualTo("Researcher"));
        }

        [Test]
        public void GetDisplayName_FallsBackToIdWhenNoDisplayName()
        {
            SetEnabledAgents(Agent("researcher", new List<string>()));

            Assert.That(_source.GetDisplayName("researcher"), Is.EqualTo("researcher"));
        }

        [Test]
        public void GetDisplayName_UnknownAgent_ReturnsToolName()
        {
            Assert.That(_source.GetDisplayName("ghost"), Is.EqualTo("ghost"));
        }
    }
}
