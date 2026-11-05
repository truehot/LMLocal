using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Models;
using LMLocal.Core.Models.Instructions;
using LMLocal.Infrastructure.Instructions;
using LMLocal.Infrastructure.Persistence;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class InstructionsManagerTests
    {
        private readonly TestDefaultsProvider _defaults = new TestDefaultsProvider();

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        private static InMemoryFileSystem CreateFs() => new InMemoryFileSystem();

        private InstructionsManager CreateManager(IFileSystem fs)
            => new InstructionsManager(fs, new TestSettingsManager(), _defaults);

        private static string FilePath() => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LMLocalChat",
            "instructions.json");

        private static JArray MakeDefaultTabs()
        {
            var arr = new JArray();
            foreach (InstructionDefaultTab d in TestDefaultsProvider.Tabs)
            {
                arr.Add(new JObject
                {
                    ["id"] = d.Id,
                    ["displayName"] = d.DisplayName,
                    ["enabled"] = d.Enabled,
                    ["temperature"] = d.Temperature,
                    ["prompt"] = "prompt-" + d.Id
                });
            }
            return arr;
        }

        private static JObject FullDocument(int selectedTabId = 1)
            => new JObject { ["tabs"] = MakeDefaultTabs(), ["selectedTabId"] = selectedTabId };

        private static async Task<InMemoryFileSystem> CreateFsWithContentAsync(string content)
        {
            var fs = new InMemoryFileSystem();
            await fs.WriteAllBytesAsync(FilePath(), Encoding.UTF8.GetBytes(content));
            return fs;
        }

        private async Task<InstructionUpdateResult> UpdateAsync(JObject doc)
        {
            var manager = CreateManager(CreateFs());
            return await manager.UpdateAsync(doc.ToString());
        }

        private async Task<(InstructionUpdateResult result, InMemoryFileSystem fs)> UpdateWithFsAsync(JObject doc)
        {
            var fs = CreateFs();
            var manager = CreateManager(fs);
            var result = await manager.UpdateAsync(doc.ToString());
            return (result, fs);
        }

        // ---------------------------------------------------------------------
        // GetAsync (read + reconciliation)
        // ---------------------------------------------------------------------

        [Test]
        public async Task GetAsync_ReturnsEmptyObject_AndDoesNotCreateFile_WhenFileMissing()
        {
            var fs = CreateFs();
            var manager = CreateManager(fs);

            var result = await manager.GetAsync();

            Assert.That(result, Is.EqualTo("{}"));
            Assert.That(fs.FileExists(FilePath()), Is.False);
            Assert.That(fs.GetAllFiles().Any(), Is.False);
        }

        [Test]
        public async Task GetAsync_AddsMissingDefaults_AndPreservesUserEdits()
        {
            var fileDoc = new JObject
            {
                ["selectedTabId"] = 4,
                ["tabs"] = new JArray
                {
                    new JObject { ["id"] = 1, ["displayName"] = "Default", ["enabled"] = false, ["temperature"] = 0.9, ["prompt"] = "edited" }
                }
            };
            var fs = await CreateFsWithContentAsync(fileDoc.ToString());
            var manager = CreateManager(fs);

            var doc = JObject.Parse(await manager.GetAsync());
            var tabs = (JArray)doc["tabs"];

            Assert.That(tabs.Count, Is.EqualTo(TestDefaultsProvider.Tabs.Count));
            Assert.That(tabs[0].Value<int>("id"), Is.EqualTo(1));

            Assert.That(tabs[0].Value<bool>("enabled"), Is.False);
            Assert.That(tabs[0].Value<double>("temperature"), Is.EqualTo(0.9).Within(0.0001));
            Assert.That(tabs[0].Value<string>("prompt"), Is.EqualTo("edited"));

            Assert.That(tabs.Any(t => t.Value<int>("id") == 9), Is.True);
            Assert.That(doc.Value<int>("selectedTabId"), Is.EqualTo(4));
        }

        [Test]
        public async Task GetAsync_RepairsFileWithoutTabs()
        {
            var fs = await CreateFsWithContentAsync("{\"selectedTabId\":5}");
            var manager = CreateManager(fs);

            var doc = JObject.Parse(await manager.GetAsync());
            var tabs = (JArray)doc["tabs"];

            Assert.That(tabs.Count, Is.EqualTo(TestDefaultsProvider.Tabs.Count));
            Assert.That(doc.Value<int>("selectedTabId"), Is.EqualTo(5));
        }

        [Test]
        public async Task GetAsync_DoesNotWriteToDisk()
        {
            const string original = "{\"tabs\":[{\"id\":1,\"displayName\":\"Default\",\"enabled\":true,\"temperature\":0.2,\"prompt\":\"p\"}]}";
            var fs = await CreateFsWithContentAsync(original);
            var manager = CreateManager(fs);

            await manager.GetAsync();
            await manager.GetAsync();

            Assert.That(fs.ReadAllText(FilePath()), Is.EqualTo(original));
        }

        [Test]
        public async Task GetAsync_NormalizesStringSelectedTabId()
        {
            var fs = await CreateFsWithContentAsync("{\"selectedTabId\":\"5\"}");
            var manager = CreateManager(fs);

            var doc = JObject.Parse(await manager.GetAsync());

            Assert.That(doc["selectedTabId"].Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(doc.Value<int>("selectedTabId"), Is.EqualTo(5));
        }

        [Test]
        public async Task GetAsync_FallsBackToMinDefault_WhenSelectedTabIdInvalid()
        {
            var fs = await CreateFsWithContentAsync("{\"selectedTabId\":9999}");
            var manager = CreateManager(fs);

            var doc = JObject.Parse(await manager.GetAsync());

            Assert.That(doc.Value<int>("selectedTabId"), Is.EqualTo(1));
        }

        [Test]
        public async Task GetAsync_ReturnsEmptyObject_OnCorruptedJson()
        {
            var fs = await CreateFsWithContentAsync("{ not valid json");
            var manager = CreateManager(fs);

            Assert.That(await manager.GetAsync(), Is.EqualTo("{}"));
        }

        [Test]
        public async Task GetAsync_DegradesToFileTabs_WhenDefaultsProviderUnavailable()
        {
            var fileDoc = new JObject
            {
                ["tabs"] = new JArray
                {
                    new JObject { ["id"] = 1, ["displayName"] = "Default", ["enabled"] = true, ["temperature"] = 0.2, ["prompt"] = "p" }
                }
            };
            var fs = await CreateFsWithContentAsync(fileDoc.ToString());

            var provider = new Mock<IInstructionDefaultsProvider>();
            provider.Setup(p => p.GetDefaultTabsAsync(It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("defaults unavailable"));

            var manager = new InstructionsManager(fs, new TestSettingsManager(), provider.Object);

            var doc = JObject.Parse(await manager.GetAsync());

            Assert.That(((JArray)doc["tabs"]).Count, Is.EqualTo(1));
            Assert.That(doc.Value<int>("selectedTabId"), Is.EqualTo(1));
        }

        // ---------------------------------------------------------------------
        // UpdateAsync (validation + normalization)
        // ---------------------------------------------------------------------

        [Test]
        public async Task UpdateAsync_Accepts_ValidFullDocument()
        {
            var (result, fs) = await UpdateWithFsAsync(FullDocument());

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(fs.FileExists(FilePath()), Is.True);
        }

        [Test]
        public async Task UpdateAsync_Rejects_RemovingDefaultTab()
        {
            var tabs = MakeDefaultTabs();
            tabs.RemoveAt(0);
            var (result, fs) = await UpdateWithFsAsync(new JObject { ["tabs"] = tabs, ["selectedTabId"] = 2 });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("Default"));
            Assert.That(fs.GetAllFiles().Any(), Is.False);
        }

        [Test]
        public async Task UpdateAsync_Rejects_RenamingDefaultTab()
        {
            var tabs = MakeDefaultTabs();
            ((JObject)tabs[0])["displayName"] = "Renamed";
            var result = await UpdateAsync(new JObject { ["tabs"] = tabs, ["selectedTabId"] = 1 });

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public async Task UpdateAsync_Rejects_DuplicateId()
        {
            var tabs = MakeDefaultTabs();
            tabs.Add(new JObject { ["id"] = 1, ["displayName"] = "Extra", ["enabled"] = true, ["temperature"] = 0.2, ["prompt"] = "x" });
            var result = await UpdateAsync(new JObject { ["tabs"] = tabs, ["selectedTabId"] = 1 });

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public async Task UpdateAsync_Rejects_DuplicateName_CaseInsensitive()
        {
            var tabs = MakeDefaultTabs();
            tabs.Add(new JObject { ["id"] = 50, ["displayName"] = "default", ["enabled"] = true, ["temperature"] = 0.2, ["prompt"] = "x" });
            var result = await UpdateAsync(new JObject { ["tabs"] = tabs, ["selectedTabId"] = 1 });

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public async Task UpdateAsync_Rejects_EmptyDisplayName()
        {
            var tabs = MakeDefaultTabs();
            tabs.Add(new JObject { ["id"] = 50, ["displayName"] = "   ", ["enabled"] = true, ["temperature"] = 0.2, ["prompt"] = "x" });
            var result = await UpdateAsync(new JObject { ["tabs"] = tabs, ["selectedTabId"] = 1 });

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public async Task UpdateAsync_Rejects_ReservedIdRange()
        {
            var tabs = MakeDefaultTabs();
            tabs.Add(new JObject { ["id"] = 10, ["displayName"] = "Custom", ["enabled"] = true, ["temperature"] = 0.2, ["prompt"] = "x" });
            var result = await UpdateAsync(new JObject { ["tabs"] = tabs, ["selectedTabId"] = 1 });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("reserved"));
        }

        [Test]
        public async Task UpdateAsync_Rejects_TooManyUserTabs()
        {
            var tabs = MakeDefaultTabs();
            for (int i = 0; i < 51; i++)
                tabs.Add(new JObject { ["id"] = 50 + i, ["displayName"] = "U" + i, ["enabled"] = true, ["temperature"] = 0.2, ["prompt"] = "x" });
            var result = await UpdateAsync(new JObject { ["tabs"] = tabs, ["selectedTabId"] = 1 });

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public async Task UpdateAsync_Rejects_TemperatureOutOfRange()
        {
            var tabs = MakeDefaultTabs();
            ((JObject)tabs[0])["temperature"] = 1.5;
            var result = await UpdateAsync(new JObject { ["tabs"] = tabs, ["selectedTabId"] = 1 });

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public async Task UpdateAsync_Rejects_EmptyTabsArray()
        {
            var result = await UpdateAsync(new JObject { ["tabs"] = new JArray(), ["selectedTabId"] = 1 });

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public async Task UpdateAsync_Accepts_UserTab_AndNormalizesSelectedTabIdAndOrder()
        {
            var tabs = MakeDefaultTabs();
            tabs.Add(new JObject { ["id"] = 50, ["displayName"] = "Mine", ["enabled"] = true, ["temperature"] = 0.3, ["prompt"] = "hi" });

            var (result, fs) = await UpdateWithFsAsync(new JObject { ["tabs"] = tabs, ["selectedTabId"] = "50" });

            Assert.That(result.Success, Is.True, result.Error);

            var stored = JObject.Parse(fs.ReadAllText(FilePath()));
            var storedTabs = (JArray)stored["tabs"];

            Assert.That(stored["selectedTabId"].Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(stored.Value<int>("selectedTabId"), Is.EqualTo(50));
            Assert.That(storedTabs.Count, Is.EqualTo(TestDefaultsProvider.Tabs.Count + 1));
            Assert.That(storedTabs.Last().Value<int>("id"), Is.EqualTo(50));
            Assert.That(storedTabs[0].Value<int>("id"), Is.EqualTo(1));
        }

        [Test]
        public async Task UpdateAsync_PreservesUnknownFields()
        {
            var doc = FullDocument();
            doc["customRoot"] = "keep";

            var (result, fs) = await UpdateWithFsAsync(doc);

            Assert.That(result.Success, Is.True, result.Error);
            var stored = JObject.Parse(fs.ReadAllText(FilePath()));
            Assert.That(stored.Value<string>("customRoot"), Is.EqualTo("keep"));
        }

        // ---------------------------------------------------------------------
        // UpdateSelectedTabAsync
        // ---------------------------------------------------------------------

        [Test]
        public async Task UpdateSelectedTabAsync_NoOp_WhenFileMissing()
        {
            var fs = CreateFs();
            var manager = CreateManager(fs);

            await manager.UpdateSelectedTabAsync("5");

            Assert.That(fs.GetAllFiles().Any(), Is.False);
        }

        [Test]
        public async Task UpdateSelectedTabAsync_NoOp_WhenTabNotFound()
        {
            var original = FullDocument().ToString();
            var fs = await CreateFsWithContentAsync(original);
            var manager = CreateManager(fs);

            await manager.UpdateSelectedTabAsync("999");

            Assert.That(fs.ReadAllText(FilePath()), Is.EqualTo(original));
        }

        [Test]
        public async Task UpdateSelectedTabAsync_Updates_WhenTabExists()
        {
            var fs = await CreateFsWithContentAsync(FullDocument(1).ToString());
            var manager = CreateManager(fs);

            await manager.UpdateSelectedTabAsync("5");

            var stored = JObject.Parse(fs.ReadAllText(FilePath()));
            Assert.That(stored.Value<int>("selectedTabId"), Is.EqualTo(5));
        }

        // ---------------------------------------------------------------------
        // GetInstructionTabIdByDisplayNameAsync
        // ---------------------------------------------------------------------

        [Test]
        public async Task GetInstructionTabIdByDisplayNameAsync_ReturnsId_WhenFoundAndEnabled()
        {
            var fs = await CreateFsWithContentAsync(FullDocument().ToString());
            var manager = CreateManager(fs);

            var result = await manager.GetInstructionTabIdByDisplayNameAsync("Tests");

            Assert.That(result, Is.EqualTo("8"));
        }

        [Test]
        public async Task GetInstructionTabIdByDisplayNameAsync_ReturnsNull_WhenDisabled()
        {
            var tabs = MakeDefaultTabs();
            ((JObject)tabs.First(t => t.Value<int>("id") == 7))["enabled"] = false;
            var fs = await CreateFsWithContentAsync(new JObject { ["tabs"] = tabs, ["selectedTabId"] = 1 }.ToString());
            var manager = CreateManager(fs);

            var result = await manager.GetInstructionTabIdByDisplayNameAsync("Explain");

            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task GetInstructionTabIdByDisplayNameAsync_IsCaseInsensitive()
        {
            var fs = await CreateFsWithContentAsync(FullDocument().ToString());
            var manager = CreateManager(fs);

            var resultLower = await manager.GetInstructionTabIdByDisplayNameAsync("tests");
            var resultMixed = await manager.GetInstructionTabIdByDisplayNameAsync("TeStS");

            Assert.That(resultLower, Is.EqualTo("8"));
            Assert.That(resultMixed, Is.EqualTo("8"));
        }

        [Test]
        public async Task GetInstructionTabIdByDisplayNameAsync_ReturnsNull_WhenNullOrEmpty()
        {
            var fs = await CreateFsWithContentAsync(FullDocument().ToString());
            var manager = CreateManager(fs);

            Assert.That(await manager.GetInstructionTabIdByDisplayNameAsync(null), Is.Null);
            Assert.That(await manager.GetInstructionTabIdByDisplayNameAsync(""), Is.Null);
        }

        // ---------------------------------------------------------------------
        // Constructor
        // ---------------------------------------------------------------------

        [Test]
        public void Constructor_ValidatesPath_AndEnsuresDirectory()
        {
            var spy = new SpyFileSystem();
            var _ = new InstructionsManager(spy, new TestSettingsManager(), _defaults);

            Assert.That(spy.ValidateCalled, Is.True);
            Assert.That(spy.EnsureDirectoryCalled, Is.True);
        }

        // ---------------------------------------------------------------------
        // Test doubles
        // ---------------------------------------------------------------------

        private sealed class TestDefaultsProvider : IInstructionDefaultsProvider
        {
            public static readonly IReadOnlyList<InstructionDefaultTab> Tabs = new List<InstructionDefaultTab>
            {
                new InstructionDefaultTab(1, "Default", true, 0.2, "p1"),
                new InstructionDefaultTab(2, "Improve", true, 0.1, "p2"),
                new InstructionDefaultTab(3, "Write", true, 0.2, "p3"),
                new InstructionDefaultTab(4, "Review", true, 0.1, "p4"),
                new InstructionDefaultTab(5, "Plan", true, 0.5, "p5"),
                new InstructionDefaultTab(6, "Bugfix", true, 0.0, "p6"),
                new InstructionDefaultTab(7, "Explain", true, 0.4, "p7"),
                new InstructionDefaultTab(8, "Tests", true, 0.1, "p8"),
                new InstructionDefaultTab(9, "Plan & Execute", true, 0.5, "p9"),
            };

            public Task<IReadOnlyList<InstructionDefaultTab>> GetDefaultTabsAsync(CancellationToken cancellationToken = default)
                => Task.FromResult(Tabs);
        }

        private class TestSettingsManager : ISettingsManager
        {
            public AppSettings Current => new AppSettings();
            public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AppSettings());
            public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task SetAiToolsModeAsync(string mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task SetSubAgentsEnabledAsync(bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;

#pragma warning disable 0067 // event required by interface but not used in tests
            public event Action<AppSettings> SettingsChanged;
#pragma warning restore 0067

            public string ApplicationName => "LMLocal";
            public string SettingsFileName => "settings.json";
            public string LocalAppDataFolder => "LMLocalChat";
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
            public string UserAgent => "LMLocal/1.0";
            public string AssistantPlaceholder => string.Empty;
        }

        private sealed class SpyFileSystem : IFileSystem
        {
            public bool ValidateCalled { get; private set; }
            public bool EnsureDirectoryCalled { get; private set; }

            public void CreateDirectory(string path) { }
            public bool FileExists(string path) => false;
            public (long Length, DateTime LastWriteTimeUtc) GetFileInfo(string path) => (0, DateTime.MinValue);
            public string ReadAllText(string path) => "{}";
            public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult("{}");
            public Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task WriteAllBytesWithEncodingAsync(string path, string content, Encoding encoding, bool hasBom, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
            public Task AppendAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public void EnsureDirectoryExistsForFile(string filePath) { EnsureDirectoryCalled = true; }
            public void ValidateFilePath(string filePath) { ValidateCalled = true; }
            public void Replace(string sourceFileName, string destinationFileName) { }
            public void Move(string sourceFileName, string destinationFileName) { }
            public void Delete(string path) { }
            public Task CopyFileAsync(string sourcePath, string destPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<string> ReadAllTextWithSharedReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult("{}");
            public Task<(string content, Encoding encoding, bool hasBom)> ReadAllTextWithDetectedEncodingAsync(string path, CancellationToken cancellationToken = default)
                => Task.FromResult(("{}", Encoding.UTF8, false));
            public Task<List<string>> ReadLinesRangeAsync(string path, int startLine, int endLine, CancellationToken cancellationToken = default) => Task.FromResult(new List<string>());
            public async Task<(System.Collections.Generic.List<string> Lines, int? TotalLines)> ReadLinesRangeWithTotalAsync(string path, int startLine, int endLine, CancellationToken cancellationToken = default)
                => (await ReadLinesRangeAsync(path, startLine, endLine, cancellationToken).ConfigureAwait(false), null);

            public Task ReadLinesAsync(string path, Action<int, string> lineHandler, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public void ReplaceOrCreate(string sourceFileName, string destinationFileName) { }
            public string[] GetFiles(string path, string searchPattern) => Array.Empty<string>();
            public string GetFileExtension(string filePath) => string.Empty;
            public bool DirectoryExists(string path) => false;
            public Task<List<FileSystemEntry>> EnumerateDirectoryAsync(string path, HashSet<string> excludedDirectoryNames, CancellationToken cancellationToken = default)
                => Task.FromResult(new List<FileSystemEntry>());
        }
    }
}
