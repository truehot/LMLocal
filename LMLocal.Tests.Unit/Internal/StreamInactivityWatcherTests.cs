using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Streaming;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class StreamInactivityWatcherTests
    {
        [Test]
        public void Constructor_Throws_OnInvalidArguments()
        {
            Assert.Throws<ArgumentNullException>(() => new StreamInactivityWatcher(null, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StreamInactivityWatcher(new CancellationTokenSource(), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StreamInactivityWatcher(new CancellationTokenSource(), 1, 0));
        }

        [Test]
        public async Task WatchAsync_ExitsOnSignalCompletion_WhenNotActive()
        {
            var cts = new CancellationTokenSource();
            var watcher = new StreamInactivityWatcher(cts, 1, 10);

            // Start watching; activity time always zero. SignalCompletion should make it exit quickly.
            var watchTask = watcher.WatchAsync(CancellationToken.None);
            watcher.SignalCompletion();

            // Should complete without throwing
            await watchTask.ConfigureAwait(false);
        }

        [Test]
        public async Task WatchAsync_ExitsOnSignalCompletion_WhenActive()
        {
            var cts = new CancellationTokenSource();
            var watcher = new StreamInactivityWatcher(cts, 1, 10);

            var watchTask = watcher.WatchAsync(CancellationToken.None);
            watcher.SignalCompletion();

            await watchTask.ConfigureAwait(false);
        }

        [Test]
        public async Task WatchAsync_ReportsTimeout_WhenInactivityTimeoutExceeded()
        {
            // Short timeout so test runs quickly
            var internalCts = new CancellationTokenSource();
            var watcher = new StreamInactivityWatcher(internalCts, 1, 10);

            await watcher.WatchAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(watcher.IsTimeout, Is.True);
        }

        [Test]
        public async Task WatchAsync_Respects_CancellationToken()
        {
            var internalCts = new CancellationTokenSource();
            var watcher = new StreamInactivityWatcher(internalCts, 10, 100);
            var cts = new CancellationTokenSource();

            var watchTask = watcher.WatchAsync(cts.Token);

            // Cancel the token; the watcher should observe this and exit gracefully.
            cts.Cancel();

            // Should complete without throwing
            await watchTask.ConfigureAwait(false);

            // Cancellation should not be treated as inactivity timeout
            Assert.That(watcher.IsTimeout, Is.False);
        }
    }
}
