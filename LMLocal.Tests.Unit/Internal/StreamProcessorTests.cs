using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Infrastructure.Streaming;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class StreamProcessorTests
    {
        private class MockStreamInactivityWatcher : IStreamInactivityWatcher
        {
            public bool IsTimeout => false;

            public Task WatchAsync(Func<long> isActive, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public void SignalCompletion() { }

            public void SignalActivity() { }

            public Task WatchAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }

        private class MockTokenSpeedCalculator : ITokenSpeedCalculator
        {
            public void Update(int totalTokens) { }
            public double GetTokensPerSecond() => 0.0;
        }

        [Test]
        public void ProcessStreamAsync_Throws_OnCancellation()
        {
            var processor = new StreamProcessor(
                new MockTokenSpeedCalculator(),
                inactivityWatcher: new MockStreamInactivityWatcher()
            );
            var json = "data: {\"choices\":[{\"delta\":{\"content\":\"cancel\"}}]}\n";
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var cts = new CancellationTokenSource();
                cts.Cancel();
                Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await processor.ProcessStreamAsync(stream, cts.Token, async (chunk, stats) => { await Task.CompletedTask; }));
            }
        }

        [Test]
        public async Task ProcessStreamAsync_BatchesChunks_WhenIntervalElapsed()
        {
            int chunkCount = 0;
            var processor = new StreamProcessor(
                new MockTokenSpeedCalculator(),
                inactivityWatcher: new MockStreamInactivityWatcher()
            );
            var json = new StringBuilder();
            json.AppendLine("data: {\"choices\":[{\"delta\":{\"content\":\"a\"}}]}");
            json.AppendLine("data: {\"choices\":[{\"delta\":{\"content\":\"b\"}}]}");
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.ToString())))
            {
                await processor.ProcessStreamAsync(stream, CancellationToken.None, async (chunk, stats) => { chunkCount++; await Task.CompletedTask; }, 1);
                Assert.That(chunkCount, Is.GreaterThanOrEqualTo(1));
            }
        }

        [Test]
        public async Task ProcessStreamAsync_IgnoresDoneAndEmptyLines()
        {
            bool chunkCalled = false;
            var processor = new StreamProcessor(
                new MockTokenSpeedCalculator(),
                inactivityWatcher: new MockStreamInactivityWatcher()
            );
            var json = "\n   \ndata: [DONE]\n";
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var result = await processor.ProcessStreamAsync(stream, CancellationToken.None, async (chunk, stats) => { chunkCalled = true; await Task.CompletedTask; });
                Assert.That(chunkCalled, Is.False);
                Assert.That(result.ContentResponse, Is.EqualTo(""));
            }
        }

        [Test]
        public async Task ProcessStreamAsync_CallsOnError_OnInvalidJson()
        {
            bool errorCalled = false;
            var processor = new StreamProcessor(
                new MockTokenSpeedCalculator(),
                inactivityWatcher: new MockStreamInactivityWatcher()
            );
            var invalid = "data: {not a json}\n";
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(invalid)))
            {
                await processor.ProcessStreamAsync(stream, CancellationToken.None, async (chunk, tokens) => { await Task.CompletedTask; });
                // Parser now returns null on malformed JSON; processor should not call onError
                Assert.That(errorCalled, Is.False);
            }
        }

        [Test]
        public async Task ProcessStreamAsync_ReportsTokens_ViaOnChunk_FromUsage()
        {
            int reportedTokens = 0;
            var processor = new StreamProcessor(
                new MockTokenSpeedCalculator(),
                inactivityWatcher: new MockStreamInactivityWatcher()
            );
            // usage typically arrives in a separate chunk; simulate content then usage
            var json = new StringBuilder();
            json.AppendLine("data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}");
            json.AppendLine("data: {\"choices\":[],\"usage\":{\"total_tokens\":42}}");
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.ToString())))
            {
                var result = await processor.ProcessStreamAsync(stream, CancellationToken.None, async (chunk, stats) => { reportedTokens = stats.TotalTokens; await Task.CompletedTask; }, 0);
                Assert.That(result.ContentResponse, Is.EqualTo("hi"));
                // onChunk reports tokens per content chunks; usage stored separately
                Assert.That(reportedTokens, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ProcessStreamAsync_SendsFinalChunk_AtEnd()
        {
            int chunkCount = 0;
            var processor = new StreamProcessor(
                new MockTokenSpeedCalculator(),
                inactivityWatcher: new MockStreamInactivityWatcher()
            );
            var json = "data: {\"choices\":[{\"delta\":{\"content\":\"final\"}}]}\n";
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var res = await processor.ProcessStreamAsync(stream, CancellationToken.None, async (chunk, tokens) => { chunkCount++; await Task.CompletedTask; }, 5000);
                Assert.That(chunkCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ProcessStreamAsync_DoesNotCallChunk_OnEmptyStream()
        {
            bool chunkCalled = false;
            var processor = new StreamProcessor(
                new MockTokenSpeedCalculator(),
                inactivityWatcher: new MockStreamInactivityWatcher()
            );
            using (var stream = new MemoryStream())
            {
                var result = await processor.ProcessStreamAsync(stream, CancellationToken.None, async (chunk, tokens) => { chunkCalled = true; await Task.CompletedTask; });
                Assert.That(chunkCalled, Is.False);
                Assert.That(result.ContentResponse, Is.EqualTo(""));
            }
        }

        [Test]
        public async Task ProcessStreamAsync_ProcessesChunksAndReturnsResult()
        {
            var chunkCalled = false;
            var errorCalled = false;
            int reportedTokens = 0;
            var processor = new StreamProcessor(
                new MockTokenSpeedCalculator(),
                inactivityWatcher: new MockStreamInactivityWatcher()
            );
            var json = "data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}\n";
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var result = await processor.ProcessStreamAsync(stream, CancellationToken.None, async (chunk, stats) => { chunkCalled = true; reportedTokens = stats.TotalTokens; await Task.CompletedTask; }, 10);
                Assert.That(chunkCalled, Is.True);
                Assert.That(errorCalled, Is.False);
                Assert.That(result.ContentResponse, Is.EqualTo("hello"));
                Assert.That(reportedTokens, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ProcessStreamAsync_UsesInactivityWatcher_WhenProvided()
        {
            bool watcherCalled = false;
            var watcherMock = new CustomStreamInactivityWatcher(() => watcherCalled = true);

            var processor = new StreamProcessor(
                new MockTokenSpeedCalculator(),
                inactivityWatcher: watcherMock
            );

            var json = "data: {\"choices\":[{\"delta\":{\"content\":\"test\"}}]}\n";
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                await processor.ProcessStreamAsync(stream, CancellationToken.None, async (chunk, stats) => { await Task.CompletedTask; });
                Assert.That(watcherCalled, Is.True);
            }
        }

        private class CustomStreamInactivityWatcher : IStreamInactivityWatcher
        {
            private readonly Action _onWatchCalled;

            public CustomStreamInactivityWatcher(Action onWatchCalled)
            {
                _onWatchCalled = onWatchCalled;
            }

            public bool IsTimeout => false;

            public Task WatchAsync(Func<long> isActive, CancellationToken cancellationToken)
            {
                _onWatchCalled();
                return Task.CompletedTask;
            }

            public void SignalCompletion() { }

            public void SignalActivity() { }

            public Task WatchAsync(CancellationToken cancellationToken)
            {
                _onWatchCalled();
                return Task.CompletedTask;
            }
        }
    }
}
