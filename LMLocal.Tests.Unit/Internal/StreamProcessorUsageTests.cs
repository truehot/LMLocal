using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Models;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Infrastructure.Streaming;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class StreamProcessorUsageTests
    {
        private class MockTokenSpeedCalculator : ITokenSpeedCalculator
        {
            public List<(int Chars, double Tokens)> Added { get; } = new List<(int, double)>();

            public void Add(int chars, double estimatedTokens) => Added.Add((chars, estimatedTokens));
            public double GetTokensPerSecond() => 0.0;
            public double GetAverageTokensPerSecond() => 42.0;
        }

        private class MockSettingsManager : ISettingsManager
        {
            public AppSettings Current => new AppSettings { StreamInactivityTimeoutSeconds = 0 };
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
            public Task SetSubAgentsEnabledAsync(bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        [Test]
        public async Task ProcessStreamAsync_PopulatesTokenUsage_AndSystemFingerprint()
        {
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockSettingsManager());

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

        [Test]
        public async Task ProcessStreamAsync_PopulatesCachedTokens_FromPromptTokensDetails()
        {
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockSettingsManager());

            var sb = new StringBuilder();
            sb.AppendLine("data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}");
            sb.AppendLine("data: {\"choices\":[],\"usage\":{\"total_tokens\":12,\"prompt_tokens\":5,\"completion_tokens\":7,\"prompt_tokens_details\":{\"cached_tokens\":4}},\"system_fingerprint\":\"fp456\"}");

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString())))
            {
                var result = await processor.ProcessStreamAsync(stream, CancellationToken.None, async (chunk, stats) => { await Task.CompletedTask; });

                Assert.That(result.TokenUsage, Is.Not.Null);
                Assert.That(result.TokenUsage.TotalTokens, Is.EqualTo(12));
                Assert.That(result.TokenUsage.PromptTokens, Is.EqualTo(5));
                Assert.That(result.TokenUsage.CompletionTokens, Is.EqualTo(7));
                Assert.That(result.TokenUsage.CachedTokens, Is.EqualTo(4));
            }
        }

        [Test]
        public async Task ProcessStreamAsync_ReportsAverageSpeed_IgnoringUsageCompletionTokens()
        {
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockSettingsManager());

            var sb = new StringBuilder();
            sb.AppendLine("data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}");
            sb.AppendLine("data: {\"choices\":[],\"usage\":{\"total_tokens\":110,\"completion_tokens\":100}}");

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString())))
            {
                var result = await processor.ProcessStreamAsync(stream, CancellationToken.None,
                    async (chunk, stats) => { await Task.CompletedTask; });

                // The round speed is the calculator's average of readings; provider usage does not override it.
                Assert.That(result.TokensPerSecond, Is.EqualTo(42.0));
            }
        }

        [Test]
        public async Task ProcessStreamAsync_FeedsEveryDeltaToSpeedCalculator()
        {
            var speed = new MockTokenSpeedCalculator();
            var processor = new StreamProcessor(speed, new MockSettingsManager());

            var sb = new StringBuilder();
            sb.AppendLine("data: {\"choices\":[{\"delta\":{\"content\":\"hello\"}}]}");
            sb.AppendLine("data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"path\\\":\\\"a.txt\\\"}\"}}]}}]}");

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString())))
            {
                await processor.ProcessStreamAsync(stream, CancellationToken.None,
                    async (chunk, stats) => { await Task.CompletedTask; });

                // Same chunks as the token counter: content and the initial tool-call arguments are both generation.
                Assert.That(speed.Added.Count, Is.EqualTo(2));
                Assert.That(speed.Added[0].Chars, Is.EqualTo(5));
                Assert.That(speed.Added[0].Tokens, Is.EqualTo(1.25).Within(0.0001));
                Assert.That(speed.Added[1].Chars, Is.EqualTo(16));
                Assert.That(speed.Added[1].Tokens, Is.EqualTo(4.0).Within(0.0001));
            }
        }
    }
}
