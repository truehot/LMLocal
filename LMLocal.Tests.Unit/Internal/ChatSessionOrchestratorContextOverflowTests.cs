using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.SubAgents;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.WebView;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Internal
{
    [TestFixture]
    public class ChatSessionOrchestratorContextOverflowTests
    {
        private Mock<IChatStreamService> _chatServiceMock;
        private Mock<IToolExecutionManager> _toolManagerMock;
        private Mock<IHistoryCompactor> _compactorMock;
        private Mock<IChatHistoryManager> _historyMock;
        private Mock<ISnapshotManager> _snapshotManagerMock;
        private Mock<IToolCallLoopDetector> _loopDetectorMock;
        private Mock<ISubAgentsParallelPolicy> _parallelPolicyMock;
        private Mock<ISubAgentsParallelRunner> _parallelRunnerMock;
        private List<WebView2ScriptMessage> _messages;
        private List<bool> _retryFlags;
        private List<bool> _toolRoundFlags;

        [SetUp]
        public void SetUp()
        {
            _chatServiceMock = new Mock<IChatStreamService>();
            _toolManagerMock = new Mock<IToolExecutionManager>();
            _compactorMock = new Mock<IHistoryCompactor>();
            _historyMock = new Mock<IChatHistoryManager>();
            _snapshotManagerMock = new Mock<ISnapshotManager>();
            _loopDetectorMock = new Mock<IToolCallLoopDetector>();
            _parallelPolicyMock = new Mock<ISubAgentsParallelPolicy>();
            _parallelRunnerMock = new Mock<ISubAgentsParallelRunner>();

            _messages = new List<WebView2ScriptMessage>();
            _retryFlags = new List<bool>();
            _toolRoundFlags = new List<bool>();

            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(false);
            _compactorMock.Setup(c => c.IsCompactionEnabled()).Returns(true);

            _historyMock.Setup(h => h.GetHistoryCopy()).Returns(new List<ChatMessage>());
            _historyMock
                .Setup(h => h.ReplaceHistoryWithNewSessionAsync(It.IsAny<IEnumerable<ChatMessage>>()))
                .ReturnsAsync(true);
        }

        private ChatSessionOrchestrator CreateOrchestrator()
        {
            return new ChatSessionOrchestrator(
                _chatServiceMock.Object,
                _toolManagerMock.Object,
                _compactorMock.Object,
                _historyMock.Object,
                _snapshotManagerMock.Object,
                _loopDetectorMock.Object,
                _parallelPolicyMock.Object,
                _parallelRunnerMock.Object);
        }

        private static StreamCompletionResult Overflow()
        {
            return new StreamCompletionResult
            {
                ContentResponse = string.Empty,
                FinishReason = "error",
                ErrorMessage = "This model's maximum context length is 8192 tokens",
                ErrorType = "exceed_context_size_error"
            };
        }

        private static StreamCompletionResult Success()
        {
            return new StreamCompletionResult
            {
                ContentResponse = "done",
                FinishReason = "stop",
                ToolCalls = new List<ToolCallRecord>()
            };
        }

        /// <summary>
        /// Each round returns the next result from the sequence; the last one is repeated.
        /// </summary>
        private void SetupRounds(params StreamCompletionResult[] results)
        {
            int index = 0;

            _chatServiceMock
                .Setup(s => s.GenerateStreamAsync(
                    It.IsAny<GenerateStreamContext>(),
                    It.IsAny<List<ToolResultMessage>>(),
                    It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                    It.IsAny<Func<StreamCompletionResult, Task>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, List<ToolResultMessage>, Func<TextStreamChunk, TokenGenerationStats, Task>, Func<StreamCompletionResult, Task>, CancellationToken>(
                    async (gctx, toolResults, onChunk, onComplete, ct) =>
                    {
                        _retryFlags.Add(gctx.IsContextOverflowRetry);
                        _toolRoundFlags.Add(toolResults != null && toolResults.Count > 0);

                        var result = results[Math.Min(index, results.Length - 1)];
                        index++;

                        if (onComplete != null)
                            await onComplete(result).ConfigureAwait(false);
                    });
        }

        private Task RunSessionAsync()
        {
            async Task OnMessage(WebView2ScriptMessage msg)
            {
                _messages.Add(msg);
                await Task.CompletedTask;
            }

            var context = new GenerateStreamContext { Prompt = "prompt", ModelId = "m" };
            return CreateOrchestrator().RunSessionAsync(context, OnMessage, CancellationToken.None);
        }

        [Test]
        public async Task Overflow_CompactsAndRetries_ThenCompletes()
        {
            SetupRounds(Overflow(), Success());
            _compactorMock
                .Setup(c => c.CompactForcedAsync("m", It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CompactionOutcome.Success, (IReadOnlyList<ChatMessage>)new List<ChatMessage>
                {
                    new ChatMessage("user", "Summary of the earlier part of this session:"),
                    new ChatMessage("assistant", "summary")
                }));

            await RunSessionAsync().ConfigureAwait(false);

            Assert.That(_retryFlags.Count, Is.EqualTo(2), "one failed round plus one retry");
            Assert.That(_retryFlags[0], Is.False);
            Assert.That(_retryFlags[1], Is.True, "retry must skip AddUserMessage");

            Assert.That(_messages.Any(m => m.Type == WebView2MessageType.ChatSessionError), Is.False);
            Assert.That(_messages.Any(m => m.Type == WebView2MessageType.ChatSessionComplete), Is.True);
            Assert.That(_messages.Any(m => m.Type == WebView2MessageType.CompactionStart), Is.True);

            // Exactly one compaction happens here: the recovery one. The end-of-session compaction is not reached
            // because NeedsCompaction() stays false, so the log cannot contain a second pair.
            Assert.That(_messages.Count(m => m.Type == WebView2MessageType.CompactionStart), Is.EqualTo(1),
                "only the recovery compaction runs in this scenario");

            // The session continues with a retry, so the compaction must end with Continues = true. A plain
            // CompactionEnd would return the UI to idle, let a second session start while the retry is still
            // pending, and make the retry append to the bubble of the failed round.
            var compactionEnd = _messages.OfType<WebView2CompactionEndMessage>().Single();
            Assert.That(compactionEnd.Continues, Is.True);

            // Ordering: the compaction must end before the retry asks for generation, so the first chunk of the
            // retry cannot land in the previous bubble.
            int compactionEndIndex = _messages.IndexOf(compactionEnd);
            int lastCompactionStart = _messages.FindLastIndex(m => m.Type == WebView2MessageType.CompactionStart);
            Assert.That(compactionEndIndex, Is.GreaterThan(lastCompactionStart));

            _compactorMock.Verify(c => c.CompactForcedAsync("m", 0, It.IsAny<CancellationToken>()), Times.Once);
            _historyMock.Verify(
                h => h.ReplaceHistoryWithNewSessionAsync(It.IsAny<IEnumerable<ChatMessage>>()),
                Times.Once,
                "the compacted history must be applied and written as a new session, so a restart loads the short version");
        }

        [Test]
        public async Task Overflow_GateDisabled_SendsErrorWithoutCompacting()
        {
            SetupRounds(Overflow());
            _compactorMock.Setup(c => c.IsCompactionEnabled()).Returns(false);

            await RunSessionAsync().ConfigureAwait(false);

            Assert.That(_messages.Any(m => m.Type == WebView2MessageType.ChatSessionError), Is.True);
            _compactorMock.Verify(c => c.CompactForcedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(_retryFlags.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task Overflow_AttemptsExhausted_SendsError()
        {
            SetupRounds(Overflow());
            _compactorMock
                .Setup(c => c.CompactForcedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CompactionOutcome.Success, (IReadOnlyList<ChatMessage>)null));

            await RunSessionAsync().ConfigureAwait(false);

            // Initial round + MAX_CONTEXT_OVERFLOW_RETRIES (2) retries.
            Assert.That(_retryFlags.Count, Is.EqualTo(3));
            _compactorMock.Verify(c => c.CompactForcedAsync("m", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            Assert.That(_messages.Any(m => m.Type == WebView2MessageType.ChatSessionError), Is.True);
        }

        [Test]
        public async Task Overflow_NothingToCompact_SendsError()
        {
            SetupRounds(Overflow());
            _compactorMock
                .Setup(c => c.CompactForcedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CompactionOutcome.NothingChanged, (IReadOnlyList<ChatMessage>)null));

            await RunSessionAsync().ConfigureAwait(false);

            Assert.That(_retryFlags.Count, Is.EqualTo(1));
            Assert.That(_messages.Any(m => m.Type == WebView2MessageType.ChatSessionError), Is.True);
        }

        [Test]
        public async Task Overflow_CompactionCancelled_SendsCancelled()
        {
            SetupRounds(Overflow());
            _compactorMock
                .Setup(c => c.CompactForcedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CompactionOutcome.Cancelled, (IReadOnlyList<ChatMessage>)null));

            await RunSessionAsync().ConfigureAwait(false);

            Assert.That(_messages.Any(m => m.Type == WebView2MessageType.ChatSessionCancelled), Is.True);
            Assert.That(_messages.Any(m => m.Type == WebView2MessageType.ChatSessionError), Is.False);
        }

        [Test]
        public async Task NonOverflowError_DoesNotCompact()
        {
            SetupRounds(new StreamCompletionResult
            {
                ContentResponse = string.Empty,
                FinishReason = "error",
                ErrorMessage = "Invalid API key",
                HttpStatusCode = 401
            });

            await RunSessionAsync().ConfigureAwait(false);

            _compactorMock.Verify(c => c.CompactForcedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(_messages.Any(m => m.Type == WebView2MessageType.ChatSessionError), Is.True);
            Assert.That(_retryFlags.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task Overflow_Recovered_SkipsEndOfSessionCompaction()
        {
            // The history was already compacted during recovery. Running the threshold-based end-of-session
            // compaction afterwards would summarize it a second time and add a second entry to the chat history.
            SetupRounds(Overflow(), Success());
            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(true);
            _compactorMock
                .Setup(c => c.CompactForcedAsync("m", It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CompactionOutcome.Success, (IReadOnlyList<ChatMessage>)new List<ChatMessage>
                {
                    new ChatMessage("user", "Summary of the earlier part of this session:"),
                    new ChatMessage("assistant", "summary")
                }));

            await RunSessionAsync().ConfigureAwait(false);

            _compactorMock.Verify(
                c => c.CompactIfNeededAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "a compacted session must not be compacted again at its end");
        }

        [Test]
        public async Task NoOverflow_EndOfSessionCompactionStillRuns()
        {
            // The flag must only suppress the second compaction, never the normal one.
            SetupRounds(Success());
            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(true);

            await RunSessionAsync().ConfigureAwait(false);

            _compactorMock.Verify(
                c => c.CompactIfNeededAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
