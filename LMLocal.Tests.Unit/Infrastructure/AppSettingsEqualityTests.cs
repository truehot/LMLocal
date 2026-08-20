using System.Text;
using System.Threading.Tasks;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Settings;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class AppSettingsEqualityTests
    {
        [Test]
        public void AppSettings_Equals_IsCaseInsensitiveForLmStudioBaseUrl()
        {
            var a = new AppSettings { LmStudioBaseUrl = "HTTP://LOCALHOST:1234" };
            var b = new AppSettings { LmStudioBaseUrl = "http://localhost:1234" };
            Assert.That(a.Equals(b), Is.True);
            Assert.That(b.Equals(a), Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public async Task SettingsManager_SaveAsync_DoesNotRaiseEvent_WhenOnlyUrlCaseDiffers()
        {
            var fs = new InMemoryFileSystem();
            var path = "settings.json";
            var initialJson = "{\"LmStudioBaseUrl\":\"http://example.com\",\"AutoLoadOnStartup\":true,\"EnableHistoryCompression\":true,\"EnableHistoryCompaction\":true,\"Theme\":0,\"StreamInactivityTimeoutSeconds\":20,\"EnableChatLogging\":false}";
            await fs.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(initialJson));

            var manager = new SettingsManager(path, fs);
            AppSettings observed = null;
            manager.SettingsChanged += s => observed = s;

            var loaded = await manager.LoadAsync();
            Assert.That(loaded.LmStudioBaseUrl, Is.EqualTo("http://example.com"));
            Assert.That(observed, Is.Not.Null);

            observed = null;
            var toSave = new AppSettings { LmStudioBaseUrl = "HTTP://EXAMPLE.COM", AutoLoadOnStartup = loaded.AutoLoadOnStartup, EnableHistoryCompression = loaded.EnableHistoryCompression, EnableHistoryCompaction = loaded.EnableHistoryCompaction, Theme = loaded.Theme, StreamInactivityTimeoutSeconds = loaded.StreamInactivityTimeoutSeconds, EnableChatLogging = loaded.EnableChatLogging };
            await manager.SaveAsync(toSave);
            Assert.That(manager.Current.LmStudioBaseUrl, Is.EqualTo("HTTP://EXAMPLE.COM"));
            Assert.That(observed, Is.Null);
        }

        [Test]
        public void AppSettings_Equals_IsCaseInsensitiveForTrustedServerCertificatePath()
        {
            var a = new AppSettings { TrustedServerCertificatePath = "C:\\CERTS\\MY-SERVER.CER" };
            var b = new AppSettings { TrustedServerCertificatePath = "c:\\certs\\my-server.cer" };
            Assert.That(a.Equals(b), Is.True);
            Assert.That(b.Equals(a), Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void AppSettings_Equals_IsFalseForDifferentTrustedServerCertificatePaths()
        {
            var a = new AppSettings { TrustedServerCertificatePath = "C:\\certs\\a.cer" };
            var b = new AppSettings { TrustedServerCertificatePath = "C:\\certs\\b.cer" };
            Assert.That(a.Equals(b), Is.False);
            Assert.That(a.GetHashCode(), Is.Not.EqualTo(b.GetHashCode()));
        }
    }
}
