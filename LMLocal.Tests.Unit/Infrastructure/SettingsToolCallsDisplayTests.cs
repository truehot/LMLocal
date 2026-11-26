using System.Text;
using System.Threading.Tasks;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Settings;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    /// <summary>
    /// ToolCallsDisplay is the source of truth of the chat layout, the legacy CollapseToolCalls flag only seeds it when
    /// the value is missing, and both keys stay in sync so an older build still reads a sensible value.
    /// </summary>
    [TestFixture]
    public class SettingsToolCallsDisplayTests
    {
        private const string BaseJson =
            "\"LmStudioBaseUrl\":\"http://test\",\"AutoLoadOnStartup\":true,\"Theme\":0,\"StreamInactivityTimeoutSeconds\":20";

        private static async Task<AppSettings> LoadAsync(string json)
        {
            var fs = new InMemoryFileSystem();
            const string path = "settings.json";
            await fs.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(json));

            var manager = new SettingsManager(path, fs);
            return await manager.LoadAsync();
        }

        [Test]
        public async Task LoadAsync_OldFileWithCollapseToolCallsTrue_MigratesToExpanded()
        {
            var loaded = await LoadAsync("{" + BaseJson + ",\"CollapseToolCalls\":true}");

            Assert.That(loaded.ToolCallsDisplay, Is.EqualTo("expanded"));
            Assert.That(loaded.CollapseToolCalls, Is.True);
        }

        [Test]
        public async Task LoadAsync_OldFileWithCollapseToolCallsFalse_MigratesToFlat()
        {
            var loaded = await LoadAsync("{" + BaseJson + ",\"CollapseToolCalls\":false}");

            Assert.That(loaded.ToolCallsDisplay, Is.EqualTo("flat"));
            Assert.That(loaded.CollapseToolCalls, Is.False);
        }

        [TestCase("flat", false)]
        [TestCase("expanded", true)]
        [TestCase("collapsed", true)]
        public async Task LoadAsync_ValidMode_IsPreserved(string mode, bool expectedCollapseFlag)
        {
            var loaded = await LoadAsync("{" + BaseJson + ",\"ToolCallsDisplay\":\"" + mode + "\"}");

            Assert.That(loaded.ToolCallsDisplay, Is.EqualTo(mode));
            Assert.That(loaded.CollapseToolCalls, Is.EqualTo(expectedCollapseFlag));
        }

        [TestCase(" COLLAPSED ")]
        [TestCase("Collapsed")]
        [TestCase("cOlLaPsEd")]
        public async Task LoadAsync_ModeIsTrimmedAndLowerCased(string value)
        {
            var loaded = await LoadAsync("{" + BaseJson + ",\"ToolCallsDisplay\":\"" + value + "\"}");

            Assert.That(loaded.ToolCallsDisplay, Is.EqualTo("collapsed"));
        }

        [Test]
        public async Task LoadAsync_UnknownMode_FallsBackToTheLegacyFlag()
        {
            var grouped = await LoadAsync("{" + BaseJson + ",\"ToolCallsDisplay\":\"bogus\",\"CollapseToolCalls\":true}");
            var plain = await LoadAsync("{" + BaseJson + ",\"ToolCallsDisplay\":\"bogus\",\"CollapseToolCalls\":false}");

            Assert.That(grouped.ToolCallsDisplay, Is.EqualTo("expanded"));
            Assert.That(plain.ToolCallsDisplay, Is.EqualTo("flat"));
        }

        [Test]
        public async Task LoadAsync_FlatWithLegacyFlagTrue_KeepsFlatAndClearsTheFlag()
        {
            var loaded = await LoadAsync("{" + BaseJson + ",\"ToolCallsDisplay\":\"flat\",\"CollapseToolCalls\":true}");

            Assert.That(loaded.ToolCallsDisplay, Is.EqualTo("flat"));
            Assert.That(loaded.CollapseToolCalls, Is.False);
        }

        [Test]
        public async Task LoadAsync_EmptyFile_LeavesTheModeUnsetForTheWebViewFallback()
        {
            // An empty or corrupt file is served as a fresh AppSettings without the migration, so a real null reaches
            // the webview; the JS resolver applies the same rule there instead of the migration.
            var loaded = await LoadAsync(string.Empty);

            Assert.That(loaded.ToolCallsDisplay, Is.Null);
            Assert.That(loaded.CollapseToolCalls, Is.False);
        }

        [Test]
        public async Task SaveAsync_PersistsTheModeAndTheLegacyFlag()
        {
            var fs = new InMemoryFileSystem();
            const string path = "settings.json";
            var manager = new SettingsManager(path, fs);

            // The dialog sends only the select value: CollapseToolCalls is restored from it.
            await manager.SaveAsync(new AppSettings { ToolCallsDisplay = "collapsed" });

            var content = fs.ReadAllText(path);
            Assert.That(content, Does.Contain("\"ToolCallsDisplay\": \"collapsed\""));
            Assert.That(content, Does.Contain("\"CollapseToolCalls\": true"));
        }

        [Test]
        public async Task SaveAsync_WithoutAChosenMode_WritesFlat()
        {
            var fs = new InMemoryFileSystem();
            const string path = "settings.json";
            var manager = new SettingsManager(path, fs);

            await manager.SaveAsync(new AppSettings());

            var content = fs.ReadAllText(path);
            Assert.That(content, Does.Contain("\"ToolCallsDisplay\": \"flat\""));
            Assert.That(content, Does.Contain("\"CollapseToolCalls\": false"));
        }

        [Test]
        public async Task SaveAsync_ThenLoad_KeepsTheMode()
        {
            var fs = new InMemoryFileSystem();
            const string path = "settings.json";
            var manager = new SettingsManager(path, fs);

            await manager.SaveAsync(new AppSettings { ToolCallsDisplay = "expanded" });
            var loaded = await manager.LoadAsync();

            Assert.That(loaded.ToolCallsDisplay, Is.EqualTo("expanded"));
            Assert.That(loaded.CollapseToolCalls, Is.True);
        }
    }
}
