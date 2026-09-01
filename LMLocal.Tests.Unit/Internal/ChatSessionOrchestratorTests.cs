using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.WebView;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Internal
{
    [TestFixture]
    public class ChatSessionOrchestratorTests
    {
        private Mock<IChatStreamService> _chatServiceMock;
        private Mock<IToolExecutionManager> _toolManagerMock;
        private Mock<IHistoryCompactor> _compactorMock;
        private Mock<ISnapshotManager> _snapshotManagerMock;
        private Mock<IToolCallLoopDetector> _loopDetectorMock;

        [SetUp]
        public void SetUp()
        {
            _chatServiceMock = new Mock<IChatStreamService>();
            _toolManagerMock = new Mock<IToolExecutionManager>();
            _compactorMock = new Mock<IHistoryCompactor>();
            _snapshotManagerMock = new Mock<ISnapshotManager>();
            _loopDetectorMock = new Mock<IToolCallLoopDetector>();
        }

        // Arrange-Act-Assert: successful generation path without tools -> sends complete and compaction messages
        [Test]
        public async Task Generate_FullSuccessfulPath_SendsCompleteAndCompaction()
        {
            var messages = new List<WebView2ScriptMessage>();

            // Setup chat service to call onChunk and onComplete
            // Setup chat service to call onChunk and onComplete
            _chatServiceMock.Setup(s => s.GenerateStreamAsync(
                It.IsAny<GenerateStreamContext>(),
                It.IsAny<List<ToolResultMessage>>(),
                It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                It.IsAny<Func<StreamCompletionResult, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns<GenerateStreamContext, List<ToolResultMessage>, Func<TextStreamChunk, TokenGenerationStats, Task>, Func<StreamCompletionResult, Task>, CancellationToken>(async (gctx, toolResults, onChunk, onComplete, ct) =>
            {
                // complete without sending chunks
                if (onComplete != null)
                {
                    var result = new StreamCompletionResult { WasCancelled = false, ErrorMessage = null, FinishReason = "stop" };
                    await onComplete(result).ConfigureAwait(false);
                }
            });

            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(true);
            _compactorMock.Setup(c => c.CompactIfNeededAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var orchestrator = new ChatSessionOrchestrator(_chatServiceMock.Object, _toolManagerMock.Object, _compactorMock.Object, _snapshotManagerMock.Object, _loopDetectorMock.Object);

            async Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                await Task.CompletedTask;
            }

            var context = new GenerateStreamContext { Prompt = "prompt", ModelId = "m" };

            await orchestrator.RunSessionAsync(context, OnMessage, CancellationToken.None).ConfigureAwait(false);

            // Verify message types sequence contains start, stream end, complete and compaction messages
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.ChatSessionStart), Is.True);
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.StreamEnd), Is.True);
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.ChatSessionIterating
                && ((WebView2ChatSessionIteratingMessage)m).IsFinalRound), Is.True,
                "No-tools path should send ChatSessionIterating with IsFinalRound=true");
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.ChatSessionComplete), Is.True);
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.CompactionStart), Is.True);
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.CompactionEnd), Is.True);
        }

        // Arrange-Act-Assert: StopSession should trigger cancellation and send ChatSessionCancelled once
        [Test]
        public async Task StopSession_TriggersCancellation_SendsCancelled()
        {
            var messages = new List<WebView2ScriptMessage>();
            var tcs = new TaskCompletionSource<bool>();

            _chatServiceMock.Setup(s => s.GenerateStreamAsync(
                It.IsAny<GenerateStreamContext>(),
                It.IsAny<List<ToolResultMessage>>(),
                It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                It.IsAny<Func<StreamCompletionResult, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns<GenerateStreamContext, List<ToolResultMessage>, Func<TextStreamChunk, TokenGenerationStats, Task>, Func<StreamCompletionResult, Task>, CancellationToken>(async (gctx, toolResults, onChunk, onComplete, ct) =>
            {
                // Wait until cancellation is requested
                try
                {
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // signal test that cancellation happened and rethrow so orchestrator observes cancellation
                    tcs.SetCanceled();
                    throw;
                }
            });

            var orchestrator = new ChatSessionOrchestrator(_chatServiceMock.Object, _toolManagerMock.Object, _compactorMock.Object, _snapshotManagerMock.Object, _loopDetectorMock.Object);

            async Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                await Task.CompletedTask;
            }

            var context = new GenerateStreamContext { Prompt = "prompt", ModelId = "m" };

            var runTask = orchestrator.RunSessionAsync(context, OnMessage, CancellationToken.None);

            // Give the orchestrator some time to start
            await Task.Delay(50).ConfigureAwait(false);

            // Call StopSession which should cancel
            orchestrator.StopSession();

            // Wait for orchestrator to finish
            await runTask.ConfigureAwait(false);

            // We expect ChatSessionCancelled to be sent once
            Assert.That(messages.Count(m => m.Type == WebView2MessageType.ChatSessionCancelled), Is.EqualTo(1));
        }

        // Error during generation should send ChatSessionError once
        [Test]
        public async Task GenerationError_SendsChatSessionError()
        {
            var messages = new List<WebView2ScriptMessage>();

            _chatServiceMock.Setup(s => s.GenerateStreamAsync(
                It.IsAny<GenerateStreamContext>(),
                It.IsAny<List<ToolResultMessage>>(),
                It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                It.IsAny<Func<StreamCompletionResult, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns<GenerateStreamContext, List<ToolResultMessage>, Func<TextStreamChunk, TokenGenerationStats, Task>, Func<StreamCompletionResult, Task>, CancellationToken>(async (gctx, toolResults, onChunk, onComplete, ct) =>
            {
                if (onComplete != null)
                {
                    var result = new StreamCompletionResult { WasCancelled = false, ErrorMessage = "LLM error" };
                    await onComplete(result).ConfigureAwait(false);
                }
            });

            var orchestrator = new ChatSessionOrchestrator(_chatServiceMock.Object, _toolManagerMock.Object, _compactorMock.Object, _snapshotManagerMock.Object, _loopDetectorMock.Object);

            async Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                await Task.CompletedTask;
            }

            var context = new GenerateStreamContext { Prompt = "prompt", ModelId = "m" };

            await orchestrator.RunSessionAsync(context, OnMessage, CancellationToken.None).ConfigureAwait(false);

            Assert.That(messages.Count(m => m.Type == WebView2MessageType.ChatSessionError), Is.EqualTo(1));
            var errorMsg = messages.First(m => m.Type == WebView2MessageType.ChatSessionError).Payload as string;
            Assert.That(errorMsg.Contains("LLM error"), Is.True);
        }

        // Token usage metadata (including cached tokens) should propagate into ChatSessionComplete
        [Test]
        public async Task CompleteMessage_PropagatesTokenUsage_WithCachedTokens()
        {
            var messages = new List<WebView2ScriptMessage>();

            _chatServiceMock.Setup(s => s.GenerateStreamAsync(
                It.IsAny<GenerateStreamContext>(),
                It.IsAny<List<ToolResultMessage>>(),
                It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                It.IsAny<Func<StreamCompletionResult, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns<GenerateStreamContext, List<ToolResultMessage>, Func<TextStreamChunk, TokenGenerationStats, Task>, Func<StreamCompletionResult, Task>, CancellationToken>(async (gctx, toolResults, onChunk, onComplete, ct) =>
            {
                if (onComplete != null)
                {
                    var result = new StreamCompletionResult
                    {
                        WasCancelled = false,
                        ErrorMessage = null,
                        FinishReason = "stop",
                        TokensPerSecond = 42.0,
                        TokenUsage = new TokenUsageMetadata
                        {
                            TotalTokens = 800,
                            PromptTokens = 500,
                            CompletionTokens = 300,
                            ReasoningTokens = 8,
                            CachedTokens = 100
                        }
                    };
                    await onComplete(result).ConfigureAwait(false);
                }
            });

            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(true);
            _compactorMock.Setup(c => c.CompactIfNeededAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var orchestrator = new ChatSessionOrchestrator(_chatServiceMock.Object, _toolManagerMock.Object, _compactorMock.Object, _snapshotManagerMock.Object, _loopDetectorMock.Object);

            async Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                await Task.CompletedTask;
            }

            var context = new GenerateStreamContext { Prompt = "prompt", ModelId = "m" };

            await orchestrator.RunSessionAsync(context, OnMessage, CancellationToken.None).ConfigureAwait(false);

            var completeMsg = messages.OfType<WebView2SessionCompleteMessage>().Single();
            Assert.That(completeMsg.TotalTokens, Is.EqualTo(800));
            Assert.That(completeMsg.PromptTokens, Is.EqualTo(500));
            Assert.That(completeMsg.CompletionTokens, Is.EqualTo(300));
            Assert.That(completeMsg.ReasoningTokens, Is.EqualTo(8));
            Assert.That(completeMsg.CachedTokens, Is.EqualTo(100));
            Assert.That(completeMsg.TokensPerSecond, Is.EqualTo(42.0));
        }
    }
}
