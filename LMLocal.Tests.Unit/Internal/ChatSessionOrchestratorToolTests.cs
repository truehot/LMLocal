using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.WebView;
using LMLocal.Services.Tool;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using Moq;
using NUnit.Framework;
using LMLocal.Core.Models;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Application.Tool;

namespace LMLocal.Tests.Unit.Internal
{
    [TestFixture]
    public class ChatSessionOrchestratorToolTests
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

        [Test]
        public async Task ExecuteTools_Path_SendsToolCallAndToolEnd_And_Iterates()
        {
            var messages = new List<WebView2ScriptMessage>();

            int callCount = 0;
            _chatServiceMock.Setup(s => s.GenerateStreamAsync(
                It.IsAny<GenerateStreamContext>(),
                It.IsAny<List<ToolResultMessage>>(),
                It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                It.IsAny<Func<StreamCompletionResult, Task>>(),
                It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, List<ToolResultMessage>, Func<TextStreamChunk, TokenGenerationStats, Task>, Func<StreamCompletionResult, Task>, CancellationToken>(
                    async (gctx, toolResults, onChunk, onComplete, ct) =>
                    {
                        callCount++;
                        if (callCount == 1)
                        {
                            // First generation produces one tool call
                            var result = new StreamCompletionResult
                            {
                                WasCancelled = false,
                                ErrorMessage = null,
                                FinishReason = "tool_calls",
                                ToolCalls = new[] { new ToolCallRecord { CallId = "call1", FunctionName = "tool1", ArgumentsJson = "{\"a\":1}" } }
                            };

                            if (onComplete != null)
                                await onComplete(result).ConfigureAwait(false);
                        }
                        else
                        {
                            // Second generation (after tool results) completes normally with no tool calls
                            var result = new StreamCompletionResult
                            {
                                WasCancelled = false,
                                ErrorMessage = null,
                                FinishReason = "stop",
                                ToolCalls = new ToolCallRecord[0]
                            };

                            if (onComplete != null)
                                await onComplete(result).ConfigureAwait(false);
                        }
                    });

            _toolManagerMock.Setup(t => t.GetProcessingMessage(It.IsAny<ToolCallRecord>())).Returns("processing");
            _toolManagerMock.Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ToolExecutionResult { Result = "ok", CompletionMessage = "done" });

            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(true);
            _compactorMock.Setup(c => c.CompactIfNeededAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var orchestrator = new ChatSessionOrchestrator(_chatServiceMock.Object, _toolManagerMock.Object, _compactorMock.Object, _snapshotManagerMock.Object, _loopDetectorMock.Object);

            Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                return Task.CompletedTask;
            }

            var context = new GenerateStreamContext { Prompt = "prompt", ModelId = "m" };

            await orchestrator.RunSessionAsync(context, OnMessage, CancellationToken.None).ConfigureAwait(false);

            // Check that tool call and tool end messages were sent
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.StreamToolCall && (m as WebView2ToolCallMessage)?.FunctionName == "tool1"), Is.True);
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.StreamToolEnd && (m as WebView2ToolCallMessage)?.FunctionName == "tool1"), Is.True);

            // Check that iteration markers were sent (one per round, final round with IsFinalRound=true)
            var iterMsgs = messages.OfType<WebView2ChatSessionIteratingMessage>().ToList();
            Assert.That(iterMsgs.Count, Is.EqualTo(2), "Should send 2 iteration messages: round1 (tools) + final round2 (no tools)");
            Assert.That(iterMsgs[0].IsFinalRound, Is.False, "First round should not be final");
            Assert.That(iterMsgs[1].IsFinalRound, Is.True, "Second (final) round should be marked final");

            // Final completion and compaction messages present
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.ChatSessionComplete), Is.True);
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.CompactionStart), Is.True);
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.CompactionEnd), Is.True);
        }

        [Test]
        public async Task ToolExecution_Error_IsReported_InStreamToolEnd()
        {
            var messages = new List<WebView2ScriptMessage>();

            int callCount = 0;
            _chatServiceMock.Setup(s => s.GenerateStreamAsync(
                It.IsAny<GenerateStreamContext>(),
                It.IsAny<List<ToolResultMessage>>(),
                It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                It.IsAny<Func<StreamCompletionResult, Task>>(),
                It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, List<ToolResultMessage>, Func<TextStreamChunk, TokenGenerationStats, Task>, Func<StreamCompletionResult, Task>, CancellationToken>(
                    async (gctx, toolResults, onChunk, onComplete, ct) =>
                    {
                        callCount++;
                        if (callCount == 1)
                        {
                            // First generation produces tool call
                            var result = new StreamCompletionResult
                            {
                                WasCancelled = false,
                                ErrorMessage = null,
                                FinishReason = "tool_calls",
                                ToolCalls = new[] { new ToolCallRecord { CallId = "call2", FunctionName = "toolErr", ArgumentsJson = "{}" } }
                            };

                            if (onComplete != null)
                                await onComplete(result).ConfigureAwait(false);
                        }
                        else
                        {
                            // Second generation should finish normally
                            var result = new StreamCompletionResult
                            {
                                WasCancelled = false,
                                ErrorMessage = null,
                                FinishReason = "stop",
                                ToolCalls = new ToolCallRecord[0]
                            };

                            if (onComplete != null)
                                await onComplete(result).ConfigureAwait(false);
                        }
                    });
            // When tool executed, return error
            _toolManagerMock.Setup(t => t.GetProcessingMessage(It.IsAny<ToolCallRecord>())).Returns("processing");
            _toolManagerMock.Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ToolExecutionResult { Error = "failed", UserMessage = "failed" });

            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(false);

            var orchestrator = new ChatSessionOrchestrator(_chatServiceMock.Object, _toolManagerMock.Object, _compactorMock.Object, _snapshotManagerMock.Object, _loopDetectorMock.Object);

            Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                return Task.CompletedTask;
            }

            var context = new GenerateStreamContext { Prompt = "prompt", ModelId = "m" };

            await orchestrator.RunSessionAsync(context, OnMessage, CancellationToken.None).ConfigureAwait(false);

            var callMsg = messages.FirstOrDefault(m => m.Type == WebView2MessageType.StreamToolCall) as WebView2ToolCallMessage;
            var endMsg = messages.FirstOrDefault(m => m.Type == WebView2MessageType.StreamToolEnd) as WebView2ToolCallMessage;
            Assert.That(callMsg, Is.Not.Null, $"StreamToolCall should exist. Messages: {messages.Count}, types: {string.Join(", ", messages.Take(10).Select(m => m.Type))}");
            Assert.That(endMsg, Is.Not.Null, $"StreamToolEnd should exist. Messages: {messages.Count}, types: {string.Join(", ", messages.Take(10).Select(m => m.Type))}");
            Assert.That(endMsg.IsError, Is.True);
            Assert.That(endMsg.Message, Is.EqualTo("failed"));
        }

        // ================================================================
        // Tool call loop detection integration tests
        // ================================================================

        /// <summary>
        /// When the model calls the exact same tool(s) with the exact same arguments
        /// for 3 consecutive rounds, the orchestrator should transition to Error state
        /// and send ChatSessionError.
        /// </summary>
        [Test]
        public async Task ThreeDuplicateToolRounds_EndsWithError()
        {
            var messages = new List<WebView2ScriptMessage>();

            _chatServiceMock.Setup(s => s.GenerateStreamAsync(
                    It.IsAny<GenerateStreamContext>(),
                    It.IsAny<List<ToolResultMessage>>(),
                    It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                    It.IsAny<Func<StreamCompletionResult, Task>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, List<ToolResultMessage>,
                    Func<TextStreamChunk, TokenGenerationStats, Task>,
                    Func<StreamCompletionResult, Task>, CancellationToken>(
                    async (gctx, toolResults, onChunk, onComplete, ct) =>
                    {
                        // Each generation returns the exact same tool call
                        var result = new StreamCompletionResult
                        {
                            WasCancelled = false,
                            ErrorMessage = null,
                            FinishReason = "tool_calls",
                            ToolCalls = new[]
                            {
                                new ToolCallRecord
                                {
                                    CallId = "call_loop",
                                    FunctionName = "read_file_lines",
                                    ArgumentsJson = "{\"file_path\":\"a.cs\",\"start_line\":1,\"end_line\":50}"
                                }
                            }
                        };
                        if (onComplete != null)
                            await onComplete(result).ConfigureAwait(false);
                    });

            _toolManagerMock.Setup(t => t.GetProcessingMessage(It.IsAny<ToolCallRecord>()))
                .Returns("processing");
            _toolManagerMock.Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ToolExecutionResult { Result = "ok", CompletionMessage = "done" });
            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(false);

            // All tool call comparisons are "same" — simulate identical calls
            _loopDetectorMock.Setup(d => d.AreSameToolCalls(
                    It.IsAny<IReadOnlyList<ToolCallRecord>>(),
                    It.IsAny<IReadOnlyList<ToolCallRecord>>()))
                .Returns(true);

            var orchestrator = new ChatSessionOrchestrator(
                _chatServiceMock.Object, _toolManagerMock.Object,
                _compactorMock.Object, _snapshotManagerMock.Object,
                _loopDetectorMock.Object);

            Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                return Task.CompletedTask;
            }

            var context = new GenerateStreamContext { Prompt = "test prompt", ModelId = "m" };

            await orchestrator.RunSessionAsync(context, OnMessage, CancellationToken.None)
                .ConfigureAwait(false);

            // After 3 duplicate rounds the orchestrator should enter Error state
            var errorMessages = messages.Where(m => m.Type == WebView2MessageType.ChatSessionError).ToList();
            Assert.That(errorMessages.Count, Is.EqualTo(1),
                "Expected exactly one ChatSessionError after 3 consecutive duplicate tool rounds");
            var errorPayload = errorMessages[0].Payload as string;
            Assert.That(errorPayload, Does.Contain("identical arguments"),
                "Error message should mention 'identical arguments'");
        }

        /// <summary>
        /// When the model changes tool calls between rounds (detector returns false),
        /// the duplicate counter resets and the session completes normally.
        /// </summary>
        [Test]
        public async Task ChangingToolCalls_NoLoopDetection_CompletesNormally()
        {
            var messages = new List<WebView2ScriptMessage>();
            int callCount = 0;

            _chatServiceMock.Setup(s => s.GenerateStreamAsync(
                    It.IsAny<GenerateStreamContext>(),
                    It.IsAny<List<ToolResultMessage>>(),
                    It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                    It.IsAny<Func<StreamCompletionResult, Task>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, List<ToolResultMessage>,
                    Func<TextStreamChunk, TokenGenerationStats, Task>,
                    Func<StreamCompletionResult, Task>, CancellationToken>(
                    async (gctx, toolResults, onChunk, onComplete, ct) =>
                    {
                        callCount++;
                        if (callCount <= 3)
                        {
                            // First 3 generations produce tool calls (different each time)
                            var result = new StreamCompletionResult
                            {
                                WasCancelled = false,
                                ErrorMessage = null,
                                FinishReason = "tool_calls",
                                ToolCalls = new[]
                                {
                                    new ToolCallRecord
                                    {
                                        CallId = $"call_{callCount}",
                                        FunctionName = "search_file_content",
                                        ArgumentsJson = $"{{\"text\":\"query{callCount}\"}}"
                                    }
                                }
                            };
                            if (onComplete != null)
                                await onComplete(result).ConfigureAwait(false);
                        }
                        else
                        {
                            // 4th generation finishes without tool calls
                            var result = new StreamCompletionResult
                            {
                                WasCancelled = false,
                                ErrorMessage = null,
                                FinishReason = "stop",
                                ToolCalls = Array.Empty<ToolCallRecord>()
                            };
                            if (onComplete != null)
                                await onComplete(result).ConfigureAwait(false);
                        }
                    });

            _toolManagerMock.Setup(t => t.GetProcessingMessage(It.IsAny<ToolCallRecord>()))
                .Returns("processing");
            _toolManagerMock.Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ToolExecutionResult { Result = "ok", CompletionMessage = "done" });
            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(false);

            // All comparisons return false — tools are "different" each round
            _loopDetectorMock.Setup(d => d.AreSameToolCalls(
                    It.IsAny<IReadOnlyList<ToolCallRecord>>(),
                    It.IsAny<IReadOnlyList<ToolCallRecord>>()))
                .Returns(false);

            var orchestrator = new ChatSessionOrchestrator(
                _chatServiceMock.Object, _toolManagerMock.Object,
                _compactorMock.Object, _snapshotManagerMock.Object,
                _loopDetectorMock.Object);

            Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                return Task.CompletedTask;
            }

            var context = new GenerateStreamContext { Prompt = "test prompt", ModelId = "m" };

            await orchestrator.RunSessionAsync(context, OnMessage, CancellationToken.None)
                .ConfigureAwait(false);

            // Session should complete normally — no error
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.ChatSessionComplete), Is.True,
                "Expected ChatSessionComplete when tool calls change between rounds");
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.ChatSessionError), Is.False,
                "No ChatSessionError expected when tool calls vary between rounds");
        }

        /// <summary>
        /// After two duplicate rounds, a different tool call resets the counter.
        /// Even if duplicates reappear later, counting starts from zero again.
        /// </summary>
        [Test]
        public async Task TwoDuplicatesThenDifferentTool_ResetsCounter_CompletesNormally()
        {
            var messages = new List<WebView2ScriptMessage>();
            int callCount = 0;
            bool detectorReturnsTrue = true;

            _chatServiceMock.Setup(s => s.GenerateStreamAsync(
                    It.IsAny<GenerateStreamContext>(),
                    It.IsAny<List<ToolResultMessage>>(),
                    It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                    It.IsAny<Func<StreamCompletionResult, Task>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, List<ToolResultMessage>,
                    Func<TextStreamChunk, TokenGenerationStats, Task>,
                    Func<StreamCompletionResult, Task>, CancellationToken>(
                    async (gctx, toolResults, onChunk, onComplete, ct) =>
                    {
                        callCount++;
                        if (callCount <= 5)
                        {
                            // After 3 calls, switch from duplicate to different
                            if (callCount == 4)
                                detectorReturnsTrue = false;

                            var result = new StreamCompletionResult
                            {
                                WasCancelled = false,
                                ErrorMessage = null,
                                FinishReason = "tool_calls",
                                ToolCalls = new[]
                                {
                                    new ToolCallRecord
                                    {
                                        CallId = $"call_{callCount}",
                                        FunctionName = "read_file_lines",
                                        ArgumentsJson = callCount <= 3
                                            ? "{\"file\":\"same.txt\"}"
                                            : "{\"file\":\"different.txt\"}"
                                    }
                                }
                            };
                            if (onComplete != null)
                                await onComplete(result).ConfigureAwait(false);
                        }
                        else
                        {
                            // 6th generation finishes without tools
                            var result = new StreamCompletionResult
                            {
                                WasCancelled = false,
                                ErrorMessage = null,
                                FinishReason = "stop",
                                ToolCalls = Array.Empty<ToolCallRecord>()
                            };
                            if (onComplete != null)
                                await onComplete(result).ConfigureAwait(false);
                        }
                    });

            _toolManagerMock.Setup(t => t.GetProcessingMessage(It.IsAny<ToolCallRecord>()))
                .Returns("processing");
            _toolManagerMock.Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ToolExecutionResult { Result = "ok", CompletionMessage = "done" });
            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(false);

            // Returns true (duplicate) for first 3 comparisons, then false (different)
            _loopDetectorMock.Setup(d => d.AreSameToolCalls(
                    It.IsAny<IReadOnlyList<ToolCallRecord>>(),
                    It.IsAny<IReadOnlyList<ToolCallRecord>>()))
                .Returns(() => detectorReturnsTrue);

            var orchestrator = new ChatSessionOrchestrator(
                _chatServiceMock.Object, _toolManagerMock.Object,
                _compactorMock.Object, _snapshotManagerMock.Object,
                _loopDetectorMock.Object);

            Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                return Task.CompletedTask;
            }

            var context = new GenerateStreamContext { Prompt = "test prompt", ModelId = "m" };

            await orchestrator.RunSessionAsync(context, OnMessage, CancellationToken.None)
                .ConfigureAwait(false);

            // Session completes normally — counter was reset before reaching threshold
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.ChatSessionComplete), Is.True,
                "Expected ChatSessionComplete when counter resets before threshold");
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.ChatSessionError), Is.False,
                "No ChatSessionError expected after counter reset");
        }
    }
}
