using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Streaming;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class StreamProcessorTests
    {
        private class MockSettingsManager : ISettingsManager
        {
            public AppSettings Current { get; }

            public MockSettingsManager() : this(0) { }

            public MockSettingsManager(int timeoutSeconds)
            {
                Current = new AppSettings { StreamInactivityTimeoutSeconds = timeoutSeconds };
            }

            public string ApplicationName => "";
            public string SettingsFileName => "";
            public string LocalAppDataFolder => "";
            public string LocalAppSettingFileName => "";
            public string LocalAppInstructionsFileName => "";
            public string LocalAppMcpFileName => "";
            public string WebViewUserDataFolder => "";
            public string ChatHistoryFolder => "";
            public string ChatHistoryFileLabel => "";
            public string HtmlResourcePath => "";
            public string VirtualHostName => "";
            public string SystemPrompt => "";
            public int BatchIntervalMs => 100;
            public int WindowSeconds => 5;
            public int RequestTimeoutSeconds => 105;
            public string SnapshotFolder => "";
            public string LocalSnapshotsFileName => "";
            public string UserAgent => "";
            public string AssistantPlaceholder => "";

            public event Action<AppSettings> SettingsChanged { add { } remove { } }

            public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
            public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task SetAiToolsModeAsync(string mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        private class MockTokenSpeedCalculator : ITokenSpeedCalculator
        {
            public void Update(int totalTokens) { }
            public double GetTokensPerSecond() => 0.0;
            public double GetAverageTokensPerSecond() => 0.0;
        }

        [Test]
        public void ProcessStreamAsync_Throws_OnCancellation()
        {
            var processor = new StreamProcessor(
                new MockTokenSpeedCalculator(),
                new MockSettingsManager()
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
                new MockSettingsManager()
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
                new MockSettingsManager()
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
                new MockSettingsManager()
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
                new MockSettingsManager()
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
                new MockSettingsManager()
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
                new MockSettingsManager()
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
                new MockSettingsManager()
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
        [Timeout(10000)] // 10s safety for CI
        public async Task ProcessStreamAsync_ReturnsTimeoutError_WhenNetworkStreamHangs()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;

                // Connect a client that never sends data
                using (var client = new TcpClient())
                {
                    await client.ConnectAsync(IPAddress.Loopback, port);

                    using (var server = await listener.AcceptTcpClientAsync())
                    using (var serverStream = server.GetStream())
                    {
                        var processor = new StreamProcessor(
                            new MockTokenSpeedCalculator(),
                            new MockSettingsManager(1) // 1 second timeout
                        );

                        var result = await processor.ProcessStreamAsync(
                            serverStream,
                            CancellationToken.None,
                            null,
                            batchIntervalMs: 1 // small interval so consumer exits quickly after timeout
                        );

                        Assert.That(result.ErrorMessage, Is.EqualTo("Stream read timeout"));
                        Assert.That(result.WasCancelled, Is.False);
                    }
                }
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
