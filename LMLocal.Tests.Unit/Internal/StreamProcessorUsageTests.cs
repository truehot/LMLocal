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
    public class StreamProcessorUsageTests
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
        public async Task ProcessStreamAsync_PopulatesTokenUsage_AndSystemFingerprint()
        {
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockWatcher());

            var sb = new StringBuilder();
            sb.AppendLine("data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}");
            sb.AppendLine("data: {\"choices\":[],\"usage\":{\"total_tokens\":10,\"prompt_tokens\":3,\"completion_tokens\":7},\"system_fingerprint\":\"fp123\"}");

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString())))
            {
                var result = await processor.ProcessStreamAsync(stream, CancellationToken.None, async (chunk, stats) => { await Task.CompletedTask; });

                Assert.That(result.ContentResponse, Is.EqualTo("hello"));
                Assert.That(result.TokenUsage, Is.Not.Null);
                Assert.That(result.TokenUsage.TotalTokens, Is.EqualTo(10));
                Assert.That(result.TokenUsage.PromptTokens, Is.EqualTo(3));
                Assert.That(result.TokenUsage.CompletionTokens, Is.EqualTo(7));
                Assert.That(result.SystemFingerprint, Is.EqualTo("fp123"));
            }
        }
    }
}
