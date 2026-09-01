using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Streaming;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class ActivityReportingStreamTests
    {
        [Test]
        public void Read_ReportsActivity_WhenBytesRead()
        {
            int activityCount = 0;
            var inner = new MemoryStream(Encoding.UTF8.GetBytes("hello"));

            using (var stream = new ActivityReportingStream(inner, () => activityCount++))
            {
                var buffer = new byte[1024];
                int read = stream.Read(buffer, 0, buffer.Length);

                Assert.That(read, Is.GreaterThan(0));
            }

            Assert.That(activityCount, Is.EqualTo(1));
        }

        [Test]
        public void Read_DoesNotReportActivity_WhenNoBytesRead()
        {
            int activityCount = 0;
            var inner = new MemoryStream(new byte[0]);

            using (var stream = new ActivityReportingStream(inner, () => activityCount++))
            {
                var buffer = new byte[1024];
                int read = stream.Read(buffer, 0, buffer.Length);

                Assert.That(read, Is.EqualTo(0));
            }

            Assert.That(activityCount, Is.EqualTo(0));
        }

        [Test]
        public async Task ReadAsync_ReportsActivity_WhenBytesRead()
        {
            int activityCount = 0;
            var inner = new MemoryStream(Encoding.UTF8.GetBytes("hi"));

            using (var stream = new ActivityReportingStream(inner, () => activityCount++))
            {
                var buffer = new byte[1024];
                int read = await stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None);

                Assert.That(read, Is.GreaterThan(0));
            }

            Assert.That(activityCount, Is.EqualTo(1));
        }

        [Test]
        public void StreamReader_ReadLine_ReportsActivity()
        {
            int activityCount = 0;
            var inner = new MemoryStream(Encoding.UTF8.GetBytes("line1\nline2\n"));

            using (var stream = new ActivityReportingStream(inner, () => activityCount++))
            using (var reader = new StreamReader(stream))
            {
                Assert.That(reader.ReadLine(), Is.EqualTo("line1"));
                Assert.That(reader.ReadLine(), Is.EqualTo("line2"));
            }

            // StreamReader may buffer both lines in a single Read, so at least one activity
            // report is guaranteed (the SSE inactivity watchdog only needs "any data arrived").
            Assert.That(activityCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void CanTimeout_And_ReadTimeout_PassThroughToInner()
        {
            var inner = new MemoryStream();
            using (var stream = new ActivityReportingStream(inner, () => { }))
            {
                Assert.That(stream.CanTimeout, Is.EqualTo(inner.CanTimeout));

                if (inner.CanTimeout)
                {
                    stream.ReadTimeout = 12345;
                    Assert.That(stream.ReadTimeout, Is.EqualTo(12345));
                }
            }
        }

        [Test]
        public void Dispose_DisposesInner()
        {
            var inner = new MemoryStream(Encoding.UTF8.GetBytes("data"));
            var stream = new ActivityReportingStream(inner, () => { });

            stream.Dispose();

            Assert.Throws<ObjectDisposedException>(() => inner.Read(new byte[1], 0, 1));
        }

        [Test]
        public async Task Watchdog_Fires_WhenWrappedStreamProducesNoData()
        {
            var cts = new CancellationTokenSource();
            var watcher = new StreamInactivityWatcher(cts, 1, 10); // 1s timeout, 10ms poll

            using (var reporting = new ActivityReportingStream(new MemoryStream(), () => { }))
            {
                // No reads happen on the wrapper -> no activity -> the watchdog must fire and cancel.
                await watcher.WatchAsync(CancellationToken.None).ConfigureAwait(false);

                Assert.That(watcher.IsTimeout, Is.True);
                Assert.That(cts.IsCancellationRequested, Is.True);
            }
        }

        [Test]
        public async Task Watchdog_DoesNotFire_WhileActivityContinues()
        {
            var cts = new CancellationTokenSource();
            var watcher = new StreamInactivityWatcher(cts, 1, 10); // 1s timeout, 10ms poll

            using (var reporting = new ActivityReportingStream(new InfiniteDataStream(), watcher.SignalActivity))
            {
                var watchTask = watcher.WatchAsync(CancellationToken.None);

                // A dedicated thread feeds activity continuously. A real Thread (not a thread-pool
                // task) is used so the feeder cannot be starved while the watcher keeps polling.
                using (var done = new ManualResetEventSlim(false))
                {
                    var feeder = new Thread(() =>
                    {
                        var buffer = new byte[16];
                        while (!done.IsSet)
                        {
                            reporting.Read(buffer, 0, buffer.Length);
                        }
                    });
                    feeder.IsBackground = true;
                    feeder.Start();

                    await Task.Delay(1300).ConfigureAwait(false); // well beyond the 1s timeout

                    done.Set();
                    feeder.Join(1000);
                }

                Assert.That(watcher.IsTimeout, Is.False);
                Assert.That(cts.IsCancellationRequested, Is.False);

                watcher.SignalCompletion();
                await watchTask.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Stream that always yields a byte so that every Read counts as activity.
        /// </summary>
        private sealed class InfiniteDataStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (count == 0) return 0;
                buffer[offset] = 1;
                return 1;
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
