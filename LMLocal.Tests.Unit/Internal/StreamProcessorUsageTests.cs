using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Settings;
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
    }
}
