using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Infrastructure.WebView.Controllers;
using LMLocal.Infrastructure.WebView.Models;
using LMLocal.Tests.Unit.Infrastructure;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    // =========================================================================
    // Manager — "Use Active Model" top-level defaults
    // =========================================================================

    [TestFixture]
    public class SubAgentsActiveModelDefaultsTests
    {
        private static (SubAgentsConfigManager Manager, InMemoryFileSystem Fs, string FilePath) CreateManager()
        {
            var fs = new InMemoryFileSystem();
            var settings = new Mock<ISettingsManager>();
            settings.Setup(s => s.LocalAppDataFolder).Returns("LMLocalChatUnit");

            var builtInTools = new Mock<IBuiltInVsToolProvider>();
            builtInTools.Setup(b => b.GetAllToolDefinitionsUnfiltered())
                .Returns(new List<ToolDefinition>());

            var manager = new SubAgentsConfigManager(fs, settings.Object, builtInTools.Object);

            var filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LMLocalChatUnit",
                "subagents.json");

            return (manager, fs, filePath);
        }

        private static void WriteConfig(InMemoryFileSystem fs, string filePath, string json)
        {
            fs.WriteAllBytesAsync(filePath, System.Text.Encoding.UTF8.GetBytes(json)).GetAwaiter().GetResult();
        }

        [Test]
        public async Task UpdateEnabledFlagsAsync_WithDefaults_ReplacesRootAndNonEmptyAgentFields()
        {
            var (manager, fs, path) = CreateManager();
            WriteConfig(fs, path, @"{ ""providerType"": ""lmstudio"", ""customBaseUrl"": ""http://localhost:1234"", ""agents"": [
                { ""id"": ""researcher"", ""description"": ""A"", ""providerType"": ""deepseek"", ""customBaseUrl"": ""https://api.deepseek.com"", ""customApiKey"": ""old-key"", ""model"": ""deepseek-chat"", ""enabled"": true },
                { ""id"": ""coder"", ""description"": ""B"", ""customBaseUrl"": ""http://localhost:9999"", ""model"": ""qwen2.5-coder"", ""enabled"": true }
            ] }");

            var defaults = new SubAgentDefaults
            {
                ProviderType = "lmstudio",
                CustomBaseUrl = "http://localhost:1234",
                CustomApiKey = null,
                Model = "google/gemma-4-e2b"
            };

            var errors = await manager.UpdateEnabledFlagsAsync(
                new List<SubAgentEnabledFlag>(),
                cancellationToken: CancellationToken.None,
                defaults: defaults);

            Assert.That(errors, Is.Empty);

            var cfg = await manager.GetAsync();
            Assert.That(cfg.ProviderType, Is.EqualTo("lmstudio"));
            Assert.That(cfg.CustomBaseUrl, Is.EqualTo("http://localhost:1234"));
            Assert.That(cfg.Model, Is.EqualTo("google/gemma-4-e2b"));

            // researcher: provider/baseUrl/model replaced, but the empty default api key must NOT clear its key.
            Assert.That(cfg.Agents[0].ProviderType, Is.EqualTo("lmstudio"));
            Assert.That(cfg.Agents[0].CustomBaseUrl, Is.EqualTo("http://localhost:1234"));
            Assert.That(cfg.Agents[0].CustomApiKey, Is.EqualTo("old-key"));
            Assert.That(cfg.Agents[0].Model, Is.EqualTo("google/gemma-4-e2b"));

            // coder: inherited provider type also replaced, model always replaced.
            Assert.That(cfg.Agents[1].ProviderType, Is.EqualTo("lmstudio"));
            Assert.That(cfg.Agents[1].CustomBaseUrl, Is.EqualTo("http://localhost:1234"));
            Assert.That(cfg.Agents[1].Model, Is.EqualTo("google/gemma-4-e2b"));
        }

        [Test]
        public async Task UpdateEnabledFlagsAsync_WithNullDefaults_LeavesConfigUntouched()
        {
            var (manager, fs, path) = CreateManager();
            WriteConfig(fs, path, @"{ ""customApiKey"": ""root-key"", ""model"": ""root-model"", ""agents"": [
                { ""id"": ""researcher"", ""description"": ""A"", ""providerType"": ""openai"", ""customBaseUrl"": ""https://api.openai.com"", ""customApiKey"": ""agent-key"", ""model"": ""gpt-4o"", ""enabled"": true }
            ] }");

            var errors = await manager.UpdateEnabledFlagsAsync(
                new List<SubAgentEnabledFlag>(),
                cancellationToken: CancellationToken.None,
                defaults: new SubAgentDefaults());

            Assert.That(errors, Is.Empty);

            var cfg = await manager.GetAsync();
            Assert.That(cfg.CustomApiKey, Is.EqualTo("root-key"));
            Assert.That(cfg.Model, Is.EqualTo("root-model"));
            Assert.That(cfg.Agents[0].ProviderType, Is.EqualTo("openai"));
            Assert.That(cfg.Agents[0].CustomApiKey, Is.EqualTo("agent-key"));
            Assert.That(cfg.Agents[0].Model, Is.EqualTo("gpt-4o"));
        }

        [Test]
        public async Task UpdateEnabledFlagsAsync_WithDefaults_ModelAlwaysAppliedToEveryAgent()
        {
            var (manager, fs, path) = CreateManager();
            WriteConfig(fs, path, @"{ ""agents"": [
                { ""id"": ""a"", ""description"": ""A"", ""customBaseUrl"": ""http://localhost:1234"", ""model"": ""old-model-a"", ""enabled"": true },
                { ""id"": ""b"", ""description"": ""B"", ""customBaseUrl"": ""http://localhost:1234"", ""enabled"": true }
            ] }");

            var errors = await manager.UpdateEnabledFlagsAsync(
                new List<SubAgentEnabledFlag>(),
                cancellationToken: CancellationToken.None,
                defaults: new SubAgentDefaults { Model = "google/gemma-4-e2b" });

            Assert.That(errors, Is.Empty);

            var cfg = await manager.GetAsync();
            Assert.That(cfg.Model, Is.EqualTo("google/gemma-4-e2b"));
            Assert.That(cfg.Agents[0].Model, Is.EqualTo("google/gemma-4-e2b"));
            Assert.That(cfg.Agents[1].Model, Is.EqualTo("google/gemma-4-e2b"));
        }

        [Test]
        public async Task UpdateEnabledFlagsAsync_WithInvalidDefaults_ReturnsErrorsAndDoesNotSave()
        {
            var (manager, fs, path) = CreateManager();
            WriteConfig(fs, path, @"{ ""agents"": [
                { ""id"": ""a"", ""description"": ""A"", ""customBaseUrl"": ""http://localhost:1234"", ""model"": ""m"", ""enabled"": true }
            ] }");

            // Invalid default base url -> config must be rejected and file left untouched.
            var errors = await manager.UpdateEnabledFlagsAsync(
                new List<SubAgentEnabledFlag>(),
                cancellationToken: CancellationToken.None,
                defaults: new SubAgentDefaults { CustomBaseUrl = "not-a-url", Model = "google/gemma-4-e2b" });

            Assert.That(errors, Is.Not.Empty);
            Assert.That(errors.Any(e => e.Contains("not a valid URL")), Is.True);
            Assert.That(manager.TryGetSnapshot().Agents, Is.Empty);
        }
    }

    // =========================================================================
    // Controller — forwarding root defaults to the manager
    // =========================================================================

    [TestFixture]
    public class SubAgentsControllerDefaultsTests
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
                    It.IsAny<SubAgentDefaults>()))
                .ReturnsAsync(new List<string>());

            _controller = new SubAgentsController(_configManagerMock.Object);
        }

        [TearDown]
        public void TearDown()
        {
            _controller = null;
            _configManagerMock = null;
        }

        [Test]
        public async Task UpdateSubAgentsAsync_WithRootDefaults_ForwardsDefaultsToManager()
        {
            var json = @"{ ""providerType"": ""lmstudio"", ""customBaseUrl"": ""http://localhost:1234"", ""model"": ""google/gemma-4-e2b"", ""agents"": [ { ""id"": ""coder"", ""enabled"": true } ] }";

            var resultJson = await _controller.UpdateSubAgentsAsync(json);
            var result = resultJson.FromJson<SubAgentsUpdateResponse>();

            Assert.That(result.Success, Is.True);
            _configManagerMock.Verify(
                m => m.UpdateEnabledFlagsAsync(
                    It.Is<IReadOnlyList<SubAgentEnabledFlag>>(flags =>
                        flags.Count == 1 && flags[0].Id == "coder" && flags[0].Enabled),
                    It.IsAny<CancellationToken>(),
                    It.Is<SubAgentDefaults>(d =>
                        d.ProviderType == "lmstudio"
                        && d.CustomBaseUrl == "http://localhost:1234"
                        && d.CustomApiKey == null
                        && d.Model == "google/gemma-4-e2b")),
                Times.Once);
        }

        [Test]
        public async Task UpdateSubAgentsAsync_WithoutRootDefaults_DoesNotPassDefaults()
        {
            var json = @"{ ""agents"": [ { ""id"": ""coder"", ""enabled"": false } ] }";

            var resultJson = await _controller.UpdateSubAgentsAsync(json);
            var result = resultJson.FromJson<SubAgentsUpdateResponse>();

            Assert.That(result.Success, Is.True);
            _configManagerMock.Verify(
                m => m.UpdateEnabledFlagsAsync(
                    It.Is<IReadOnlyList<SubAgentEnabledFlag>>(flags =>
                        flags.Count == 1 && flags[0].Id == "coder" && !flags[0].Enabled),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<SubAgentDefaults>()),
                Times.Once);
            _configManagerMock.Verify(
                m => m.UpdateEnabledFlagsAsync(
                    It.IsAny<IReadOnlyList<SubAgentEnabledFlag>>(),
                    It.IsAny<CancellationToken>(),
                    It.Is<SubAgentDefaults>(d => d != null)),
                Times.Never);
        }
    }

    // =========================================================================
    // Controller — ReplaceSubAgentsConfigAsync (create from default template)
    // =========================================================================

    [TestFixture]
    public class SubAgentsControllerReplaceTests
    {
        private Mock<ISubAgentsConfigManager> _configManagerMock;
        private SubAgentsController _controller;

        [SetUp]
        public void SetUp()
        {
            _configManagerMock = new Mock<ISubAgentsConfigManager>();
            _configManagerMock
                .Setup(m => m.SaveAsync(It.IsAny<SubAgentsConfig>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _controller = new SubAgentsController(_configManagerMock.Object);
        }

        [TearDown]
        public void TearDown()
        {
            _controller = null;
            _configManagerMock = null;
        }

        [Test]
        public async Task ReplaceSubAgentsConfigAsync_ValidPayload_SavesConfigAndReturnsSuccess()
        {
            var json = @"{ ""providerType"": ""lmstudio"", ""customBaseUrl"": ""http://localhost:1234"", ""model"": ""google/gemma-4-e2b"", ""agents"": [ { ""id"": ""code_explorer_subagent"", ""displayName"": ""Code explorer"", ""description"": ""Desc"", ""customBaseUrl"": ""http://localhost:1234"", ""model"": ""google/gemma-4-e2b"", ""enabled"": true } ] }";

            var resultJson = await _controller.ReplaceSubAgentsConfigAsync(json);
            var result = resultJson.FromJson<SubAgentsUpdateResponse>();

            Assert.That(result.Success, Is.True);
            _configManagerMock.Verify(
                m => m.SaveAsync(
                    It.Is<SubAgentsConfig>(c => c.Agents.Count == 1 && c.Agents[0].Id == "code_explorer_subagent"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ReplaceSubAgentsConfigAsync_EmptyAgents_ReturnsFailureAndDoesNotSave()
        {
            var json = @"{ ""providerType"": ""lmstudio"", ""agents"": [] }";

            var resultJson = await _controller.ReplaceSubAgentsConfigAsync(json);
            var result = resultJson.FromJson<SubAgentsUpdateResponse>();

            Assert.That(result.Success, Is.False);
            _configManagerMock.Verify(
                m => m.SaveAsync(It.IsAny<SubAgentsConfig>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ReplaceSubAgentsConfigAsync_WhenSaveThrows_ReturnsFailure()
        {
            _configManagerMock
                .Setup(m => m.SaveAsync(It.IsAny<SubAgentsConfig>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ArgumentException("invalid"));

            var json = @"{ ""agents"": [ { ""id"": ""a"", ""description"": ""D"", ""customBaseUrl"": ""http://x"", ""model"": ""m"" } ] }";

            var resultJson = await _controller.ReplaceSubAgentsConfigAsync(json);
            var result = resultJson.FromJson<SubAgentsUpdateResponse>();

            Assert.That(result.Success, Is.False);
            _configManagerMock.Verify(
                m => m.SaveAsync(It.IsAny<SubAgentsConfig>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
