using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.RecentModels;
using LMLocal.Tests.Unit.Infrastructure;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class RecentModelsManagerTests
    {
        private InMemoryFileSystem _fs;
        private TestSettingsManager _settings;
        private RecentModelsManager _manager;
        private string _filePath;

        [SetUp]
        public void SetUp()
        {
            _fs = new InMemoryFileSystem();
            _settings = new TestSettingsManager
            {
                LocalAppDataFolder = "LMLocalChat",
                CurrentSettings = new AppSettings { Provider = "lmstudio", ProviderId = null }
            };
            _manager = new RecentModelsManager(_fs, _settings);
            _filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                _settings.LocalAppDataFolder,
                "recent-models.json"
            );
        }

        // ── RecordUsage ─────────────────────────────────────────────

        [Test]
        public async Task RecordUsage_NewEntry_AppendedAtTop()
        {
            await _manager.RecordUsageAsync("lmstudio", null, "model-a", "Model A");

            var result = await _manager.GetForCurrentProviderAsync();

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].ModelId, Is.EqualTo("model-a"));
            Assert.That(result[0].ModelName, Is.EqualTo("Model A"));
            Assert.That(result[0].ProviderType, Is.EqualTo("lmstudio"));
            Assert.That(result[0].ProviderId, Is.Null);
        }

        [Test]
        public async Task RecordUsage_ExistingEntry_UpdatedAndMovedToTop()
        {
            SeedFile(new List<RecentModelEntry>
            {
                new RecentModelEntry { ProviderType = "lmstudio", ProviderId = null, ModelId = "model-b", ModelName = "Model B", LastUsedUtc = DateTimeOffset.UtcNow.AddHours(-1) },
                new RecentModelEntry { ProviderType = "lmstudio", ProviderId = null, ModelId = "model-a", ModelName = "Old Name", LastUsedUtc = DateTimeOffset.UtcNow.AddHours(-5) }
            });

            await _manager.RecordUsageAsync("lmstudio", null, "model-a", "Model A Renamed");

            var result = await _manager.GetForCurrentProviderAsync();

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].ModelId, Is.EqualTo("model-a"));
            Assert.That(result[0].ModelName, Is.EqualTo("Model A Renamed"));
            Assert.That(result[1].ModelId, Is.EqualTo("model-b"));
        }

        [Test]
        public async Task RecordUsage_SameModelIdDifferentProviders_TwoEntries()
        {
            await _manager.RecordUsageAsync("lmstudio", null, "model-a", "Model A");
            await _manager.RecordUsageAsync("openai", 3, "model-a", "Model A Cloud");

            var file = ReadFile();

            Assert.That(file.Entries, Has.Count.EqualTo(2));

            var result = await _manager.GetForCurrentProviderAsync();
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].ProviderType, Is.EqualTo("lmstudio"));
        }

        [Test]
        public async Task RecordUsage_TrimsToCap50_OldestDropped()
        {
            // Record 51 distinct models through the manager itself (guarantees newest-first order).
            for (int i = 0; i < 51; i++)
            {
                await _manager.RecordUsageAsync("lmstudio", null, "model-" + i.ToString("D2"), "Model " + i);
            }

            var file = ReadFile();

            Assert.That(file.Entries, Has.Count.EqualTo(50));
            Assert.That(file.Entries[0].ModelId, Is.EqualTo("model-50"), "Newest entry must be first");
            Assert.That(file.Entries.Any(e => e.ModelId == "model-00"), Is.False, "Oldest entry must be dropped");
        }

        // ── GetForCurrentProvider ───────────────────────────────────

        [Test]
        public async Task GetForCurrentProvider_FiltersByProviderTypeAndId()
        {
            SeedFile(new List<RecentModelEntry>
            {
                new RecentModelEntry { ProviderType = "lmstudio", ProviderId = null, ModelId = "local-old", ModelName = "Local Old", LastUsedUtc = DateTimeOffset.UtcNow.AddHours(-2) },
                new RecentModelEntry { ProviderType = "lmstudio", ProviderId = 3,   ModelId = "profile",   ModelName = "Profile",    LastUsedUtc = DateTimeOffset.UtcNow.AddHours(-1) },
                new RecentModelEntry { ProviderType = "openai",   ProviderId = null, ModelId = "cloud",     ModelName = "Cloud",      LastUsedUtc = DateTimeOffset.UtcNow.AddMinutes(-30) },
                new RecentModelEntry { ProviderType = "lmstudio", ProviderId = null, ModelId = "local-new", ModelName = "Local New",  LastUsedUtc = DateTimeOffset.UtcNow.AddMinutes(-10) }
            });

            var result = await _manager.GetForCurrentProviderAsync();

            Assert.That(result.Select(e => e.ModelId).ToList(),
                Is.EqualTo(new[] { "local-new", "local-old" }));
        }

        [Test]
        public async Task GetForCurrentProvider_MissingFile_ReturnsEmpty()
        {
            var result = await _manager.GetForCurrentProviderAsync();

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task GetForCurrentProvider_CorruptJson_ReturnsEmpty()
        {
            _fs.WriteAllBytesAsync(_filePath, Encoding.UTF8.GetBytes("{ this is not json !!!"), System.Threading.CancellationToken.None).GetAwaiter().GetResult();

            var result = await _manager.GetForCurrentProviderAsync();

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
        }

        // ── helpers ─────────────────────────────────────────────────

        private void SeedFile(List<RecentModelEntry> entries)
        {
            var json = new RecentModelsFile { Entries = entries }.ToJson();
            _fs.WriteAllBytesAsync(_filePath, Encoding.UTF8.GetBytes(json), System.Threading.CancellationToken.None).GetAwaiter().GetResult();
        }

        private RecentModelsFile ReadFile()
        {
            var json = _fs.ReadAllText(_filePath);
            return json.FromJson<RecentModelsFile>();
        }

        private class TestSettingsManager : LMLocal.Application.Abstractions.Ports.ISettingsManager
        {
            public AppSettings CurrentSettings { get; set; } = new AppSettings();
            public AppSettings Current => CurrentSettings;

            public Task<AppSettings> LoadAsync(System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(CurrentSettings);
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
