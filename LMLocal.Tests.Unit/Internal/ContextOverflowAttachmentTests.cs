using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.SubAgents;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.WebView;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Internal
{
    /// <summary>
    /// A prompt that does not fit the window on its own cannot be recovered by compacting: the retry sends the very
    /// same prompt. The recovery path must say so instead of spending an LLM call and losing the tool cycle.
    /// </summary>
    [TestFixture]
    public class ContextOverflowAttachmentTests
    {
        private Mock<IChatStreamService> _chatService;
        private Mock<IToolExecutionManager> _toolManager;
        private Mock<IHistoryCompactor> _compactor;
        private Mock<IChatHistoryManager> _history;
        private Mock<ISnapshotManager> _snapshot;
        private Mock<IToolCallLoopDetector> _loopDetector;
        private Mock<ISubAgentsParallelPolicy> _parallelPolicy;
        private Mock<ISubAgentsParallelRunner> _parallelRunner;
        private Mock<IActiveModelContext> _modelContext;
        private List<WebView2ScriptMessage> _messages;
        private List<ChatMessage> _historyMessages;

        [SetUp]
        public void SetUp()
        {
            _chatService = new Mock<IChatStreamService>();
            _toolManager = new Mock<IToolExecutionManager>();
            _compactor = new Mock<IHistoryCompactor>();
            _history = new Mock<IChatHistoryManager>();
            _snapshot = new Mock<ISnapshotManager>();
            _loopDetector = new Mock<IToolCallLoopDetector>();
            _parallelPolicy = new Mock<ISubAgentsParallelPolicy>();
            _parallelRunner = new Mock<ISubAgentsParallelRunner>();
            _modelContext = new Mock<IActiveModelContext>();

            _messages = new List<WebView2ScriptMessage>();
            _historyMessages = new List<ChatMessage>();

            _compactor.Setup(c => c.IsCompactionEnabled()).Returns(true);
            _compactor.Setup(c => c.NeedsCompaction()).Returns(false);

            _history.Setup(h => h.GetHistoryCopy()).Returns(() => _historyMessages);
            _history
                .Setup(h => h.ReplaceHistoryWithNewSessionAsync(It.IsAny<IEnumerable<ChatMessage>>()))
                .ReturnsAsync(true);
        }

        private ChatSessionOrchestrator CreateOrchestrator()
        {
            return new ChatSessionOrchestrator(
                _chatService.Object,
                _toolManager.Object,
                _compactor.Object,
                _history.Object,
                _snapshot.Object,
                _loopDetector.Object,
                _parallelPolicy.Object,
                _parallelRunner.Object,
                activeModelContext: _modelContext.Object);
        }

        private static StreamCompletionResult Overflow()
        {
            return new StreamCompletionResult
            {
                ContentResponse = string.Empty,
                FinishReason = "error",
                ErrorMessage = "prompt is too long",
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
        /// Each round returns the next result; the last one repeats.
        /// </summary>
        private Task RunOverflowAsync(params StreamCompletionResult[] results)
        {
            int index = 0;

            _chatService
                .Setup(s => s.GenerateStreamAsync(
                    It.IsAny<GenerateStreamContext>(),
                    It.IsAny<List<ToolResultMessage>>(),
                    It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                    It.IsAny<Func<StreamCompletionResult, Task>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, List<ToolResultMessage>, Func<TextStreamChunk, TokenGenerationStats, Task>, Func<StreamCompletionResult, Task>, CancellationToken>(
                    async (gctx, toolResults, onChunk, onComplete, ct) =>
                    {
                        var result = results[System.Math.Min(index, results.Length - 1)];
                        index++;

                        if (onComplete != null)
                            await onComplete(result).ConfigureAwait(false);
                    });

            async Task OnMessage(WebView2ScriptMessage msg)
            {
                _messages.Add(msg);
                await Task.CompletedTask;
            }

            var context = new GenerateStreamContext { Prompt = "p", ModelId = "m" };
            return CreateOrchestrator().RunSessionAsync(context, OnMessage, CancellationToken.None);
        }

        [Test]
        public async Task NothingToCompress_ReportsTheHonestWindowError()
        {
            _compactor
                .Setup(c => c.CompactForcedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CompactionOutcome.NothingChanged, (IReadOnlyList<ChatMessage>)null));

            await RunOverflowAsync(Overflow());

            _compactor.Verify(
                c => c.CompactForcedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Once,
                "only the compactor can tell whether anything is left to compress, so it must always be asked");

            var error = _messages.Find(m => m.Type == WebView2MessageType.ChatSessionError);
            Assert.That(error, Is.Not.Null);
            Assert.That(error.Payload, Does.Contain("does not fit the model context window"));
        }

        [Test]
        public async Task SmallPrompt_StillRecovers()
        {
            var compacted = new List<ChatMessage>
            {
                new ChatMessage("user", "Summary of the earlier part of this session:"),
                new ChatMessage("assistant", "summary")
            };

            _compactor
                .Setup(c => c.CompactForcedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CompactionOutcome.Success, (IReadOnlyList<ChatMessage>)compacted));

            _historyMessages.Add(new ChatMessage("user", "a normal short prompt"));

            await RunOverflowAsync(Overflow(), Success());

            _compactor.Verify(
                c => c.CompactForcedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Once);
            Assert.That(_messages.Exists(m => m.Type == WebView2MessageType.ChatSessionError), Is.False);
        }

        [Test]
        public async Task PromptFits_AttemptsCompaction()
        {
            _compactor
                .Setup(c => c.CompactForcedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CompactionOutcome.NothingChanged, (IReadOnlyList<ChatMessage>)null));

            await RunOverflowAsync(Overflow());

            _compactor.Verify(
                c => c.CompactForcedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Once,
                "a prompt that fits must not be blocked by the early check");
        }
    }
}


