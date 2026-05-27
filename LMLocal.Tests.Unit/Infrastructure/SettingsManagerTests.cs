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


    }
}
