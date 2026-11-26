using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Infrastructure.WebView.Controllers;
using LMLocal.Infrastructure.WebView.Models;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class SubAgentsControllerTests
    {
        private Mock<ISubAgentsConfigManager> _configManagerMock;
        private SubAgentsController _controller;

        [SetUp]
        public void SetUp()
        {
            _configManagerMock = new Mock<ISubAgentsConfigManager>();
            _configManagerMock
                .Setup(m => m.UpdateEnabledFlagsAsync(
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<SubAgentDefaults>(),
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>()))
                .ReturnsAsync(new List<string>());

            _controller = new SubAgentsController(_configManagerMock.Object);
        }

        [TearDown]
        public void TearDown()
        {
            _controller = null;
            _configManagerMock = null;
        }

        private static SubAgentsConfig CreateConfig()
        {
            var config = new SubAgentsConfig();
            config.Agents.Add(new SubAgentDefinition
            {
                Id = "researcher",
                Description = "Research agent",
                ProviderType = "deepseek",
                CustomBaseUrl = "https://api.deepseek.com",
                CustomApiKey = "secret",
                Model = "deepseek-chat",
                Temperature = 0.3,
                TimeoutSeconds = 90,
                MaxRounds = 5,
                MaxTokens = 2048,
                Enabled = true,
                AllowedTools = new List<string> { "get_solution_overview", "find_files" }
            });
            config.Agents.Add(new SubAgentDefinition
            {
                Id = "coder",
                ProviderType = "lmstudio",
                CustomBaseUrl = "http://localhost:1234",
                Model = "qwen2.5-coder-7b-instruct",
                Enabled = false
            });
            return config;
        }

        private void SetupGetAsync(SubAgentsConfig config)
        {
            _configManagerMock
                .Setup(m => m.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(config);
        }

        private static async Task<SubAgentsUpdateResponse> UpdateAsync(SubAgentsController controller, string json)
        {
            var raw = await controller.UpdateSubAgentsAsync(json);
            return raw.FromJson<SubAgentsUpdateResponse>();
        }

        // =========================================================================
        // GetSubAgentsAsync
        // =========================================================================

        [Test]
        public async Task GetSubAgentsAsync_ReturnsAgentsWithDetails()
        {
            SetupGetAsync(CreateConfig());

            var json = await _controller.GetSubAgentsAsync();

            var response = json.FromJson<SubAgentsListResponse>();
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Success, Is.True);
            Assert.That(response.Agents, Has.Count.EqualTo(2));

            var first = response.Agents[0];
            Assert.That(first.Id, Is.EqualTo("researcher"));
            Assert.That(first.Description, Is.EqualTo("Research agent"));
            Assert.That(first.ProviderType, Is.EqualTo("deepseek"));
            Assert.That(first.CustomBaseUrl, Is.EqualTo("https://api.deepseek.com"));
            Assert.That(first.Model, Is.EqualTo("deepseek-chat"));
            Assert.That(first.Temperature, Is.EqualTo(0.3));
            Assert.That(first.TimeoutSeconds, Is.EqualTo(90));
            Assert.That(first.MaxRounds, Is.EqualTo(5));
            Assert.That(first.MaxTokens, Is.EqualTo(2048));
            Assert.That(first.Enabled, Is.True);

            Assert.That(response.Agents[1].Id, Is.EqualTo("coder"));
            Assert.That(response.Agents[1].Enabled, Is.False);
        }

        [Test]
        public async Task GetSubAgentsAsync_WhenManagerThrows_ReturnsSuccessFalseWithError()
        {
            _configManagerMock
                .Setup(m => m.GetAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("file not found"));

            var json = await _controller.GetSubAgentsAsync();

            var response = json.FromJson<SubAgentsListResponse>();
            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Is.Not.Null);
            Assert.That(response.Error.Message, Does.Contain("file not found"));
        }

        // =========================================================================
        // UpdateSubAgentsAsync — happy path
        // =========================================================================

        [Test]
        public async Task UpdateSubAgentsAsync_DelegatesFlagsToManager_AndReturnsSuccess()
        {
            var json = "{\"agents\":[{\"id\":\"researcher\",\"enabled\":false},{\"id\":\"coder\",\"enabled\":true}]}";
            var result = await UpdateAsync(_controller, json);

            Assert.That(result.Success, Is.True);
            _configManagerMock.Verify(
                m => m.UpdateEnabledFlagsAsync(
                    It.Is<IReadOnlyList<SubAgentEnabledFlag>>(flags =>
                        flags.Count == 2 &&
                        flags[0].Id == "researcher" && !flags[0].Enabled &&
                        flags[1].Id == "coder" && flags[1].Enabled),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<SubAgentDefaults>(),
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>()),
                Times.Once);
        }

        // =========================================================================
        // UpdateSubAgentsAsync — malformed input
        // =========================================================================

        [Test]
        public async Task UpdateSubAgentsAsync_NullJson_ReturnsFailure()
        {
            var result = await UpdateAsync(_controller, null);

            Assert.That(result.Success, Is.False);
            _configManagerMock.Verify(
                m => m.UpdateEnabledFlagsAsync(
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<SubAgentDefaults>(),
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>()),
                Times.Never);
        }

        [Test]
        public async Task UpdateSubAgentsAsync_EmptyJson_ReturnsFailure()
        {
            var result = await UpdateAsync(_controller, "");

            Assert.That(result.Success, Is.False);
            _configManagerMock.Verify(
                m => m.UpdateEnabledFlagsAsync(
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<SubAgentDefaults>(),
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>()),
                Times.Never);
        }

        [Test]
        public async Task UpdateSubAgentsAsync_InvalidJson_ReturnsFailure()
        {
            var result = await UpdateAsync(_controller, "{\"not valid}");

            Assert.That(result.Success, Is.False);
            _configManagerMock.Verify(
                m => m.UpdateEnabledFlagsAsync(
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<SubAgentDefaults>(),
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>()),
                Times.Never);
        }

        [Test]
        public async Task UpdateSubAgentsAsync_MissingAgents_ReturnsFailure()
        {
            var result = await UpdateAsync(_controller, "{}");

            Assert.That(result.Success, Is.False);
            _configManagerMock.Verify(
                m => m.UpdateEnabledFlagsAsync(
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<SubAgentDefaults>(),
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>()),
                Times.Never);
        }

        // =========================================================================
        // UpdateSubAgentsAsync — manager errors
        // =========================================================================

        [Test]
        public async Task UpdateSubAgentsAsync_WhenManagerThrows_ReturnsFailure()
        {
            _configManagerMock
                .Setup(m => m.UpdateEnabledFlagsAsync(
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<SubAgentDefaults>(),
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>()))
                .ThrowsAsync(new InvalidOperationException("disk failure"));

            var json = "{\"agents\":[{\"id\":\"researcher\",\"enabled\":false}]}";
            var result = await UpdateAsync(_controller, json);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.Message, Does.Contain("disk failure"));
        }

        [Test]
        public async Task UpdateSubAgentsAsync_WhenValidationFails_ReturnsFailure()
        {
            _configManagerMock
                .Setup(m => m.UpdateEnabledFlagsAsync(
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<SubAgentDefaults>(),
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>()))
                .ReturnsAsync(new List<string> { "agent name 'researcher' is not unique (used by another SubAgent)" });

            var json = "{\"agents\":[{\"id\":\"researcher\",\"enabled\":true}]}";
            var result = await UpdateAsync(_controller, json);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.Message, Does.Contain("not unique"));
        }

        // =========================================================================
        // Pipelines: the dialog shows them and saves their flags through this controller. If the flags are lost on
        // the way, a pipeline toggle in the dialog silently does nothing.
        // =========================================================================

        private static SubAgentPipelineDefinition Pipeline(string id, params SubAgentPipelineStep[] steps)
        {
            return new SubAgentPipelineDefinition { Id = id, Description = "Pipeline " + id, Steps = steps.ToList() };
        }

        private static SubAgentPipelineStep Step(string agent, string id = null)
        {
            return new SubAgentPipelineStep { Id = id, Agent = agent, Prompt = "{{task}}" };
        }

        private static SubAgentDefinition Agent(string id, bool enabled = true, bool? exposed = null, params string[] tools)
        {
            return new SubAgentDefinition
            {
                Id = id,
                Description = id,
                CustomBaseUrl = "http://localhost:1234",
                Model = "m",
                Enabled = enabled,
                Exposed = exposed,
                AllowedTools = tools.ToList()
            };
        }

        [Test]
        public async Task GetSubAgentsAsync_ReportsTheExposedFlagOfEveryAgent()
        {
            var config = new SubAgentsConfig();
            config.Agents.Add(Agent("visible"));
            config.Agents.Add(Agent("hidden", exposed: false));
            SetupGetAsync(config);

            var response = (await _controller.GetSubAgentsAsync()).FromJson<SubAgentsListResponse>();

            Assert.That(response.Agents.Single(a => a.Id == "visible").Exposed, Is.True, "a missing key means exposed");
            Assert.That(response.Agents.Single(a => a.Id == "hidden").Exposed, Is.False);
        }

        [Test]
        public async Task GetSubAgentsAsync_ReturnsPipelinesWithTheirChainAndAvailability()
        {
            var config = new SubAgentsConfig();
            config.Agents.Add(Agent("explorer", exposed: false));
            config.Agents.Add(Agent("analyzer"));
            config.Agents.Add(Agent("archived", enabled: false));

            var ready = Pipeline("review_flow", Step("explorer", "find"), Step("analyzer"));
            ready.DisplayName = "Review flow";

            var broken = Pipeline("old_flow", Step("archived"));
            broken.Enabled = false;

            config.Pipelines = new List<SubAgentPipelineDefinition> { ready, broken };
            SetupGetAsync(config);

            var response = (await _controller.GetSubAgentsAsync()).FromJson<SubAgentsListResponse>();

            Assert.That(response.Pipelines, Has.Count.EqualTo(2));

            var first = response.Pipelines[0];
            Assert.That(first.Id, Is.EqualTo("review_flow"));
            Assert.That(first.DisplayName, Is.EqualTo("Review flow"));
            Assert.That(first.Enabled, Is.True);
            Assert.That(first.Steps, Is.EqualTo(new[] { "find", "analyzer" }), "step id when set, else the agent");
            Assert.That(first.UnavailableReason, Is.Null, "a hidden step agent does not make the pipeline unavailable");

            var second = response.Pipelines[1];
            Assert.That(second.Enabled, Is.False);
            Assert.That(second.UnavailableReason, Does.Contain("'archived' is disabled"));
        }

        [Test]
        public async Task GetSubAgentsAsync_ParallelBranchWithAWriteAgent_ShowsTheReason()
        {
            var builtIn = new Mock<IBuiltInVsToolProvider>();
            builtIn.Setup(b => b.GetAllToolDefinitionsUnfiltered())
                .Returns(new List<ToolDefinition> { new ToolDefinition { Name = "replace_file_lines" } });
            builtIn.Setup(b => b.ToolExists("replace_file_lines")).Returns(true);
            builtIn.Setup(b => b.GetToolAccessLevel("replace_file_lines")).Returns(ToolAccessLevel.FullAccess);

            var controller = new SubAgentsController(_configManagerMock.Object, builtIn.Object);

            var config = new SubAgentsConfig();
            config.Agents.Add(Agent("reader"));
            config.Agents.Add(Agent("editor", true, null, "replace_file_lines"));
            config.Pipelines = new List<SubAgentPipelineDefinition>
            {
                Pipeline("fan_out", new SubAgentPipelineStep
                {
                    Parallel = new List<SubAgentPipelineStep> { Step("reader"), Step("editor") }
                })
            };
            SetupGetAsync(config);

            var response = (await controller.GetSubAgentsAsync()).FromJson<SubAgentsListResponse>();

            Assert.That(response.Pipelines.Single().UnavailableReason, Does.Contain("'editor' uses write tools"));
        }

        [Test]
        public async Task GetSubAgentsAsync_NoPipelines_ReturnsAnEmptyList()
        {
            SetupGetAsync(CreateConfig());

            var response = (await _controller.GetSubAgentsAsync()).FromJson<SubAgentsListResponse>();

            Assert.That(response.Pipelines, Is.Empty);
        }

        [Test]
        public async Task UpdateSubAgentsAsync_PassesThePipelineFlagsToTheManager()
        {
            var json = "{\"agents\":[{\"id\":\"researcher\",\"enabled\":true}],"
                + "\"pipelines\":[{\"id\":\"review_flow\",\"enabled\":false},{\"id\":\"plan_flow\",\"enabled\":true}]}";

            var result = await UpdateAsync(_controller, json);

            Assert.That(result.Success, Is.True);
            _configManagerMock.Verify(
                m => m.UpdateEnabledFlagsAsync(
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>(),
                    It.IsAny<CancellationToken>(),
                    null,
                    It.Is<IReadOnlyList<SubAgentEnabledFlag>>(flags =>
                        flags != null && flags.Count == 2 &&
                        flags[0].Id == "review_flow" && !flags[0].Enabled &&
                        flags[1].Id == "plan_flow" && flags[1].Enabled)),
                Times.Once);
        }

        [Test]
        public async Task UpdateSubAgentsAsync_WithActiveModelDefaults_StillPassesThePipelineFlags()
        {
            var json = "{\"model\":\"qwen\",\"agents\":[{\"id\":\"researcher\",\"enabled\":true}],"
                + "\"pipelines\":[{\"id\":\"review_flow\",\"enabled\":false}]}";

            var result = await UpdateAsync(_controller, json);

            Assert.That(result.Success, Is.True);
            _configManagerMock.Verify(
                m => m.UpdateEnabledFlagsAsync(
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>(),
                    It.IsAny<CancellationToken>(),
                    It.Is<SubAgentDefaults>(d => d != null && d.Model == "qwen"),
                    It.Is<IReadOnlyList<SubAgentEnabledFlag>>(flags =>
                        flags != null && flags.Count == 1 && flags[0].Id == "review_flow" && !flags[0].Enabled)),
                Times.Once);
        }

        [Test]
        public async Task UpdateSubAgentsAsync_WithoutPipelines_PassesNoPipelineFlags()
        {
            var json = "{\"agents\":[{\"id\":\"researcher\",\"enabled\":true}]}";

            await UpdateAsync(_controller, json);

            _configManagerMock.Verify(
                m => m.UpdateEnabledFlagsAsync(
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<SubAgentDefaults>(),
                    null),
                Times.Once);
        }
    }
}
