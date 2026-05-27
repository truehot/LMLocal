using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Settings;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class SettingsManagerAdditionalTests
    {

        [Test]
        public void SaveAsync_NullSettings_Throws()
        {
            var fs = new CancelableFileSystem();
            var mgr = new SettingsManager("settings.json", fs);
            Assert.ThrowsAsync<ArgumentNullException>(async () => await mgr.SaveAsync(null));
        }

        [Test]
        public async Task SaveAsync_TempRemovedOnSuccess()
        {
            var fs = new CancelableFileSystem();
            var path = "settings.json";
            var mgr = new SettingsManager(path, fs);
            var s = new AppSettings { LmStudioBaseUrl = "http://x" };
            await mgr.SaveAsync(s);
            Assert.That(fs.FileExists(path + ".tmp"), Is.False);
        }

        [Test]
        public void SaveAsync_BackupRestoreOnFailure_RestoresOriginal()
        {
            var originalJson = "orig";
            var path = "settings.json";
            var failing = new FailingMoveFileSystem(Normalize(path + ".tmp"));
            failing.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(originalJson)).Wait();

            var mgr = new SettingsManager(path, failing);
            var newSettings = new AppSettings { LmStudioBaseUrl = "http://new" };

            Assert.ThrowsAsync<InvalidOperationException>(async () => await mgr.SaveAsync(newSettings));

            Assert.That(failing.ReadAllText(path), Is.EqualTo(originalJson));
        }

        [Test]
        public void SaveAsync_Cancellation_CleansTempAndThrows()
        {
            var fs = new CancelableFileSystem();
            var mgr = new SettingsManager("settings.json", fs);
            var cts = new CancellationTokenSource();
            var task = mgr.SaveAsync(new AppSettings { LmStudioBaseUrl = "http://x" }, cts.Token);
            cts.Cancel();
            Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
            Assert.That(fs.FileExists("settings.json.tmp"), Is.False);
        }

        [TestCase(2)]
        [TestCase(10)]
        public async Task SaveAsync_Concurrent_LastWins(int concurrentSaves)
        {
            var fs = new CancelableFileSystem();
            var mgr = new SettingsManager("settings.json", fs);

            var tasks = new Task[concurrentSaves];
            for (int i = 0; i < concurrentSaves; i++)
            {
                var s = new AppSettings { LmStudioBaseUrl = "http://" + i };
                tasks[i] = mgr.SaveAsync(s);
            }

            await Task.WhenAll(tasks);
            var current = mgr.Current;
            var content = fs.ReadAllText("settings.json");
            Assert.That(content, Does.Contain(current.LmStudioBaseUrl));
        }

        private static string Normalize(string p) => p?.Replace('\\','/').ToLowerInvariant();
    }
}
