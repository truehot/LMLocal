using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
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
                    It.IsAny<CancellationToken>()))
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
            Assert.That(first.AllowedTools, Is.EquivalentTo(new[] { "get_solution_overview", "find_files" }));

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
                    It.IsAny<CancellationToken>()),
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
                    It.IsAny<CancellationToken>()),
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
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task UpdateSubAgentsAsync_InvalidJson_ReturnsFailure()
        {
            var result = await UpdateAsync(_controller, "{not valid}");

            Assert.That(result.Success, Is.False);
            _configManagerMock.Verify(
                m => m.UpdateEnabledFlagsAsync(
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>(),
                    It.IsAny<CancellationToken>()),
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
                    It.IsAny<CancellationToken>()),
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
                    It.IsAny<CancellationToken>()))
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
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<string> { "agent name 'researcher' is not unique (used by another SubAgent)" });

            var json = "{\"agents\":[{\"id\":\"researcher\",\"enabled\":true}]}";
            var result = await UpdateAsync(_controller, json);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error.Message, Does.Contain("not unique"));
        }
    }
}
