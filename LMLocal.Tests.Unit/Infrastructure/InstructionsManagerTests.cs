using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure;
using LMLocal.Services;
using LMLocal.Models;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class InstructionsManagerTests
    {
        [Test]
        public async Task GetAsync_ReturnsEmptyObject_WhenFileMissing()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "", LocalAppInstructionsFileName = "instructions.json" };
            var manager = new InstructionsManager(fs, settings);
            var expectedPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), settings.LocalAppDataFolder, settings.LocalAppInstructionsFileName);
            var result = await manager.GetAsync();
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public async Task GetAsync_ReturnsContent_WhenFileExists()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "", LocalAppInstructionsFileName = "instructions.json" };
            var expectedPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), settings.LocalAppDataFolder, settings.LocalAppInstructionsFileName);
            var content = "{\"k\":\"v\"}";
            await fs.WriteAllBytesAsync(expectedPath, Encoding.UTF8.GetBytes(content));
            var manager = new InstructionsManager(fs, settings);
            var result = await manager.GetAsync();
            Assert.That(result, Is.EqualTo(content));
        }

        [Test]
        public async Task GetAsync_ReturnsEmptyObject_OnReadError()
        {
            var fs = new ThrowingReadFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "", LocalAppInstructionsFileName = "instructions.json" };
            var expectedPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), settings.LocalAppDataFolder, settings.LocalAppInstructionsFileName);
            await fs.WriteAllBytesAsync(expectedPath, Encoding.UTF8.GetBytes("{}"));
            var manager = new InstructionsManager(fs, settings);
            var result = await manager.GetAsync();
            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public async Task UpdateAsync_WritesContent_WhenValidJson()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "", LocalAppInstructionsFileName = "instructions.json" };
            var expectedPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), settings.LocalAppDataFolder, settings.LocalAppInstructionsFileName);
            var manager = new InstructionsManager(fs, settings);
            var json = "{\"a\":1}";
            await manager.UpdateAsync(json);
            var stored = fs.ReadAllText(expectedPath);
            Assert.That(stored, Is.EqualTo(json));
        }

        [Test]
        public async Task UpdateAsync_WritesEmptyObject_WhenInputNullOrWhitespace()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "", LocalAppInstructionsFileName = "instructions.json" };
            var expectedPath = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), settings.LocalAppDataFolder, settings.LocalAppInstructionsFileName);
            var manager = new InstructionsManager(fs, settings);
            await manager.UpdateAsync(null);
            var stored = fs.ReadAllText(expectedPath);
            Assert.That(stored, Is.EqualTo("{}"));
            await manager.UpdateAsync("   ");
            stored = fs.ReadAllText(expectedPath);
            Assert.That(stored, Is.EqualTo("{}"));
        }

        [Test]
        public void UpdateAsync_ThrowsInvalidOperationException_OnInvalidJson()
        {
            var fs = new InMemoryFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "", LocalAppInstructionsFileName = "instructions.json" };
            var manager = new InstructionsManager(fs, settings);
            Assert.That(async () => await manager.UpdateAsync("not json"), Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void Constructor_ValidatesPath_And_EnsuresDirectory()
        {
            var spy = new SpyFileSystem();
            var settings = new TestSettingsManager { LocalAppDataFolder = "", LocalAppInstructionsFileName = "instructions.json" };
            var _ = new InstructionsManager(spy, settings);
            Assert.That(spy.ValidateCalled, Is.True);
            Assert.That(spy.EnsureDirectoryCalled, Is.True);
        }

        private class TestSettingsManager : ISettingsManager
        {
            public AppSettings Current => new AppSettings();
            public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AppSettings());
            public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
            #pragma warning disable 0067 // event required by interface but not used in tests
            public event Action<AppSettings> SettingsChanged;
            #pragma warning restore 0067

            public string ApplicationName => "LMLocalChat";
            public string SettingsFileName => "settings.json";
            public string LocalAppDataFolder { get; set; } = "LMLocalChat";
            public string LocalAppSettingFileName => "settings.json";
            public string LocalAppInstructionsFileName { get; set; } = "instructions.json";
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

        private class ThrowingReadFileSystem : IFileSystem
        {
            private readonly InMemoryFileSystem _inner = new InMemoryFileSystem();
            public void CreateDirectory(string path) { }
            public bool FileExists(string path) => _inner.FileExists(path);
            public string ReadAllText(string path) => throw new Exception("read error");
            public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) => throw new Exception("read error");
            public Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default) => _inner.WriteAllBytesAsync(path, data, cancellationToken);
            public Task AppendAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default) => _inner.AppendAllBytesAsync(path, data, cancellationToken);
            public void Replace(string sourceFileName, string destinationFileName) => _inner.Replace(sourceFileName, destinationFileName);
            public void Move(string sourceFileName, string destinationFileName) => _inner.Move(sourceFileName, destinationFileName);
            public void Delete(string path) => _inner.Delete(path);
            public System.Collections.Generic.IEnumerable<string> GetAllFiles() => _inner.GetAllFiles();
            public void ValidateFilePath(string filePath) { }
            public void EnsureDirectoryExistsForFile(string filePath) { }
        }

        private class SpyFileSystem : IFileSystem
        {
            public bool ValidateCalled { get; private set; }
            public bool EnsureDirectoryCalled { get; private set; }
            public void CreateDirectory(string path) { }
            public bool FileExists(string path) => false;
            public string ReadAllText(string path) => throw new System.IO.FileNotFoundException();
            public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(ReadAllText(path));
            public Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default) { return Task.CompletedTask; }
            public Task AppendAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default) { return Task.CompletedTask; }
            public void Replace(string sourceFileName, string destinationFileName) { }
            public void Move(string sourceFileName, string destinationFileName) { }
            public void Delete(string path) { }
            public System.Collections.Generic.IEnumerable<string> GetAllFiles() { yield break; }
            public void ValidateFilePath(string filePath) { ValidateCalled = true; }
            public void EnsureDirectoryExistsForFile(string filePath) { EnsureDirectoryCalled = true; }
        }
    }
}
