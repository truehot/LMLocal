using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Infrastructure.ModelsConfig;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class ModelsConfigManagerTests
    {
        private static string ExpectedPath(string folder)
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                folder,
                "models.config.json"
            );
        }

        [Test]
        public async Task GetAsync_ReturnsEmptyConfig_WhenFileMissing()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var manager = new ModelsConfigManager(fs, settings);

            var result = await manager.GetAsync();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Models, Is.Not.Null);
            Assert.That(result.Models, Has.Count.EqualTo(0));
        }

        [Test]
        public async Task GetAsync_ReturnsStoredModels_WhenFileExists()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var path = ExpectedPath(settings.LocalAppDataFolder);

            var config = new ModelsConfigFile
            {
                Models = new List<ModelDefinition>
                {
                    new ModelDefinition
                    {
                        Id = 1,
                        ModelId = "qwen2.5-coder-32b-instruct",
                        ProviderType = "lmstudio",
                        ProviderId = null,
                        DisplayName = "Qwen Coder",
                        ContextLength = 131072,
                        IsCustom = false,
                        Enabled = true
                    }
                }
            };

            await fs.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(config.ToJson()));

            var manager = new ModelsConfigManager(fs, settings);
            var result = await manager.GetAsync();

            Assert.That(result.Models, Has.Count.EqualTo(1));
            Assert.That(result.Models[0].Id, Is.EqualTo(1));
            Assert.That(result.Models[0].ModelId, Is.EqualTo("qwen2.5-coder-32b-instruct"));
            Assert.That(result.Models[0].ProviderType, Is.EqualTo("lmstudio"));
            Assert.That(result.Models[0].DisplayName, Is.EqualTo("Qwen Coder"));
            Assert.That(result.Models[0].ContextLength, Is.EqualTo(131072));
        }

        [Test]
        public async Task UpdateAsync_WritesModelsToFile()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var path = ExpectedPath(settings.LocalAppDataFolder);

            var manager = new ModelsConfigManager(fs, settings);
            var config = new ModelsConfigFile
            {
                Models = new List<ModelDefinition>
                {
                    new ModelDefinition { Id = 7, ModelId = "deepseek-chat", ProviderType = "openai", ProviderId = 3 }
                }
            };

            await manager.UpdateAsync(config);

            Assert.That(fs.FileExists(path), Is.True);
            var storedJson = fs.ReadAllText(path);
            Assert.That(storedJson, Contains.Substring("models"));
            Assert.That(storedJson, Contains.Substring("deepseek-chat"));
            Assert.That(storedJson, Contains.Substring("\"providerId\":3"));
        }

        [Test]
        public async Task UpdateAsync_WritesEmptyList_WhenConfigNull()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var path = ExpectedPath(settings.LocalAppDataFolder);

            var manager = new ModelsConfigManager(fs, settings);
            await manager.UpdateAsync(null);

            Assert.That(fs.FileExists(path), Is.True);
            var storedJson = fs.ReadAllText(path);
            Assert.That(storedJson, Contains.Substring("models"));
        }

        [Test]
        public async Task UpdateAsync_OmitsUnsetOptionalFields()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var path = ExpectedPath(settings.LocalAppDataFolder);

            var manager = new ModelsConfigManager(fs, settings);
            var config = new ModelsConfigFile
            {
                Models = new List<ModelDefinition>
                {
                    // Optional fields intentionally left unset: "not set" must not persist as 0.
                    new ModelDefinition { Id = 1, ModelId = "model-a", ProviderType = "ollama" }
                }
            };

            await manager.UpdateAsync(config);

            var storedJson = fs.ReadAllText(path);
            Assert.That(storedJson, Does.Not.Contain("contextLength"));
            Assert.That(storedJson, Does.Not.Contain("maxTokens"));
            Assert.That(storedJson, Does.Not.Contain("reasoningEffort"));
            Assert.That(storedJson, Does.Not.Contain("displayName"));
            Assert.That(storedJson, Does.Not.Contain("providerId"));
        }

        [Test]
        public async Task GetAsync_ReturnsEmptyConfig_WhenFileIsCorrupted()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var path = ExpectedPath(settings.LocalAppDataFolder);

            await fs.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("{ not valid json "));

            var manager = new ModelsConfigManager(fs, settings);
            var result = await manager.GetAsync();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Models, Has.Count.EqualTo(0));
        }

        [Test]
        public async Task GetAsync_NormalizesNullModelsList()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var path = ExpectedPath(settings.LocalAppDataFolder);

            await fs.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("{ \"models\": null }"));

            var manager = new ModelsConfigManager(fs, settings);
            var result = await manager.GetAsync();

            Assert.That(result.Models, Is.Not.Null);
            Assert.That(result.Models, Has.Count.EqualTo(0));
        }

        [Test]
        public async Task UpdateAsync_ConcurrentWrites_DoNotInterleave()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var path = ExpectedPath(settings.LocalAppDataFolder);

            var manager = new ModelsConfigManager(fs, settings);

            var first = manager.UpdateAsync(new ModelsConfigFile
            {
                Models = new List<ModelDefinition> { new ModelDefinition { Id = 1, ModelId = "one", ProviderType = "ollama" } }
            });
            var second = manager.UpdateAsync(new ModelsConfigFile
            {
                Models = new List<ModelDefinition> { new ModelDefinition { Id = 2, ModelId = "two", ProviderType = "ollama" } }
            });

            await Task.WhenAll(first, second);

            // Whatever won, the stored document must still be a single valid config.
            var storedJson = fs.ReadAllText(path);
            var reloaded = storedJson.FromJson<ModelsConfigFile>();

            Assert.That(reloaded, Is.Not.Null);
            Assert.That(reloaded.Models, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task RoundTrip_PreservesAllFields()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };

            var manager = new ModelsConfigManager(fs, settings);
            var config = new ModelsConfigFile
            {
                Models = new List<ModelDefinition>
                {
                    new ModelDefinition
                    {
                        Id = 5,
                        ModelId = "kimi-k2.7-code",
                        ProviderType = "openai",
                        ProviderId = 12,
                        DisplayName = "Kimi",
                        ContextLength = 256000,
                        MaxTokens = 4096,
                        ReasoningEffort = "high",
                        IsCustom = true,
                        Enabled = false
                    }
                }
            };

            await manager.UpdateAsync(config);
            var result = await manager.GetAsync();

            Assert.That(result.Models[0], Is.EqualTo(config.Models[0]));
        }

        // ── helper ──────────────────────────────────────────────────

        private class TestSettingsManager : ISettingsManager
        {
            public AppSettings Current => new AppSettings();
            public Task<AppSettings> LoadAsync(System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(new AppSettings());
            public Task SaveAsync(AppSettings settings, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task SetAiToolsModeAsync(string mode, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task SetSubAgentsEnabledAsync(bool enabled, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
#pragma warning disable 0067
            public event Action<AppSettings> SettingsChanged;
#pragma warning restore 0067

            public string ApplicationName => "LMLocalChat";
            public string SettingsFileName => "settings.json";
            public string LocalAppDataFolder { get; set; } = "LMLocalChat";
            public string LocalAppSettingFileName => "settings.json";
            public string LocalAppInstructionsFileName => "instructions.json";
            public string LocalAppMcpFileName => "mcp.json";
            public string WebViewUserDataFolder => "WebViewData";
            public string ChatHistoryFolder => "ChatHistory";
            public string ChatHistoryFileLabel => "chat_";
            public string HtmlResourcePath => "Resources/app.html";
            public string VirtualHostName => "app.local";
            public string SystemPrompt => string.Empty;
            public int BatchIntervalMs => 100;
            public int WindowSeconds => 5;
            public int RequestTimeoutSeconds => 15;
            public string SnapshotFolder => "Snapshots";
            public string LocalSnapshotsFileName => "manifest.json";
            public string UserAgent => "LMLocalChat/1.0";
            public string AssistantPlaceholder => string.Empty;
        }
    }
}
