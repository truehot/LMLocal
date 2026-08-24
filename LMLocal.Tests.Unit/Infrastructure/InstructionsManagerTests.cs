using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Instructions;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Settings;
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
            _ = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), settings.LocalAppDataFolder, settings.LocalAppInstructionsFileName);
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
            public Task SetAiToolsModeAsync(string mode, CancellationToken cancellationToken = default) => Task.CompletedTask;

            [Test]
            public async Task GetInstructionTabIdByDisplayNameAsync_ReturnsId_WhenFoundAndEnabled()
            {
                var fs = new InMemoryFileSystem();
                var settings = new TestSettingsManager { LocalAppDataFolder = "", LocalAppInstructionsFileName = "instructions.json" };
                var content = @"{
                ""tabs"": [
                    { ""id"": ""5"", ""displayName"": ""Review"", ""enabled"": true, ""temperature"": 0.1, ""prompt"": ""review prompt"" },
                    { ""id"": ""3"", ""displayName"": ""Tests"", ""enabled"": true, ""temperature"": 0.1, ""prompt"": ""test prompt"" },
                    { ""id"": ""7"", ""displayName"": ""Explain"", ""enabled"": false, ""temperature"": 0.4, ""prompt"": ""explain prompt"" }
                ]
            }";
                var expectedPath = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    settings.LocalAppDataFolder, settings.LocalAppInstructionsFileName);
                await fs.WriteAllBytesAsync(expectedPath, System.Text.Encoding.UTF8.GetBytes(content));
                var manager = new InstructionsManager(fs, settings);

                var result = await manager.GetInstructionTabIdByDisplayNameAsync("Tests");

                Assert.That(result, Is.EqualTo("3"));
            }

            [Test]
            public async Task GetInstructionTabIdByDisplayNameAsync_ReturnsNull_WhenDisabled()
            {
                var fs = new InMemoryFileSystem();
                var settings = new TestSettingsManager { LocalAppDataFolder = "", LocalAppInstructionsFileName = "instructions.json" };
                var content = @"{
                ""tabs"": [
                    { ""id"": ""7"", ""displayName"": ""Explain"", ""enabled"": false, ""temperature"": 0.4, ""prompt"": ""explain prompt"" }
                ]
            }";
                var expectedPath = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    settings.LocalAppDataFolder, settings.LocalAppInstructionsFileName);
                await fs.WriteAllBytesAsync(expectedPath, System.Text.Encoding.UTF8.GetBytes(content));
                var manager = new InstructionsManager(fs, settings);

                var result = await manager.GetInstructionTabIdByDisplayNameAsync("Explain");

                Assert.That(result, Is.Null);
            }

            [Test]
            public async Task GetInstructionTabIdByDisplayNameAsync_ReturnsNull_WhenNotFound()
            {
                var fs = new InMemoryFileSystem();
                var settings = new TestSettingsManager { LocalAppDataFolder = "", LocalAppInstructionsFileName = "instructions.json" };
                var content = @"{
                ""tabs"": [
                    { ""id"": ""1"", ""displayName"": ""Default"", ""enabled"": true, ""temperature"": 0.2, ""prompt"": ""default prompt"" }
                ]
            }";
                var expectedPath = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    settings.LocalAppDataFolder, settings.LocalAppInstructionsFileName);
                await fs.WriteAllBytesAsync(expectedPath, System.Text.Encoding.UTF8.GetBytes(content));
                var manager = new InstructionsManager(fs, settings);

                var result = await manager.GetInstructionTabIdByDisplayNameAsync("NonExistent");

                Assert.That(result, Is.Null);
            }

            [Test]
            public async Task GetInstructionTabIdByDisplayNameAsync_IsCaseInsensitive()
            {
                var fs = new InMemoryFileSystem();
                var settings = new TestSettingsManager { LocalAppDataFolder = "", LocalAppInstructionsFileName = "instructions.json" };
                var content = @"{
                ""tabs"": [
                    { ""id"": ""2"", ""displayName"": ""Bugfix"", ""enabled"": true, ""temperature"": 0.1, ""prompt"": ""bugfix prompt"" }
                ]
            }";
                var expectedPath = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    settings.LocalAppDataFolder, settings.LocalAppInstructionsFileName);
                await fs.WriteAllBytesAsync(expectedPath, System.Text.Encoding.UTF8.GetBytes(content));
                var manager = new InstructionsManager(fs, settings);

                var resultLower = await manager.GetInstructionTabIdByDisplayNameAsync("bugfix");
                var resultUpper = await manager.GetInstructionTabIdByDisplayNameAsync("BUGFIX");
                var resultMixed = await manager.GetInstructionTabIdByDisplayNameAsync("BugFix");

                Assert.That(resultLower, Is.EqualTo("2"));
                Assert.That(resultUpper, Is.EqualTo("2"));
                Assert.That(resultMixed, Is.EqualTo("2"));
            }

            [Test]
            public async Task GetInstructionTabIdByDisplayNameAsync_ReturnsNull_WhenNullOrEmpty()
            {
                var fs = new InMemoryFileSystem();
                var settings = new TestSettingsManager { LocalAppDataFolder = "", LocalAppInstructionsFileName = "instructions.json" };
                var manager = new InstructionsManager(fs, settings);

                var resultNull = await manager.GetInstructionTabIdByDisplayNameAsync(null);
                var resultEmpty = await manager.GetInstructionTabIdByDisplayNameAsync("");

                Assert.That(resultNull, Is.Null);
                Assert.That(resultEmpty, Is.Null);
            }

            [Test]
            public async Task GetInstructionTabIdByDisplayNameAsync_ReturnsFirstMatch_WhenMultipleTabsHaveSameName()
            {
                var fs = new InMemoryFileSystem();
                var settings = new TestSettingsManager { LocalAppDataFolder = "", LocalAppInstructionsFileName = "instructions.json" };
                var content = @"{
                ""tabs"": [
                    { ""id"": ""10"", ""displayName"": ""Review"", ""enabled"": true, ""temperature"": 0.1, ""prompt"": ""first"" },
                    { ""id"": ""20"", ""displayName"": ""Review"", ""enabled"": true, ""temperature"": 0.2, ""prompt"": ""second"" }
                ]
            }";
                var expectedPath = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                    settings.LocalAppDataFolder, settings.LocalAppInstructionsFileName);
                await fs.WriteAllBytesAsync(expectedPath, System.Text.Encoding.UTF8.GetBytes(content));
                var manager = new InstructionsManager(fs, settings);

                var result = await manager.GetInstructionTabIdByDisplayNameAsync("Review");

                Assert.That(result, Is.EqualTo("10"));
            }

            public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
#pragma warning disable 0067 // event required by interface but not used in tests
            public event Action<AppSettings> SettingsChanged;
#pragma warning restore 0067

            public string ApplicationName => "LMLocal";
            public string SettingsFileName => "settings.json";
            public string LocalAppDataFolder { get; set; } = "LMLocalChat";
            public string LocalAppSettingFileName => "settings.json";
            public string LocalAppInstructionsFileName { get; set; } = "instructions.json";
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
            public string UserAgent => "LMLocal/1.0";

            public string AssistantPlaceholder => throw new NotImplementedException();
        }

        private class ThrowingReadFileSystem : IFileSystem
        {
            private readonly InMemoryFileSystem _inner = new InMemoryFileSystem();
            public void CreateDirectory(string path) { }
            public bool FileExists(string path) => _inner.FileExists(path);
            public (long Length, DateTime LastWriteTimeUtc) GetFileInfo(string path) => _inner.GetFileInfo(path);
            public string ReadAllText(string path) => throw new Exception("read error");
            public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) => throw new Exception("read error");
            public Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default) => _inner.WriteAllBytesAsync(path, data, cancellationToken);
            public Task WriteAllBytesWithEncodingAsync(string path, string content, System.Text.Encoding encoding, bool hasBom, CancellationToken cancellationToken = default)
                => _inner.WriteAllBytesWithEncodingAsync(path, content, encoding, hasBom, cancellationToken);

            public Task AppendAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default) => _inner.AppendAllBytesAsync(path, data, cancellationToken);
            public void Replace(string sourceFileName, string destinationFileName) => _inner.Replace(sourceFileName, destinationFileName);
            public void Move(string sourceFileName, string destinationFileName) => _inner.Move(sourceFileName, destinationFileName);
            public void Delete(string path) => _inner.Delete(path);
            public System.Collections.Generic.IEnumerable<string> GetAllFiles() => _inner.GetAllFiles();
            public void ValidateFilePath(string filePath) { }
            public void EnsureDirectoryExistsForFile(string filePath) { }
            public Task CopyFileAsync(string sourcePath, string destPath, CancellationToken cancellationToken) => _inner.CopyFileAsync(sourcePath, destPath, cancellationToken);
            public Task<string> ReadAllTextWithSharedReadAsync(string path, CancellationToken cancellationToken = default) => _inner.ReadAllTextWithSharedReadAsync(path, cancellationToken);
            public Task<(string content, System.Text.Encoding encoding, bool hasBom)> ReadAllTextWithDetectedEncodingAsync(string path, CancellationToken cancellationToken = default)
                => ReadAllTextWithSharedReadAsync(path, cancellationToken).ContinueWith(t => (t.Result, System.Text.Encoding.UTF8, false), cancellationToken);
            public (System.Text.Encoding encoding, bool hasBom) DetectEncoding(string path) => (new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), false);

            public Task<System.Collections.Generic.List<string>> ReadLinesRangeAsync(string path, int startLine, int endLine, CancellationToken cancellationToken = default) => _inner.ReadLinesRangeAsync(path, startLine, endLine, cancellationToken);
            public Task ReadLinesAsync(string path, Action<int, string> lineHandler, CancellationToken cancellationToken = default) => _inner.ReadLinesAsync(path, lineHandler, cancellationToken);

            public void ReplaceOrCreate(string sourceFileName, string destinationFileName)
            {
            }

            public string[] GetFiles(string path, string searchPattern)
            {
                return _inner.GetFiles(path, searchPattern);
            }

            public string GetFileExtension(string filePath)
            {
                throw new NotImplementedException();
            }

            public bool DirectoryExists(string path)
            {
                throw new NotImplementedException();
            }

            public Task<List<FileSystemEntry>> EnumerateDirectoryAsync(string path, HashSet<string> excludedDirectoryNames, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }
        }

        private class SpyFileSystem : IFileSystem
        {
            public bool ValidateCalled { get; private set; }
            public bool EnsureDirectoryCalled { get; private set; }
            public void CreateDirectory(string path) { }
            public bool FileExists(string path) => false;
            public (long Length, DateTime LastWriteTimeUtc) GetFileInfo(string path) => throw new System.IO.FileNotFoundException();
            public string ReadAllText(string path) => throw new System.IO.FileNotFoundException();
            public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(ReadAllText(path));
            public Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default) { return Task.CompletedTask; }
            public Task WriteAllBytesWithEncodingAsync(string path, string content, System.Text.Encoding encoding, bool hasBom, CancellationToken cancellationToken = default) { return WriteAllBytesAsync(path, encoding.GetBytes(content), cancellationToken); }

            public Task AppendAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default) { return Task.CompletedTask; }
            public void Replace(string sourceFileName, string destinationFileName) { }
            public void Move(string sourceFileName, string destinationFileName) { }
            public void Delete(string path) { }
            public System.Collections.Generic.IEnumerable<string> GetAllFiles() { yield break; }
            public void ValidateFilePath(string filePath) { ValidateCalled = true; }
            public void EnsureDirectoryExistsForFile(string filePath) { EnsureDirectoryCalled = true; }
            public Task CopyFileAsync(string sourcePath, string destPath, CancellationToken cancellationToken) { return Task.CompletedTask; }
            public Task<string> ReadAllTextWithSharedReadAsync(string path, CancellationToken cancellationToken = default) { return Task.FromResult(ReadAllText(path)); }
            public Task<System.Collections.Generic.List<string>> ReadLinesRangeAsync(string path, int startLine, int endLine, CancellationToken cancellationToken = default) { return Task.FromResult(new System.Collections.Generic.List<string>()); }
            public Task ReadLinesAsync(string path, Action<int, string> lineHandler, CancellationToken cancellationToken = default) { return Task.CompletedTask; }
            public Task<(string content, System.Text.Encoding encoding, bool hasBom)> ReadAllTextWithDetectedEncodingAsync(string path, CancellationToken cancellationToken = default)
                => ReadAllTextWithSharedReadAsync(path, cancellationToken).ContinueWith(t => (t.Result, System.Text.Encoding.UTF8, false), cancellationToken);
            public (System.Text.Encoding encoding, bool hasBom) DetectEncoding(string path) => (new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), false);


            public void ReplaceOrCreate(string sourceFileName, string destinationFileName)
            {
            }

            public string[] GetFiles(string path, string searchPattern)
            {
                return Array.Empty<string>();
            }

            public string GetFileExtension(string filePath)
            {
                throw new NotImplementedException();
            }

            public bool DirectoryExists(string path)
            {
                throw new NotImplementedException();
            }

            public Task<List<FileSystemEntry>> EnumerateDirectoryAsync(string path, HashSet<string> excludedDirectoryNames, CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }
        }
    }
}
