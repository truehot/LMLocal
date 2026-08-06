using System;
using System.Text;
using System.Threading.Tasks;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Settings;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class SettingsManagerTests
    {


        [Test]
        public async Task SaveAsync_CreatesFile_UpdatesCurrent_And_RaisesEvent()
        {
            var fs = new InMemoryFileSystem();
            var path = "settings.json";
            var manager = new SettingsManager(path, fs);

            AppSettings observed = null;
            manager.SettingsChanged += s => observed = s;

            var settings = new AppSettings { LmStudioBaseUrl = "http://example.test", AutoLoadOnStartup = false };
            await manager.SaveAsync(settings);

            Assert.That(manager.Current, Is.EqualTo(settings));
            Assert.That(observed, Is.Not.Null);
            Assert.That(observed.LmStudioBaseUrl, Is.EqualTo("http://example.test"));

            var content = fs.ReadAllText(path);
            Assert.That(content, Does.Contain("http://example.test"));
        }

        [Test]
        public async Task LoadAsync_ReadsFile_And_OnlyRaisesEventOnChange()
        {
            var fs = new InMemoryFileSystem();
            var path = "settings.json";

            var initialJson = "{\"LmStudioBaseUrl\":\"http://a\",\"AutoLoadOnStartup\":true,\"EnableHistoryCompression\":true,\"EnableHistoryCompaction\":true,\"Theme\":0,\"StreamInactivityTimeoutSeconds\":20,\"EnableChatLogging\":false}";
            fs.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(initialJson)).Wait();

            var manager = new SettingsManager(path, fs);
            AppSettings observed = null;
            manager.SettingsChanged += s => observed = s;

            var loaded = await manager.LoadAsync();
            Assert.That(loaded.LmStudioBaseUrl, Is.EqualTo("http://a"));
            Assert.That(observed, Is.Not.Null);

            observed = null;
            var loaded2 = await manager.LoadAsync();
            Assert.That(loaded2.LmStudioBaseUrl, Is.EqualTo("http://a"));
            Assert.That(observed, Is.Null);
        }

        // =========================================================================
        // SetAiToolsModeAsync
        // =========================================================================

        [Test]
        public async Task SetAiToolsModeAsync_ReadWrite_SetsBothFlagsAndPersists()
        {
            var fs = new InMemoryFileSystem();
            var path = "settings.json";
            var manager = new SettingsManager(path, fs);
            await manager.SaveAsync(new AppSettings { LmStudioBaseUrl = "http://x", EnableAiTools = false, EnableAiWriteTools = false });

            await manager.SetAiToolsModeAsync("readwrite");

            Assert.Multiple(() =>
            {
                Assert.That(manager.Current.EnableAiTools, Is.True);
                Assert.That(manager.Current.EnableAiWriteTools, Is.True);
            });

            var content = fs.ReadAllText(path);
            Assert.That(content, Does.Contain("\"EnableAiTools\": true"));
            Assert.That(content, Does.Contain("\"EnableAiWriteTools\": true"));
        }

        [Test]
        public async Task SetAiToolsModeAsync_ReadOnly_SetsReadAccess()
        {
            var manager = new SettingsManager("settings.json", new InMemoryFileSystem());
            await manager.SaveAsync(new AppSettings { LmStudioBaseUrl = "http://x" });

            await manager.SetAiToolsModeAsync("readonly");

            Assert.Multiple(() =>
            {
                Assert.That(manager.Current.EnableAiTools, Is.True);
                Assert.That(manager.Current.EnableAiWriteTools, Is.False);
            });
        }

        [Test]
        public async Task SetAiToolsModeAsync_None_DisablesBoth()
        {
            var manager = new SettingsManager("settings.json", new InMemoryFileSystem());
            await manager.SaveAsync(new AppSettings { LmStudioBaseUrl = "http://x", EnableAiTools = true, EnableAiWriteTools = true });

            await manager.SetAiToolsModeAsync("none");

            Assert.Multiple(() =>
            {
                Assert.That(manager.Current.EnableAiTools, Is.False);
                Assert.That(manager.Current.EnableAiWriteTools, Is.False);
            });
        }

        [Test]
        public async Task SetAiToolsModeAsync_UnknownMode_TreatedAsNone()
        {
            var manager = new SettingsManager("settings.json", new InMemoryFileSystem());
            await manager.SaveAsync(new AppSettings { LmStudioBaseUrl = "http://x", EnableAiTools = true, EnableAiWriteTools = true });

            await manager.SetAiToolsModeAsync("garbage");

            Assert.Multiple(() =>
            {
                Assert.That(manager.Current.EnableAiTools, Is.False);
                Assert.That(manager.Current.EnableAiWriteTools, Is.False);
            });
        }

        [Test]
        public void SetAiToolsModeAsync_NullMode_Throws()
        {
            var manager = new SettingsManager("settings.json", new InMemoryFileSystem());
            Assert.ThrowsAsync<ArgumentException>(async () => await manager.SetAiToolsModeAsync(null));
        }

        [Test]
        public void SetAiToolsModeAsync_WhitespaceMode_Throws()
        {
            var manager = new SettingsManager("settings.json", new InMemoryFileSystem());
            Assert.ThrowsAsync<ArgumentException>(async () => await manager.SetAiToolsModeAsync("   "));
        }

        [Test]
        public async Task SetAiToolsModeAsync_DoesNotMutateCachedInstance()
        {
            var manager = new SettingsManager("settings.json", new InMemoryFileSystem());
            var original = new AppSettings { LmStudioBaseUrl = "http://x", EnableAiTools = false, EnableAiWriteTools = false };
            await manager.SaveAsync(original);

            var cachedBefore = manager.Current;
            await manager.SetAiToolsModeAsync("readwrite");

            Assert.That(ReferenceEquals(manager.Current, cachedBefore), Is.False, "A new instance should be saved.");
            // The original cached instance must remain untouched.
            Assert.Multiple(() =>
            {
                Assert.That(original.EnableAiTools, Is.False);
                Assert.That(original.EnableAiWriteTools, Is.False);
            });
        }

        [Test]
        public async Task SetAiToolsModeAsync_LoadsWhenNotLoaded()
        {
            var fs = new InMemoryFileSystem();
            var path = "settings.json";
            var json = "{\"LmStudioBaseUrl\":\"http://a\",\"EnableAiTools\":false,\"EnableAiWriteTools\":false}";
            fs.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(json)).Wait();

            var manager = new SettingsManager(path, fs);

            await manager.SetAiToolsModeAsync("readonly");

            Assert.Multiple(() =>
            {
                Assert.That(manager.Current.LmStudioBaseUrl, Is.EqualTo("http://a"));
                Assert.That(manager.Current.EnableAiTools, Is.True);
            });
        }


    }
}
