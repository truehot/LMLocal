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
    public class StreamProcessorToolCallsTests
    {
        private class MockTokenSpeedCalculator : ITokenSpeedCalculator
        {
            public void Update(int totalTokens) { }
            public double GetTokensPerSecond() => 0.0;
        }

        private class MockWatcher : IStreamInactivityWatcher
        {
            public bool IsTimeout => false;
            public void SignalCompletion() { }
            public void SignalActivity() { }
            public Task WatchAsync(System.Func<long> activityTimeMs, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task WatchAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        [Test]
        public async Task ProcessStreamAsync_CollectsToolCalls_MetadataAndArguments()
        {
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockWatcher());

            var sb = new StringBuilder();
            sb.AppendLine("data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call0\",\"function\":{\"name\":\"fn0\"}}]}}]}");
            sb.AppendLine("data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"a\\\":1}\"}}]}}]}");
            sb.AppendLine("data: [DONE]");

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString())))
            {
                var result = await processor.ProcessStreamAsync(stream, CancellationToken.None, async (chunk, stats) => { await Task.CompletedTask; }, batchIntervalMs: 1);

                Assert.That(result.ToolCalls, Is.Not.Null);
                Assert.That(result.ToolCalls.Count, Is.EqualTo(1));

                var call = result.ToolCalls[0];
                Assert.That(call.Index, Is.EqualTo(0));
                Assert.That(call.CallId, Is.EqualTo("call0"));
                Assert.That(call.FunctionName, Is.EqualTo("fn0"));
                Assert.That(call.ArgumentsJson, Does.Contain("\"a\":1"));
            }
        }
    }
}
