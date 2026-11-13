using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Core.Models;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Infrastructure.Streaming;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class StreamProcessorToolCallsTests
    {
        private const string DoneMarker = "data: [DONE]";

        private class MockTokenSpeedCalculator : ITokenSpeedCalculator
        {
            public void Add(int chars, double estimatedTokens) { }
            public double GetTokensPerSecond() => 0.0;
            public double GetAverageTokensPerSecond() => 0.0;
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

        /// <summary>
        /// Builds a full SSE data line: data: {"choices":[{"delta":{"tool_calls":[<toolCall>]}}]}
        /// Serializing through JObject removes any hand-escaping/JSON-validity mistakes.
        /// </summary>
        private static string DeltaLine(JObject toolCall)
        {
            var delta = new JObject(new JProperty("tool_calls", new JArray(toolCall)));
            var choice = new JObject(new JProperty("delta", delta));
            var root = new JObject(new JProperty("choices", new JArray(choice)));
            return "data: " + root.ToString(Formatting.None);
        }

        private static JObject MetaCall(int index, string callId, string functionName, string extraContentJson = null)
        {
            var call = new JObject(
                new JProperty("index", index),
                new JProperty("id", callId),
                new JProperty("function", new JObject(new JProperty("name", functionName))));

            if (extraContentJson != null)
                call.Add("extra_content", JToken.Parse(extraContentJson));

            return call;
        }

        private static JObject ArgsCall(int index, string argumentsFragment)
        {
            return new JObject(
                new JProperty("index", index),
                new JProperty("function", new JObject(new JProperty("arguments", argumentsFragment))));
        }

        private static async Task<StreamCompletionResult> RunAsync(StreamProcessor processor, string[] dataLines)
        {
            var sb = new StringBuilder();
            foreach (var line in dataLines)
                sb.AppendLine(line);
            sb.AppendLine(DoneMarker);

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString())))
            {
                return await processor.ProcessStreamAsync(stream, CancellationToken.None,
                    async (chunk, stats) => { await Task.CompletedTask; }, batchIntervalMs: 1);
            }
        }

        [Test]
        public async Task ProcessStreamAsync_CollectsToolCalls_MetadataAndArguments()
        {
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockSettingsManager());

            var result = await RunAsync(processor, new[]
            {
                DeltaLine(MetaCall(0, "call0", "fn0")),
                DeltaLine(ArgsCall(0, "{\"a\":1}"))
            });

            Assert.That(result.ToolCalls, Is.Not.Null);
            Assert.That(result.ToolCalls.Count, Is.EqualTo(1));

            var call = result.ToolCalls[0];
            Assert.That(call.Index, Is.EqualTo(0));
            Assert.That(call.CallId, Is.EqualTo("call0"));
            Assert.That(call.FunctionName, Is.EqualTo("fn0"));
            Assert.That(call.ArgumentsJson, Does.Contain("\"a\":1"));
            Assert.That(call.IsInvalid, Is.False);
        }

        [Test]
        public async Task ProcessStreamAsync_InvalidToolArguments_SanitizesToEmptyObjectAndMarksInvalid()
        {
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockSettingsManager());

            var result = await RunAsync(processor, new[]
            {
                DeltaLine(MetaCall(0, "call0", "fn0")),
                DeltaLine(ArgsCall(0, "{\"a\":"))
            });

            Assert.That(result.ToolCalls, Is.Not.Null);
            Assert.That(result.ToolCalls.Count, Is.EqualTo(1));

            var call = result.ToolCalls[0];
            Assert.That(call.CallId, Is.EqualTo("call0"));
            Assert.That(call.FunctionName, Is.EqualTo("fn0"));
            Assert.That(call.IsInvalid, Is.True);
            Assert.That(call.ArgumentsJson, Is.EqualTo("{}"));
        }

        // ================ Gemini extra_content (thought_signature echo-back) ================

        [Test]
        public async Task ProcessStreamAsync_ExtraContent_ReachesToolCallRecord()
        {
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockSettingsManager());

            var result = await RunAsync(processor, new[]
            {
                DeltaLine(MetaCall(0, "call0", "fn0", "{\"google\":{\"thought_signature\":\"sigA\"}}"))
            });

            Assert.That(result.ToolCalls, Is.Not.Null);
            Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
            Assert.That(result.ToolCalls[0].ExtraContentJson, Is.EqualTo("{\"google\":{\"thought_signature\":\"sigA\"}}"));
        }

        [Test]
        public async Task ProcessStreamAsync_NoExtraContent_ExtraContentJsonNull()
        {
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockSettingsManager());

            var result = await RunAsync(processor, new[]
            {
                DeltaLine(MetaCall(0, "call0", "fn0"))
            });

            Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
            Assert.That(result.ToolCalls[0].ExtraContentJson, Is.Null);
        }

        [Test]
        public async Task ProcessStreamAsync_ArgumentsAcrossMultipleDeltas_Accumulate()
        {
            // Split JSON arguments across several deltas must be appended into one buffer.
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockSettingsManager());

            var result = await RunAsync(processor, new[]
            {
                DeltaLine(MetaCall(0, "call0", "fn0")),
                DeltaLine(ArgsCall(0, "{\"a\":")),
                DeltaLine(ArgsCall(0, "1}"))
            });

            Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
            Assert.That(result.ToolCalls[0].ArgumentsJson, Is.EqualTo("{\"a\":1}"));
            Assert.That(result.ToolCalls[0].IsInvalid, Is.False);
        }

        [Test]
        public async Task ProcessStreamAsync_ArgumentsArriveBeforeMetadata_StillAttached()
        {
            // Provider may stream arguments before the id/name delta (metadata must not lose buffers).
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockSettingsManager());

            var result = await RunAsync(processor, new[]
            {
                DeltaLine(ArgsCall(0, "{\"a\":1}")),
                DeltaLine(MetaCall(0, "call0", "fn0"))
            });

            Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
            Assert.That(result.ToolCalls[0].CallId, Is.EqualTo("call0"));
            Assert.That(result.ToolCalls[0].FunctionName, Is.EqualTo("fn0"));
            Assert.That(result.ToolCalls[0].ArgumentsJson, Is.EqualTo("{\"a\":1}"));
        }

        [Test]
        public async Task ProcessStreamAsync_RepeatedMetadataDelta_LastWins()
        {
            // Overwrite semantics: when id/name/extra_content are re-sent, the LAST delta wins.
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockSettingsManager());

            var result = await RunAsync(processor, new[]
            {
                DeltaLine(MetaCall(0, "call0", "fn0", "{\"google\":{\"thought_signature\":\"OLD\"}}")),
                DeltaLine(MetaCall(0, "call1", "fn1", "{\"google\":{\"thought_signature\":\"NEW\"}}"))
            });

            Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
            Assert.That(result.ToolCalls[0].CallId, Is.EqualTo("call1"));
            Assert.That(result.ToolCalls[0].FunctionName, Is.EqualTo("fn1"));
            Assert.That(result.ToolCalls[0].ExtraContentJson, Is.EqualTo("{\"google\":{\"thought_signature\":\"NEW\"}}"));
        }

        [Test]
        public async Task ProcessStreamAsync_ParallelCalls_ExtraContentOnFirstOnly()
        {
            // Parallel calls: signature only on the first tool call of the response (Gemini docs).
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockSettingsManager());

            var result = await RunAsync(processor, new[]
            {
                DeltaLine(MetaCall(0, "call0", "fn0", "{\"google\":{\"thought_signature\":\"sigA\"}}")),
                DeltaLine(MetaCall(1, "call1", "fn1")),
                DeltaLine(ArgsCall(0, "{\"a\":1}")),
                DeltaLine(ArgsCall(1, "{\"b\":2}"))
            });

            Assert.That(result.ToolCalls.Count, Is.EqualTo(2));

            var first = result.ToolCalls.First(t => t.Index == 0);
            var second = result.ToolCalls.First(t => t.Index == 1);

            Assert.That(first.CallId, Is.EqualTo("call0"));
            Assert.That(first.ArgumentsJson, Is.EqualTo("{\"a\":1}"));
            Assert.That(first.ExtraContentJson, Is.EqualTo("{\"google\":{\"thought_signature\":\"sigA\"}}"));

            Assert.That(second.CallId, Is.EqualTo("call1"));
            Assert.That(second.ArgumentsJson, Is.EqualTo("{\"b\":2}"));
            Assert.That(second.ExtraContentJson, Is.Null);
        }

        [Test]
        public async Task ProcessStreamAsync_ReusedInstance_SecondRunHasNoLeftovers()
        {
            // Buffers/metadata are instance-level; a second run must not leak the first run's tool calls.
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockSettingsManager());

            var first = await RunAsync(processor, new[]
            {
                DeltaLine(MetaCall(0, "call0", "fn0")),
                DeltaLine(ArgsCall(0, "{\"a\":1}"))
            });
            Assert.That(first.ToolCalls.Count, Is.EqualTo(1));

            // Second run: plain content only — no leftover tool calls from the first run.
            var second = await RunAsync(processor, new[] { "data: {\"choices\":[{\"delta\":{\"content\":\"plain\"}}]}" });
            Assert.That(second.ToolCalls, Is.Not.Null);
            Assert.That(second.ToolCalls.Count, Is.EqualTo(0));
            Assert.That(second.ContentResponse, Is.EqualTo("plain"));
        }

        // ================ Arguments guard (broken Gemma tool calls) ================

        private static string ContentLine(string text)
        {
            var delta = new JObject(new JProperty("content", text));
            var root = new JObject(new JProperty("choices", new JArray(new JObject(new JProperty("delta", delta)))));
            return "data: " + root.ToString(Formatting.None);
        }

        /// <summary>
        /// The 2026-11-12 find_files case: content, the call, broken arguments, then more output that must not be read.
        /// </summary>
        private static string[] BrokenFindFilesLines()
        {
            return new[]
            {
                ContentLine("Before"),
                DeltaLine(MetaCall(0, "call0", "find_files")),
                DeltaLine(ArgsCall(0, "{\"file_name=\\\"*StreamErrorClassifier*\\\", max_results=500")),
                DeltaLine(ArgsCall(0, "<|\\\"|>")),
                ContentLine("AFTER"),
                DeltaLine(ArgsCall(0, "\\n<|channel>thought"))
            };
        }

        [Test]
        public async Task ProcessStreamAsync_GuardRejectsArguments_StopsReadingAndMarksCallInvalid()
        {
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockSettingsManager(), GemmaToolCallArgumentsGuard.Instance);

            var result = await RunAsync(processor, BrokenFindFilesLines());

            Assert.That(result.StopReason, Is.EqualTo(StreamStopReason.ToolCallAborted));
            Assert.That(result.WasCancelled, Is.False);
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(result.FinishReason, Is.Null);
            Assert.That(result.ContentResponse, Is.EqualTo("Before"), "lines after the abort are not read");
            Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
            Assert.That(result.ToolCalls[0].FunctionName, Is.EqualTo("find_files"));
            Assert.That(result.ToolCalls[0].IsInvalid, Is.True);
            Assert.That(result.ToolCalls[0].ArgumentsJson, Is.EqualTo("{}"));
        }

        [Test]
        public async Task ProcessStreamAsync_NoGuard_SameStream_ReadToTheEnd()
        {
            // Models without a guard keep the old behavior: the whole stream is read, the broken JSON is caught at the end.
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockSettingsManager());

            var result = await RunAsync(processor, BrokenFindFilesLines());

            Assert.That(result.StopReason, Is.EqualTo(StreamStopReason.Completed));
            Assert.That(result.ContentResponse, Is.EqualTo("BeforeAFTER"));
            Assert.That(result.ToolCalls[0].IsInvalid, Is.True);
        }

        [Test]
        public async Task ProcessStreamAsync_GuardWithValidArguments_NoAbort()
        {
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockSettingsManager(), GemmaToolCallArgumentsGuard.Instance);

            var result = await RunAsync(processor, new[]
            {
                DeltaLine(MetaCall(0, "call0", "search_file_content")),
                DeltaLine(ArgsCall(0, "{\"text\":\"x => x > 0\",")),
                DeltaLine(ArgsCall(0, "\"path\":\"a.cs\"}")),
                ContentLine("AFTER")
            });

            Assert.That(result.StopReason, Is.EqualTo(StreamStopReason.Completed));
            Assert.That(result.ContentResponse, Is.EqualTo("AFTER"));
            Assert.That(result.ToolCalls[0].IsInvalid, Is.False);
            Assert.That(result.ToolCalls[0].ArgumentsJson, Is.EqualTo("{\"text\":\"x => x > 0\",\"path\":\"a.cs\"}"));
        }

        [Test]
        public async Task ProcessStreamAsync_SecondCallRejected_FirstCallStaysValid()
        {
            var processor = new StreamProcessor(new MockTokenSpeedCalculator(), new MockSettingsManager(), GemmaToolCallArgumentsGuard.Instance);

            var result = await RunAsync(processor, new[]
            {
                DeltaLine(MetaCall(0, "call0", "read_file_lines")),
                DeltaLine(ArgsCall(0, "{\"file_path\":\"a.cs\"}")),
                DeltaLine(MetaCall(1, "call1", "search_file_content")),
                DeltaLine(ArgsCall(1, "{\"text\":\"}")),
                DeltaLine(ArgsCall(1, "<tool_call|>")),
                DeltaLine(ArgsCall(1, "<|tool_call>call:search_file_content{"))
            });

            Assert.That(result.StopReason, Is.EqualTo(StreamStopReason.ToolCallAborted));
            Assert.That(result.ToolCalls.Count, Is.EqualTo(2));
            Assert.That(result.ToolCalls[0].IsInvalid, Is.False);
            Assert.That(result.ToolCalls[0].ArgumentsJson, Is.EqualTo("{\"file_path\":\"a.cs\"}"));
            Assert.That(result.ToolCalls[1].IsInvalid, Is.True);
        }
    }
}
