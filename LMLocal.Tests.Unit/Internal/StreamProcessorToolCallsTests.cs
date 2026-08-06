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
    public class StreamProcessorToolCallsTests
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
        public async Task ProcessStreamAsync_CollectsToolCalls_MetadataAndArguments()
        {
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockSettingsManager());

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
