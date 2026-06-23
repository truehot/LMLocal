using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Tests.Unit.Infrastructure;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling
{
    /// <summary>
    /// Unit tests for <see cref="ToolsConfigManager"/>.
    /// Validates configuration file loading, caching, saving, and lifecycle management.
    /// </summary>
    [TestFixture]
    public class ToolsConfigManagerTests
    {
        /// <summary>
        /// Helper method to build the expected file path based on the same logic used by ToolsConfigManager.
        /// </summary>
        private static string BuildExpectedFilePath(string localAppDataFolder)
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                localAppDataFolder ?? "LMLocalChat",
                "tools.json"
            );
        }

        /// <summary>
        /// Helper to create a default empty ToolsConfigFile (same as ToolsConfigManager.GetDefaultTools).
        /// </summary>
        private static ToolsConfigFile CreateDefaultConfig()
        {
            return new ToolsConfigFile
            {
                Tools = new System.Collections.Generic.List<ToolConfig>()
            };
        }

        #region Constructor Tests

        /// <summary>
        /// Verifies that the constructor throws ArgumentNullException when fileSystem is null.
        /// </summary>
        [Test]
        public void Constructor_ThrowsArgumentNullException_WhenFileSystemIsNull()
        {
            var settings = new TestSettingsManager();

            var ex = Assert.Throws<ArgumentNullException>(() =>
                new ToolsConfigManager(null, settings));

            Assert.That(ex.ParamName, Is.EqualTo("fileSystem"));
        }

        /// <summary>
        /// Verifies that the constructor throws ArgumentNullException when settingsManager is null.
        /// </summary>
        [Test]
        public void Constructor_ThrowsArgumentNullException_WhenSettingsManagerIsNull()
        {
            var fs = new InMemoryFileSystem();

            var ex = Assert.Throws<ArgumentNullException>(() =>
                new ToolsConfigManager(fs, null));

            Assert.That(ex.ParamName, Is.EqualTo("settingsManager"));
        }

        /// <summary>
        /// Verifies that the constructor validates the file path via IFileSystem.ValidateFilePath.
        /// </summary>
        [Test]
        public void Constructor_ValidatesFilePath()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };

            // Should not throw; InMemoryFileSystem.ValidateFilePath is a real validation.
            Assert.DoesNotThrow(() => new ToolsConfigManager(fs, settings));
        }

        #endregion

        #region LoadAsync Tests

        /// <summary>
        /// Verifies that LoadAsync returns a default configuration when the file does not exist.
        /// </summary>
        [Test]
        public async Task LoadAsync_ReturnsDefaultConfig_WhenFileMissing()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var manager = new ToolsConfigManager(fs, settings);

            var result = await manager.LoadAsync();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Tools, Is.Not.Null);
            Assert.That(result.Tools.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that LoadAsync returns a default configuration when the file exists but is empty.
        /// </summary>
        [Test]
        public async Task LoadAsync_ReturnsDefaultConfig_WhenFileIsEmpty()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var expectedPath = BuildExpectedFilePath(settings.LocalAppDataFolder);

            // Write an empty file
            await fs.WriteAllBytesAsync(expectedPath, Array.Empty<byte>());

            var manager = new ToolsConfigManager(fs, settings);
            var result = await manager.LoadAsync();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Tools, Is.Not.Null);
            Assert.That(result.Tools.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that LoadAsync returns a default configuration when the file contains only whitespace.
        /// </summary>
        [Test]
        public async Task LoadAsync_ReturnsDefaultConfig_WhenFileIsWhitespace()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var expectedPath = BuildExpectedFilePath(settings.LocalAppDataFolder);

            await fs.WriteAllBytesAsync(expectedPath, Encoding.UTF8.GetBytes("   \n  \t  "));

            var manager = new ToolsConfigManager(fs, settings);
            var result = await manager.LoadAsync();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Tools, Is.Not.Null);
            Assert.That(result.Tools.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that LoadAsync returns a default configuration when the file contains invalid JSON.
        /// </summary>
        [Test]
        public async Task LoadAsync_ReturnsDefaultConfig_WhenFileContainsInvalidJson()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var expectedPath = BuildExpectedFilePath(settings.LocalAppDataFolder);

            await fs.WriteAllBytesAsync(expectedPath, Encoding.UTF8.GetBytes("{ invalid json }"));

            var manager = new ToolsConfigManager(fs, settings);
            var result = await manager.LoadAsync();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Tools, Is.Not.Null);
            Assert.That(result.Tools.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that LoadAsync successfully parses valid JSON and returns the stored configuration.
        /// </summary>
        [Test]
        public async Task LoadAsync_ReturnsStoredConfig_WhenFileExistsWithValidJson()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var expectedPath = BuildExpectedFilePath(settings.LocalAppDataFolder);

            var storedConfig = new ToolsConfigFile
            {
                Tools = new System.Collections.Generic.List<ToolConfig>
                {
                    new ToolConfig { Id = "list-directory-contents", Enabled = true },
                    new ToolConfig { Id = "read-file", Enabled = false }
                }
            };

            var json = storedConfig.ToJson();
            await fs.WriteAllBytesAsync(expectedPath, Encoding.UTF8.GetBytes(json));

            var manager = new ToolsConfigManager(fs, settings);
            var result = await manager.LoadAsync();

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Tools.Count, Is.EqualTo(2));
            Assert.That(result.Tools[0].Id, Is.EqualTo("list-directory-contents"));
            Assert.That(result.Tools[0].Enabled, Is.True);
            Assert.That(result.Tools[1].Id, Is.EqualTo("read-file"));
            Assert.That(result.Tools[1].Enabled, Is.False);
        }

        /// <summary>
        /// Verifies that LoadAsync caches the configuration so that Current returns the same instance.
        /// </summary>
        [Test]
        public async Task LoadAsync_CachesConfig_AndCurrentReturnsSameInstance()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var expectedPath = BuildExpectedFilePath(settings.LocalAppDataFolder);

            var storedConfig = new ToolsConfigFile
            {
                Tools = new System.Collections.Generic.List<ToolConfig>
                {
                    new ToolConfig { Id = "test-tool", Enabled = true }
                }
            };
            await fs.WriteAllBytesAsync(expectedPath, Encoding.UTF8.GetBytes(storedConfig.ToJson()));

            var manager = new ToolsConfigManager(fs, settings);
            var loaded = await manager.LoadAsync();

            Assert.That(manager.Current, Is.SameAs(loaded));
            Assert.That(manager.Current.Tools[0].Id, Is.EqualTo("test-tool"));
        }

        #endregion



        #region Current Property Tests

        /// <summary>
        /// Verifies that accessing Current before calling LoadAsync throws InvalidOperationException.
        /// </summary>
        [Test]
        public void Current_ThrowsInvalidOperationException_WhenNotLoaded()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager();
            var manager = new ToolsConfigManager(fs, settings);

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                var _ = manager.Current;
            });

            Assert.That(ex.Message, Does.Contain("not loaded"));
        }

        /// <summary>
        /// Verifies that Current returns the cached configuration after LoadAsync succeeds.
        /// </summary>
        [Test]
        public async Task Current_ReturnsConfig_AfterLoadAsync()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var expectedPath = BuildExpectedFilePath(settings.LocalAppDataFolder);

            var config = new ToolsConfigFile
            {
                Tools = new System.Collections.Generic.List<ToolConfig>
                {
                    new ToolConfig { Id = "tool-1", Enabled = true }
                }
            };
            await fs.WriteAllBytesAsync(expectedPath, Encoding.UTF8.GetBytes(config.ToJson()));

            var manager = new ToolsConfigManager(fs, settings);
            await manager.LoadAsync();

            Assert.That(manager.Current, Is.Not.Null);
            Assert.That(manager.Current.Tools.Count, Is.EqualTo(1));
            Assert.That(manager.Current.Tools[0].Id, Is.EqualTo("tool-1"));
        }

        #endregion

        #region SaveAsync Tests

        /// <summary>
        /// Verifies that SaveAsync writes the configuration to disk and updates the cache.
        /// </summary>
        [Test]
        public async Task SaveAsync_WritesConfigToFile()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var expectedPath = BuildExpectedFilePath(settings.LocalAppDataFolder);

            var manager = new ToolsConfigManager(fs, settings);
            var config = new ToolsConfigFile
            {
                Tools = new System.Collections.Generic.List<ToolConfig>
                {
                    new ToolConfig { Id = "saved-tool", Enabled = false }
                }
            };

            await manager.SaveAsync(config);

            Assert.That(fs.FileExists(expectedPath), Is.True);
            var storedJson = fs.ReadAllText(expectedPath);
            Assert.That(storedJson, Is.Not.Empty);

            // Verify the saved JSON can be parsed back and matches the original
            var savedConfig = storedJson.FromJson<ToolsConfigFile>();
            Assert.That(savedConfig, Is.Not.Null);
            Assert.That(savedConfig.Tools.Count, Is.EqualTo(1));
            Assert.That(savedConfig.Tools[0].Id, Is.EqualTo("saved-tool"));
            Assert.That(savedConfig.Tools[0].Enabled, Is.False);
        }

        /// <summary>
        /// Verifies that SaveAsync updates the cache so that Current returns the saved config.
        /// </summary>
        [Test]
        public async Task SaveAsync_UpdatesCache()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var manager = new ToolsConfigManager(fs, settings);

            // Load default config first
            await manager.LoadAsync();
            Assert.That(manager.Current.Tools.Count, Is.EqualTo(0));

            // Save a new config
            var newConfig = new ToolsConfigFile
            {
                Tools = new System.Collections.Generic.List<ToolConfig>
                {
                    new ToolConfig { Id = "new-tool", Enabled = true }
                }
            };
            await manager.SaveAsync(newConfig);

            // Current should now reflect the saved config
            Assert.That(manager.Current.Tools.Count, Is.EqualTo(1));
            Assert.That(manager.Current.Tools[0].Id, Is.EqualTo("new-tool"));
        }

        /// <summary>
        /// Verifies that SaveAsync throws ArgumentNullException when config is null.
        /// </summary>
        [Test]
        public void SaveAsync_ThrowsArgumentNullException_WhenConfigIsNull()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager();
            var manager = new ToolsConfigManager(fs, settings);

            var ex = Assert.ThrowsAsync<ArgumentNullException>(() =>
                manager.SaveAsync(null));

            Assert.That(ex.ParamName, Is.EqualTo("config"));
        }

        /// <summary>
        /// Verifies that SaveAsync respects cancellation via CancellationToken.
        /// </summary>
        [Test]
        public void SaveAsync_RespectsCancellation()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var manager = new ToolsConfigManager(fs, settings);
            var config = new ToolsConfigFile
            {
                Tools = new System.Collections.Generic.List<ToolConfig>()
            };

            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                var ex = Assert.ThrowsAsync<OperationCanceledException>(() =>
                    manager.SaveAsync(config, cts.Token));

                Assert.That(ex, Is.TypeOf<OperationCanceledException>());
            }
        }

        /// <summary>
        /// Verifies that multiple sequential SaveAsync calls all persist the data correctly.
        /// </summary>
        [Test]
        public async Task SaveAsync_MultipleCalls_PersistAll()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "LMLocalChat" };
            var expectedPath = BuildExpectedFilePath(settings.LocalAppDataFolder);

            var manager = new ToolsConfigManager(fs, settings);

            var config1 = new ToolsConfigFile
            {
                Tools = new System.Collections.Generic.List<ToolConfig>
                {
                    new ToolConfig { Id = "tool-a", Enabled = true }
                }
            };
            await manager.SaveAsync(config1);

            var config2 = new ToolsConfigFile
            {
                Tools = new System.Collections.Generic.List<ToolConfig>
                {
                    new ToolConfig { Id = "tool-b", Enabled = false }
                }
            };
            await manager.SaveAsync(config2);

            // Only the last saved config should exist on disk
            var storedJson = fs.ReadAllText(expectedPath);
            var savedConfig = storedJson.FromJson<ToolsConfigFile>();
            Assert.That(savedConfig.Tools.Count, Is.EqualTo(1));
            Assert.That(savedConfig.Tools[0].Id, Is.EqualTo("tool-b"));
        }

        #endregion

        #region Dispose Tests

        /// <summary>
        /// Verifies that after disposal, LoadAsync throws ObjectDisposedException.
        /// </summary>
        [Test]
        public void Dispose_LoadAsync_ThrowsObjectDisposedException()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager();
            var manager = new ToolsConfigManager(fs, settings);

            manager.Dispose();

            var ex = Assert.ThrowsAsync<ObjectDisposedException>(() =>
                manager.LoadAsync());

            Assert.That(ex.ObjectName, Is.EqualTo("ToolsConfigManager"));
        }

        /// <summary>
        /// Verifies that after disposal, SaveAsync throws ObjectDisposedException.
        /// </summary>
        [Test]
        public void Dispose_SaveAsync_ThrowsObjectDisposedException()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager();
            var manager = new ToolsConfigManager(fs, settings);

            manager.Dispose();

            var ex = Assert.ThrowsAsync<ObjectDisposedException>(() =>
                manager.SaveAsync(new ToolsConfigFile()));

            Assert.That(ex.ObjectName, Is.EqualTo("ToolsConfigManager"));
        }

        /// <summary>
        /// Verifies that after disposal, accessing Current throws ObjectDisposedException.
        /// </summary>
        [Test]
        public void Dispose_Current_ThrowsObjectDisposedException()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager();
            var manager = new ToolsConfigManager(fs, settings);

            manager.Dispose();

            var ex = Assert.Throws<ObjectDisposedException>(() =>
            {
                var _ = manager.Current;
            });

            Assert.That(ex.ObjectName, Is.EqualTo("ToolsConfigManager"));
        }

        /// <summary>
        /// Verifies that Dispose can be called multiple times without throwing.
        /// </summary>
        [Test]
        public void Dispose_MultipleCalls_DoesNotThrow()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager();
            var manager = new ToolsConfigManager(fs, settings);

            Assert.DoesNotThrow(() =>
            {
                manager.Dispose();
                manager.Dispose();
                manager.Dispose();
            });
        }

        #endregion

        #region Test Settings Manager

        /// <summary>
        /// Test implementation of <see cref="ISettingsManager"/> for unit testing.
        /// </summary>
        private class TestSettingsManager : ISettingsManager
        {
            public AppSettings Current => new AppSettings();
            public Task<AppSettings> LoadAsync(System.Threading.CancellationToken cancellationToken = default)
                => Task.FromResult(new AppSettings());
            public Task SaveAsync(AppSettings settings, System.Threading.CancellationToken cancellationToken = default)
                => Task.CompletedTask;
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
            public string SnapshotFolder => "Snapshots";
            public string LocalSnapshotsFileName => "manifest.json";

            public string UserAgent => "LMLocalChat/1.0";
        }

        #endregion
    }
}
