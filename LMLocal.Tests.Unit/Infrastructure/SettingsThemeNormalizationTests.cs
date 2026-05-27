using System.Text;
using System.Threading.Tasks;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Settings;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class SettingsThemeNormalizationTests
    {
        [TestCase(0, AppTheme.Dark)]
        [TestCase(1, AppTheme.Light)]
        [TestCase(2, AppTheme.MidLight)]
        [TestCase(3, AppTheme.MidDark)]
        public async Task LoadAsync_ValidThemeValues_PreservedCorrectly(int themeValue, AppTheme expected)
        {
            var fs = new InMemoryFileSystem();
            var path = "settings.json";
            var json = $"{{\"LmStudioBaseUrl\":\"http://test\",\"AutoLoadOnStartup\":true,\"EnableHistoryCompression\":true,\"EnableHistoryCompaction\":true,\"Theme\":{themeValue},\"StreamInactivityTimeoutSeconds\":20,\"EnableChatLogging\":false}}";
            fs.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(json)).Wait();

            var manager = new SettingsManager(path, fs);
            var loaded = await manager.LoadAsync();

            Assert.That(loaded.Theme, Is.EqualTo(expected));
        }

        [TestCase(-1)]
        [TestCase(4)]
        [TestCase(100)]
        public async Task LoadAsync_InvalidThemeValue_DefaultsToDark(int invalidTheme)
        {
            var fs = new InMemoryFileSystem();
            var path = "settings.json";
            var json = $"{{\"LmStudioBaseUrl\":\"http://test\",\"AutoLoadOnStartup\":true,\"EnableHistoryCompression\":true,\"EnableHistoryCompaction\":true,\"Theme\":{invalidTheme},\"StreamInactivityTimeoutSeconds\":20,\"EnableChatLogging\":false}}";
            fs.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(json)).Wait();

            var manager = new SettingsManager(path, fs);
            var loaded = await manager.LoadAsync();

            Assert.That(loaded.Theme, Is.EqualTo(AppTheme.Dark));
        }

        [Test]
        public async Task SaveAsync_NormalizesTheme_BeforePersisting()
        {
            var fs = new InMemoryFileSystem();
            var path = "settings.json";
            var manager = new SettingsManager(path, fs);

            var settings = new AppSettings { Theme = AppTheme.MidDark };
            await manager.SaveAsync(settings);

            var content = fs.ReadAllText(path);
            Assert.That(content, Does.Contain("\"Theme\": 3"));
        }

        [Test]
        public async Task LoadAsync_MalformedTheme_DefaultsToDark()
        {
            var fs = new InMemoryFileSystem();
            var path = "settings.json";
            var json = "{\"LmStudioBaseUrl\":\"http://test\",\"AutoLoadOnStartup\":true,\"EnableHistoryCompression\":true,\"EnableHistoryCompaction\":true,\"Theme\":\"invalid\",\"StreamInactivityTimeoutSeconds\":20,\"EnableChatLogging\":false}";
            fs.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(json)).Wait();

            var manager = new SettingsManager(path, fs);
            var loaded = await manager.LoadAsync();

            Assert.That(loaded.Theme, Is.EqualTo(AppTheme.Dark));
        }

        [Test]
        public async Task Load_ValidTheme_PreservedCorrectly()
        {
            var fs = new InMemoryFileSystem();
            var path = "settings.json";
            var json = "{\"LmStudioBaseUrl\":\"http://test\",\"AutoLoadOnStartup\":true,\"EnableHistoryCompression\":true,\"EnableHistoryCompaction\":true,\"Theme\":2,\"StreamInactivityTimeoutSeconds\":20,\"EnableChatLogging\":false}";
            fs.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(json)).Wait();

            var manager = new SettingsManager(path, fs);
            var loaded = await manager.LoadAsync();

            Assert.That(loaded.Theme, Is.EqualTo(AppTheme.MidLight));
        }

        [Test]
        public async Task Load_InvalidTheme_DefaultsToDark()
        {
            var fs = new InMemoryFileSystem();
            var path = "settings.json";
            var json = "{\"LmStudioBaseUrl\":\"http://test\",\"AutoLoadOnStartup\":true,\"EnableHistoryCompression\":true,\"EnableHistoryCompaction\":true,\"Theme\":99,\"StreamInactivityTimeoutSeconds\":20,\"EnableChatLogging\":false}";
            fs.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(json)).Wait();

            var manager = new SettingsManager(path, fs);
            var loaded = await manager.LoadAsync();

            Assert.That(loaded.Theme, Is.EqualTo(AppTheme.Dark));
        }
    }
}
