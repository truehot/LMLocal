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
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.WebView;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Internal
{
    /// <summary>
    /// A regenerate/edit request replaces the last exchange once, before the first round, and the round itself starts
    /// only when there was something to replace.
    /// </summary>
    [TestFixture]
    public class ChatSessionOrchestratorReplaceLastTests
    {
        private Mock<IChatStreamService> _chatServiceMock;
        private Mock<IToolExecutionManager> _toolManagerMock;
        private Mock<IHistoryCompactor> _compactorMock;
        private Mock<IChatHistoryManager> _historyMock;
        private Mock<ISnapshotManager> _snapshotManagerMock;
        private Mock<IToolCallLoopDetector> _loopDetectorMock;
        private Mock<ISubAgentsParallelPolicy> _parallelPolicyMock;
        private Mock<ISubAgentsParallelRunner> _parallelRunnerMock;

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

            _chatServiceMock
                .Setup(s => s.GenerateStreamAsync(
                    It.IsAny<GenerateStreamContext>(),
                    It.IsAny<List<ToolResultMessage>>(),
                    It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                    It.IsAny<Func<StreamCompletionResult, Task>>(),
                    It.IsAny<Func<RateLimitWaitInfo, Task>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, List<ToolResultMessage>, Func<TextStreamChunk, TokenGenerationStats, Task>, Func<StreamCompletionResult, Task>, Func<RateLimitWaitInfo, Task>, CancellationToken>(
                    async (gctx, toolResults, onChunk, onComplete, onRateLimitWait, ct) =>
                    {
                        if (onComplete != null)
                        {
                            await onComplete(new StreamCompletionResult
                            {
                                StopReason = StreamStopReason.Completed,
                                FinishReason = "stop"
                            }).ConfigureAwait(false);
                        }
                    });

            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(false);
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

        private static List<WebView2ScriptMessage> Collect(WebView2ScriptMessage message, List<WebView2ScriptMessage> into)
        {
            into.Add(message);
            return into;
        }

        [Test]
        public async Task ReplaceAnswer_TruncatesOnceBeforeTheFirstRound()
        {
            _historyMock
                .Setup(h => h.TruncateLastExchangeAsync(true, It.IsAny<string>()))
                .ReturnsAsync(TruncateLastExchangeOutcome.Replaced);

            var messages = new List<WebView2ScriptMessage>();
            var orchestrator = CreateOrchestrator();
            var context = new GenerateStreamContext
            {
                Prompt = string.Empty,
                ModelId = "m",
                ReplaceLast = ReplaceLastModes.Answer,
                ReplaceLastPrompt = "the prompt on screen"
            };

            await orchestrator.RunSessionAsync(
                context,
                msg => { messages.Add(msg); return Task.CompletedTask; },
                CancellationToken.None).ConfigureAwait(false);

            _historyMock.Verify(h => h.TruncateLastExchangeAsync(true, "the prompt on screen"), Times.Once);
            _chatServiceMock.Verify(s => s.GenerateStreamAsync(
                It.IsAny<GenerateStreamContext>(),
                It.IsAny<List<ToolResultMessage>>(),
                It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                It.IsAny<Func<StreamCompletionResult, Task>>(),
                It.IsAny<Func<RateLimitWaitInfo, Task>>(),
                It.IsAny<CancellationToken>()), Times.Once);

            Assert.That(messages.Exists(m => m.Type == WebView2MessageType.ChatSessionError), Is.False);
        }

        [Test]
        public async Task ReplaceExchange_TruncatesWithThePromptIncluded()
        {
            _historyMock
                .Setup(h => h.TruncateLastExchangeAsync(false, It.IsAny<string>()))
                .ReturnsAsync(TruncateLastExchangeOutcome.Replaced);

            var orchestrator = CreateOrchestrator();
            var context = new GenerateStreamContext
            {
                Prompt = "corrected prompt",
                ModelId = "m",
                ReplaceLast = ReplaceLastModes.Exchange,
                ReplaceLastPrompt = "old prompt"
            };

            await orchestrator.RunSessionAsync(
                context,
                _ => Task.CompletedTask,
                CancellationToken.None).ConfigureAwait(false);

            _historyMock.Verify(h => h.TruncateLastExchangeAsync(false, "old prompt"), Times.Once);
            _historyMock.Verify(h => h.TruncateLastExchangeAsync(true, It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task ReplaceLast_NothingToReplace_SendsRefusalAndSkipsTheRound()
        {
            _historyMock
                .Setup(h => h.TruncateLastExchangeAsync(It.IsAny<bool>(), It.IsAny<string>()))
                .ReturnsAsync(TruncateLastExchangeOutcome.NothingToReplace);

            var messages = new List<WebView2ScriptMessage>();
            var orchestrator = CreateOrchestrator();
            var context = new GenerateStreamContext
            {
                Prompt = string.Empty,
                ModelId = "m",
                ReplaceLast = ReplaceLastModes.Answer
            };

            await orchestrator.RunSessionAsync(
                context,
                msg => { messages.Add(msg); return Task.CompletedTask; },
                CancellationToken.None).ConfigureAwait(false);

            AssertRefused(messages);
        }

        [Test]
        public async Task ReplaceLast_OutOfSync_SendsRefusalAndSkipsTheRound()
        {
            _historyMock
                .Setup(h => h.TruncateLastExchangeAsync(It.IsAny<bool>(), It.IsAny<string>()))
                .ReturnsAsync(TruncateLastExchangeOutcome.OutOfSync);

            var messages = new List<WebView2ScriptMessage>();
            var orchestrator = CreateOrchestrator();
            var context = new GenerateStreamContext
            {
                Prompt = "corrected",
                ModelId = "m",
                ReplaceLast = ReplaceLastModes.Exchange,
                ReplaceLastPrompt = "prompt that was never committed"
            };

            await orchestrator.RunSessionAsync(
                context,
                msg => { messages.Add(msg); return Task.CompletedTask; },
                CancellationToken.None).ConfigureAwait(false);

            AssertRefused(messages);

            var error = messages.Find(m => m.Type == WebView2MessageType.ChatSessionError);
            Assert.That(error.Payload, Does.Contain("out of sync"),
                "An out-of-sync prompt must say that nothing was changed");
        }

        [TestCase("Answer")]
        [TestCase("answr")]
        [TestCase("anything")]
        public async Task ReplaceLast_UnknownMode_LeavesTheHistoryAloneAndRunsTheRound(string mode)
        {
            var messages = new List<WebView2ScriptMessage>();
            var orchestrator = CreateOrchestrator();
            var context = new GenerateStreamContext
            {
                Prompt = "hello",
                ModelId = "m",
                ReplaceLast = mode,
                ReplaceLastPrompt = "hello"
            };

            await orchestrator.RunSessionAsync(
                context,
                msg => { messages.Add(msg); return Task.CompletedTask; },
                CancellationToken.None).ConfigureAwait(false);

            _historyMock.Verify(h => h.TruncateLastExchangeAsync(It.IsAny<bool>(), It.IsAny<string>()), Times.Never,
                "An unknown mode must never drop a prompt");
            _chatServiceMock.Verify(s => s.GenerateStreamAsync(
                It.IsAny<GenerateStreamContext>(),
                It.IsAny<List<ToolResultMessage>>(),
                It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                It.IsAny<Func<StreamCompletionResult, Task>>(),
                It.IsAny<Func<RateLimitWaitInfo, Task>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// The UI reads an error that arrives before ChatSessionStart as "the history was not changed" and draws the chat
        /// again from it, so a refusal must never announce a start.
        /// </summary>
        private static void AssertRefused(List<WebView2ScriptMessage> messages)
        {
            var error = messages.Find(m => m.Type == WebView2MessageType.ChatSessionError);
            Assert.That(error, Is.Not.Null, "A refusal must be reported");

            Assert.That(messages.Exists(m => m.Type == WebView2MessageType.ChatSessionStart), Is.False,
                "The session must not announce a start when the round never runs");
        }

        [Test]
        public async Task NormalPrompt_DoesNotTouchTheHistory()
        {
            var orchestrator = CreateOrchestrator();
            var context = new GenerateStreamContext { Prompt = "hello", ModelId = "m" };

            await orchestrator.RunSessionAsync(
                context,
                _ => Task.CompletedTask,
                CancellationToken.None).ConfigureAwait(false);

            _historyMock.Verify(h => h.TruncateLastExchangeAsync(It.IsAny<bool>(), It.IsAny<string>()), Times.Never);
        }
    }
}
