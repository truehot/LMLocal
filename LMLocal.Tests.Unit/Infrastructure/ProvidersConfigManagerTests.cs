using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Api;
using LMLocal.Infrastructure.LlmApi.Provider;
using LMLocal.Infrastructure.Providers;
using LMLocal.Infrastructure.Settings;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class ProvidersConfigManagerTests
    {
        // ── original tests ──────────────────────────────────────────

        [Test]
        public async Task GetAsync_ReturnsDefaultProviders_WhenFileMissing()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var manager = new ProvidersConfigManager(fs, settings);

            var result = await manager.GetAsync();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.DefaultProviders, Is.Not.Null);
            Assert.That(result.DefaultProviders.Count, Is.GreaterThan(0));
            Assert.That(result.Providers, Is.Not.Null);
        }

        [Test]
        public async Task GetAsync_ReturnsStoredProviders_WhenFileExists()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var expectedPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                settings.LocalAppDataFolder,
                "providers.json"
            );

            var customProvider = new CustomProvider
            {
                Id = 99,
                ProviderName = "Test Provider",
                ProviderType = "custom",
                CustomBaseUrl = "http://test:9999",
                CustomApiKey = "test-key"
            };

            var config = new ProvidersConfigFile
            {
                DefaultProviders = new List<CustomProvider>(),
                Providers = new List<CustomProvider> { customProvider }
            };

            var json = config.ToJson();
            await fs.WriteAllBytesAsync(expectedPath, Encoding.UTF8.GetBytes(json));

            var manager = new ProvidersConfigManager(fs, settings);
            var result = await manager.GetAsync();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Providers, Is.Not.Null);
        }

        [Test]
        public async Task UpdateAsync_WritesProvidersToFile()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var expectedPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                settings.LocalAppDataFolder,
                "providers.json"
            );

            var manager = new ProvidersConfigManager(fs, settings);
            var config = new ProvidersConfigFile
            {
                DefaultProviders = new List<CustomProvider>(),
                Providers = new List<CustomProvider>
                {
                    new CustomProvider
                    {
                        Id = 1,
                        ProviderName = "My Provider",
                        ProviderType = "openai",
                        CustomBaseUrl = "http://localhost:8000",
                        CustomApiKey = "my-api-key"
                    }
                }
            };

            await manager.UpdateAsync(config);

            Assert.That(fs.FileExists(expectedPath), Is.True);
            var storedJson = fs.ReadAllText(expectedPath);
            Assert.That(storedJson, Is.Not.Empty);
        }

        [Test]
        public async Task UpdateAsync_WritesDefaultProviders_WhenConfigNull()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var expectedPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                settings.LocalAppDataFolder,
                "providers.json"
            );

            var manager = new ProvidersConfigManager(fs, settings);
            await manager.UpdateAsync(null);

            Assert.That(fs.FileExists(expectedPath), Is.True);
            var storedJson = fs.ReadAllText(expectedPath);
            Assert.That(storedJson, Is.Not.Empty);
        }

        [Test]
        public async Task UpdateAsync_IncludesDefaultProviders_InStoredConfig()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var expectedPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                settings.LocalAppDataFolder,
                "providers.json"
            );

            var manager = new ProvidersConfigManager(fs, settings);
            var config = new ProvidersConfigFile
            {
                DefaultProviders = null,
                Providers = new List<CustomProvider>()
            };

            await manager.UpdateAsync(config);

            var storedJson = fs.ReadAllText(expectedPath);
            Assert.That(storedJson, Contains.Substring("defaultProviders"));
        }

        // ── new tests for BuildDefaultProviders ─────────────────────

        [Test]
        public void BuildDefaultProviders_ReturnsFiveProviders()
        {
            var result = ProvidersConfigManager.BuildDefaultProviders();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(5));
        }

        [Test]
        public void BuildDefaultProviders_HasExpectedIds()
        {
            var result = ProvidersConfigManager.BuildDefaultProviders();

            Assert.That(result[0].Id, Is.EqualTo(0));
            Assert.That(result[1].Id, Is.EqualTo(1));
            Assert.That(result[2].Id, Is.EqualTo(2));
            Assert.That(result[3].Id, Is.EqualTo(4));
            Assert.That(result[4].Id, Is.EqualTo(3));
        }

        [Test]
        public void BuildDefaultProviders_IdsAreUnique()
        {
            var result = ProvidersConfigManager.BuildDefaultProviders();
            var ids = new List<int>();
            foreach (var p in result) ids.Add(p.Id);
            Assert.That(ids.Count, Is.EqualTo(5));
            Assert.That(new HashSet<int>(ids).Count, Is.EqualTo(5));
        }

        [Test]
        public void BuildDefaultProviders_ProviderNamesComeFromEnumAttribute()
        {
            var result = ProvidersConfigManager.BuildDefaultProviders();

            Assert.That(result[0].ProviderName, Is.EqualTo("LM Studio (local)"));
            Assert.That(result[1].ProviderName, Is.EqualTo("Ollama (local)"));
            Assert.That(result[2].ProviderName, Is.EqualTo("Jan (local)"));
            Assert.That(result[3].ProviderName, Is.EqualTo("Llama.cpp (local)"));
            Assert.That(result[4].ProviderName, Is.EqualTo("OpenAI compatible"));
        }

        [Test]
        public void BuildDefaultProviders_ProviderTypesMatchEnumKeys()
        {
            var result = ProvidersConfigManager.BuildDefaultProviders();

            Assert.That(result[0].ProviderType, Is.EqualTo("lmstudio"));
            Assert.That(result[1].ProviderType, Is.EqualTo("ollama"));
            Assert.That(result[2].ProviderType, Is.EqualTo("jan"));
            Assert.That(result[3].ProviderType, Is.EqualTo("llamacpp"));
            Assert.That(result[4].ProviderType, Is.EqualTo("openai"));
        }

        [Test]
        public void BuildDefaultProviders_BaseUrlsAreCorrect()
        {
            var result = ProvidersConfigManager.BuildDefaultProviders();

            Assert.That(result[0].CustomBaseUrl, Is.EqualTo("http://localhost:1234"));
            Assert.That(result[1].CustomBaseUrl, Is.EqualTo("http://localhost:11434"));
            Assert.That(result[2].CustomBaseUrl, Is.EqualTo("http://localhost:1337"));
            Assert.That(result[3].CustomBaseUrl, Is.EqualTo("http://localhost:8080"));
            Assert.That(result[4].CustomBaseUrl, Is.EqualTo(string.Empty));
        }

        [Test]
        public void BuildDefaultProviders_ApiKeyIsAlwaysEmpty()
        {
            var result = ProvidersConfigManager.BuildDefaultProviders();
            foreach (var p in result)
                Assert.That(p.CustomApiKey, Is.EqualTo(string.Empty));
        }

        [Test]
        public void BuildDefaultProviders_ProviderNameMatchesGetDisplayName_ForAllEnumValues()
        {
            var result = ProvidersConfigManager.BuildDefaultProviders();
            foreach (var p in result)
            {
                var parsed = Enum.TryParse<ModelProvider>(p.ProviderType, ignoreCase: true, out var mp);
                Assert.That(parsed, Is.True, $"Unknown provider type: {p.ProviderType}");
                var expectedDisplay = ProviderResolver.GetDisplayName(mp);
                Assert.That(p.ProviderName, Is.EqualTo(expectedDisplay),
                    $"Display name mismatch for {p.ProviderType}");
            }
        }

        // ── helper ──────────────────────────────────────────────────

        private class TestSettingsManager : ISettingsManager
        {
            public AppSettings Current => new AppSettings();
            public Task<AppSettings> LoadAsync(System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(new AppSettings());
            public Task SaveAsync(AppSettings settings, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
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
            public string AssistantPlaceholder => throw new NotImplementedException();
        }
    }
}
