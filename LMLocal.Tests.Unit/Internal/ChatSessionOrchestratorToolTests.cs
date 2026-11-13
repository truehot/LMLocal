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
    public class ChatSessionOrchestratorToolTests
    {
        private Mock<IChatStreamService> _chatServiceMock;
        private Mock<IToolExecutionManager> _toolManagerMock;
        private Mock<IHistoryCompactor> _compactorMock;
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
            _snapshotManagerMock = new Mock<ISnapshotManager>();
            _loopDetectorMock = new Mock<IToolCallLoopDetector>();
            _parallelPolicyMock = new Mock<ISubAgentsParallelPolicy>();
            _parallelRunnerMock = new Mock<ISubAgentsParallelRunner>();
        }

        private ChatSessionOrchestrator CreateOrchestrator()
        {
            return new ChatSessionOrchestrator(
                _chatServiceMock.Object,
                _toolManagerMock.Object,
                _compactorMock.Object,
                new Mock<IChatHistoryManager>().Object,
                _snapshotManagerMock.Object,
                _loopDetectorMock.Object,
                _parallelPolicyMock.Object,
                _parallelRunnerMock.Object);
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
                                StopReason = StreamStopReason.Completed,
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
                                StopReason = StreamStopReason.Completed,
                                ErrorMessage = null,
                                FinishReason = "stop",
                                ToolCalls = new ToolCallRecord[0]
                            };

                            if (onComplete != null)
                                await onComplete(result).ConfigureAwait(false);
                        }
                    });

            _toolManagerMock.Setup(t => t.GetProcessingMessage(It.IsAny<ToolCallRecord>())).Returns("processing");
            _toolManagerMock.Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<ToolActivityEvent>>())).ReturnsAsync(new ToolExecutionResult { Result = "ok", CompletionMessage = "done" });

            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(true);
            _compactorMock.Setup(c => c.CompactIfNeededAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var orchestrator = CreateOrchestrator();

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
                                StopReason = StreamStopReason.Completed,
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
                                StopReason = StreamStopReason.Completed,
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
            _toolManagerMock.Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<ToolActivityEvent>>())).ReturnsAsync(new ToolExecutionResult { Error = "failed", UserMessage = "failed" });

            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(false);

            var orchestrator = CreateOrchestrator();

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
        /// for 2 consecutive rounds (duplicate on the 2nd attempt), the orchestrator
        /// should transition to Error state and send ChatSessionError.
        /// </summary>
        [Test]
        public async Task DuplicateToolRound_EndsWithError_OnSecondAttempt()
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
                            StopReason = StreamStopReason.Completed,
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
                    It.IsAny<CancellationToken>(), It.IsAny<IProgress<ToolActivityEvent>>()))
                .ReturnsAsync(new ToolExecutionResult { Result = "ok", CompletionMessage = "done" });
            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(false);

            // All tool call comparisons are "same" — simulate identical calls
            _loopDetectorMock.Setup(d => d.AreSameToolCalls(
                    It.IsAny<IReadOnlyList<ToolCallRecord>>(),
                    It.IsAny<IReadOnlyList<ToolCallRecord>>()))
                .Returns(true);

            var orchestrator = CreateOrchestrator();

            Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                return Task.CompletedTask;
            }

            var context = new GenerateStreamContext { Prompt = "test prompt", ModelId = "m" };

            await orchestrator.RunSessionAsync(context, OnMessage, CancellationToken.None)
                .ConfigureAwait(false);

            // After the 2nd identical attempt the orchestrator should enter Error state
            var errorMessages = messages.Where(m => m.Type == WebView2MessageType.ChatSessionError).ToList();
            Assert.That(errorMessages.Count, Is.EqualTo(1),
                "Expected exactly one ChatSessionError after a duplicate tool call on the 2nd attempt");
            var errorPayload = errorMessages[0].Payload as string;
            Assert.That(errorPayload, Does.Contain("identical arguments"),
                "Error message should mention 'identical arguments'");
            Assert.That(errorPayload, Does.Contain("2 identical rounds in a row: read_file_lines"),
                "The count must be the number of identical rounds, not the number of repeats.");
            Assert.That(errorPayload, Does.Not.Contain("1 times"));
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
                                StopReason = StreamStopReason.Completed,
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
                                StopReason = StreamStopReason.Completed,
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
                    It.IsAny<CancellationToken>(), It.IsAny<IProgress<ToolActivityEvent>>()))
                .ReturnsAsync(new ToolExecutionResult { Result = "ok", CompletionMessage = "done" });
            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(false);

            // All comparisons return false — tools are "different" each round
            _loopDetectorMock.Setup(d => d.AreSameToolCalls(
                    It.IsAny<IReadOnlyList<ToolCallRecord>>(),
                    It.IsAny<IReadOnlyList<ToolCallRecord>>()))
                .Returns(false);

            var orchestrator = CreateOrchestrator();

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
        /// With MAX_DUPLICATE_TOOL_ROUNDS = 1 a duplicate pair halts on the 2nd consecutive
        /// identical call. This test verifies the reset path: the same tool + arguments that
        /// WOULD be a loop when consecutive is interrupted by a different-argument call, so
        /// the counter resets each time and the session completes normally.
        /// </summary>
        [Test]
        public async Task SameToolRepeatedButNotConsecutively_CounterResets_CompletesNormally()
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
                            // 1: {"file":"a.txt"} -> 2: {"file":"b.txt"} -> 3: {"file":"a.txt"}
                            var result = new StreamCompletionResult
                            {
                                StopReason = StreamStopReason.Completed,
                                ErrorMessage = null,
                                FinishReason = "tool_calls",
                                ToolCalls = new[]
                                {
                                    new ToolCallRecord
                                    {
                                        CallId = $"call_{callCount}",
                                        FunctionName = "read_file_lines",
                                        ArgumentsJson = callCount == 2
                                            ? "{\"file\":\"b.txt\"}"
                                            : "{\"file\":\"a.txt\"}"
                                    }
                                }
                            };
                            if (onComplete != null)
                                await onComplete(result).ConfigureAwait(false);
                        }
                        else
                        {
                            // 4th generation finishes without tools
                            var result = new StreamCompletionResult
                            {
                                StopReason = StreamStopReason.Completed,
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
                    It.IsAny<CancellationToken>(), It.IsAny<IProgress<ToolActivityEvent>>()))
                .ReturnsAsync(new ToolExecutionResult { Result = "ok", CompletionMessage = "done" });
            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(false);

            // Mirror the real detector: two calls are "same" only when tool name AND arguments match.
            _loopDetectorMock.Setup(d => d.AreSameToolCalls(
                    It.IsAny<IReadOnlyList<ToolCallRecord>>(),
                    It.IsAny<IReadOnlyList<ToolCallRecord>>()))
                .Returns((IReadOnlyList<ToolCallRecord> current, IReadOnlyList<ToolCallRecord> previous) =>
                    current != null && previous != null &&
                    current.Count == previous.Count && current.Count > 0 &&
                    string.Equals(current[0].FunctionName, previous[0].FunctionName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(current[0].ArgumentsJson, previous[0].ArgumentsJson, StringComparison.Ordinal));

            var orchestrator = CreateOrchestrator();

            Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                return Task.CompletedTask;
            }

            var context = new GenerateStreamContext { Prompt = "test prompt", ModelId = "m" };

            await orchestrator.RunSessionAsync(context, OnMessage, CancellationToken.None)
                .ConfigureAwait(false);

            // "a.txt" appears twice but never consecutively -> the counter resets each round -> no loop.
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.ChatSessionComplete), Is.True,
                "Expected ChatSessionComplete when the identical call is not consecutive");
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.ChatSessionError), Is.False,
                "No ChatSessionError expected when calls are not consecutive duplicates");
        }
    }
}
