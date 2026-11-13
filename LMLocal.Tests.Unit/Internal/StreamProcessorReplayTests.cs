using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Streaming;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    /// <summary>
    /// Replays a real broken generation through <see cref="StreamProcessor"/>: the 2026-11-12 19:31 explorer run where
    /// gemma-4-e4b wrote find_files arguments as file_name="..." and LM Studio kept the rest of the output inside the
    /// arguments (13 074 tokens until it was stopped by hand). The fixture holds the first 1100 SSE packets, rebuilt from
    /// the LM Studio server log by Script\sse-replay.csx: reasoning, the call metadata at packet 162, the first control token
    /// at packet 180 (arguments 62 chars), and arguments past 4000 chars at packet 1014.
    /// Plan: Docs\broken-tool-call-stream-abort-plan.md.
    /// </summary>
    [TestFixture]
    public class StreamProcessorReplayTests
    {
        private static readonly string FixtureRelativePath =
            Path.Combine("LMLocal.Tests.Unit", "Fixtures", "gemma-broken-find-files-20261112.sse");

        /// <summary>
        /// Remembers why the inner guard stopped the call and how long the arguments were at that moment.
        /// </summary>
        private sealed class RecordingGuard : IToolCallArgumentsGuard
        {
            private readonly IToolCallArgumentsGuard _inner;

            public RecordingGuard(IToolCallArgumentsGuard inner)
            {
                _inner = inner;
            }

            public string Reason { get; private set; }
            public int ArgumentsLength { get; private set; }

            public string Check(string functionName, StringBuilder arguments, string fragment)
            {
                string reason = _inner.Check(functionName, arguments, fragment);
                if (reason != null && Reason == null)
                {
                    Reason = reason;
                    ArgumentsLength = arguments.Length;
                }

                return reason;
            }
        }

        private static string ResolveFixturePath()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "LMLocal.sln")))
                    return Path.Combine(dir.FullName, FixtureRelativePath);
                dir = dir.Parent;
            }

            return null;
        }

        private static ISettingsManager CreateSettings()
        {
            var settings = new Mock<ISettingsManager>();
            settings.Setup(s => s.WindowSeconds).Returns(5);
            settings.Setup(s => s.Current).Returns(new AppSettings { StreamInactivityTimeoutSeconds = 0 });
            return settings.Object;
        }

        private static async Task<StreamCompletionResult> ReplayAsync(IToolCallArgumentsGuard guard)
        {
            string path = ResolveFixturePath();
            Assert.That(path != null && File.Exists(path), Is.True, "Fixture not found: " + FixtureRelativePath);

            var sse = new StringBuilder();
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                sse.AppendLine(line);
            sse.AppendLine("data: [DONE]");

            var processor = new StreamProcessor(Mock.Of<ITokenSpeedCalculator>(), CreateSettings(), guard);
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(sse.ToString())))
            {
                return await processor.ProcessStreamAsync(stream, CancellationToken.None,
                    async (chunk, stats) => { await Task.CompletedTask; }, batchIntervalMs: 1);
            }
        }

        private static void AssertCallAborted(StreamCompletionResult result)
        {
            Assert.That(result.StopReason, Is.EqualTo(StreamStopReason.ToolCallAborted));
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
            Assert.That(result.ToolCalls[0].FunctionName, Is.EqualTo("find_files"));
            Assert.That(result.ToolCalls[0].IsInvalid, Is.True);
            Assert.That(result.ToolCalls[0].ArgumentsJson, Is.EqualTo("{}"));
        }

        [Test]
        public async Task Replay_GemmaGuard_AbortsOnFirstControlToken()
        {
            var guard = new RecordingGuard(new ToolCallArgumentsGuardFactory().Create("google_gemma-4-e4b-it"));

            var result = await ReplayAsync(guard);

            AssertCallAborted(result);
            Assert.That(guard.Reason, Does.Contain(GemmaControlTokens.EscapedStringDelimiter));
            Assert.That(guard.ArgumentsLength, Is.EqualTo(62), "the first token arrives with arguments 62 chars long");
        }

        [Test]
        public async Task Replay_OtherModel_AbortsOnLengthLimit()
        {
            // The same stream under a model id without the Gemma token check: only the length limit stops it.
            var guard = new RecordingGuard(new ToolCallArgumentsGuardFactory().Create("qwen/qwen3-coder-30b"));

            var result = await ReplayAsync(guard);

            AssertCallAborted(result);
            Assert.That(guard.Reason, Does.Contain("exceed " + ToolArgumentLimits.SimpleToolMaxLength));
            Assert.That(guard.ArgumentsLength, Is.GreaterThan(ToolArgumentLimits.SimpleToolMaxLength)
                .And.LessThan(ToolArgumentLimits.SimpleToolMaxLength + 100));
        }

        [Test]
        public async Task Replay_NoGuard_ReadsWholeFixture()
        {
            // Without a guard nothing stops the call while it streams; the broken JSON is caught only at the end.
            var result = await ReplayAsync(null);

            Assert.That(result.StopReason, Is.EqualTo(StreamStopReason.Completed));
            Assert.That(result.ToolCalls.Count, Is.EqualTo(1));
            Assert.That(result.ToolCalls[0].IsInvalid, Is.True);
        }
    }
}
