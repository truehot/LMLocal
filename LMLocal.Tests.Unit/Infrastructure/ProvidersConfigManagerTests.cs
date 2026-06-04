using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using LMLocal.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure;
using LMLocal.Infrastructure.Providers;
using LMLocal.Infrastructure.Settings;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class ProvidersConfigManagerTests
    {
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
            public string ChatHistoryFilePrefix => "chat_";
            public string HtmlResourcePath => "Resources/app.html";
            public string VirtualHostName => "app.local";
            public string SystemPrompt => string.Empty;
            public int BatchIntervalMs => 100;
            public int WindowSeconds => 5;
            public int RequestTimeoutSeconds => 15;
        }
    }
}
