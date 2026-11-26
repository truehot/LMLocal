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
        private Mock<ISubAgentsParallelPolicy> _parallelPolicyMock;
        private Mock<ISubAgentsParallelRunner> _parallelRunnerMock;

        [SetUp]
        public void SetUp()
        {
            _chatServiceMock = new Mock<IChatStreamService>();
            _toolManagerMock = new Mock<IToolExecutionManager>();
            _compactorMock = new Mock<IHistoryCompactor>();
            _snapshotManagerMock = new Mock<ISnapshotManager>();
            _parallelPolicyMock = new Mock<ISubAgentsParallelPolicy>();
            _parallelRunnerMock = new Mock<ISubAgentsParallelRunner>();
        }

        private ChatSessionOrchestrator CreateOrchestrator()
        {
            return new OrchestratorTestBuilder
            {
                ChatService = _chatServiceMock,
                ToolManager = _toolManagerMock,
                Compactor = _compactorMock,
                SnapshotManager = _snapshotManagerMock,
                ParallelPolicy = _parallelPolicyMock,
                ParallelRunner = _parallelRunnerMock.Object
            }.Build();
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
                It.IsAny<Func<RateLimitWaitInfo, Task>>(),
                It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, List<ToolResultMessage>, Func<TextStreamChunk, TokenGenerationStats, Task>, Func<StreamCompletionResult, Task>, Func<RateLimitWaitInfo, Task>, CancellationToken>(
                    async (gctx, toolResults, onChunk, onComplete, onRateLimitWait, ct) =>
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
            _toolManagerMock.Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<ToolActivityEvent>>(), It.IsAny<ToolExecutionBudget>(), It.IsAny<LMLocal.Application.Usage.UsageCallContext>())).ReturnsAsync(new ToolExecutionResult { Result = "ok", CompletionMessage = "done" });

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
                It.IsAny<Func<RateLimitWaitInfo, Task>>(),
                It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, List<ToolResultMessage>, Func<TextStreamChunk, TokenGenerationStats, Task>, Func<StreamCompletionResult, Task>, Func<RateLimitWaitInfo, Task>, CancellationToken>(
                    async (gctx, toolResults, onChunk, onComplete, onRateLimitWait, ct) =>
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
            _toolManagerMock.Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<ToolActivityEvent>>(), It.IsAny<ToolExecutionBudget>(), It.IsAny<LMLocal.Application.Usage.UsageCallContext>())).ReturnsAsync(new ToolExecutionResult { Error = "failed", UserMessage = "failed" });

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
            Assert.That(endMsg.UiData, Is.Null, "a tool without UI data leaves the property empty");
            Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(endMsg), Does.Not.Contain("UiData"),
                "the property is omitted, so the other tools' messages do not change");
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
                    It.IsAny<Func<RateLimitWaitInfo, Task>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, List<ToolResultMessage>,
                    Func<TextStreamChunk, TokenGenerationStats, Task>,
                    Func<StreamCompletionResult, Task>, Func<RateLimitWaitInfo, Task>, CancellationToken>(
                    async (gctx, toolResults, onChunk, onComplete, onRateLimitWait, ct) =>
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
                    It.IsAny<CancellationToken>(), It.IsAny<IProgress<ToolActivityEvent>>(), It.IsAny<ToolExecutionBudget>(), It.IsAny<LMLocal.Application.Usage.UsageCallContext>()))
                .ReturnsAsync(new ToolExecutionResult { Result = "ok", CompletionMessage = "done" });
            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(false);

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
                    It.IsAny<Func<RateLimitWaitInfo, Task>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, List<ToolResultMessage>,
                    Func<TextStreamChunk, TokenGenerationStats, Task>,
                    Func<StreamCompletionResult, Task>, Func<RateLimitWaitInfo, Task>, CancellationToken>(
                    async (gctx, toolResults, onChunk, onComplete, onRateLimitWait, ct) =>
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
                    It.IsAny<CancellationToken>(), It.IsAny<IProgress<ToolActivityEvent>>(), It.IsAny<ToolExecutionBudget>(), It.IsAny<LMLocal.Application.Usage.UsageCallContext>()))
                .ReturnsAsync(new ToolExecutionResult { Result = "ok", CompletionMessage = "done" });
            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(false);

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
                    It.IsAny<Func<RateLimitWaitInfo, Task>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, List<ToolResultMessage>,
                    Func<TextStreamChunk, TokenGenerationStats, Task>,
                    Func<StreamCompletionResult, Task>, Func<RateLimitWaitInfo, Task>, CancellationToken>(
                    async (gctx, toolResults, onChunk, onComplete, onRateLimitWait, ct) =>
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
                    It.IsAny<CancellationToken>(), It.IsAny<IProgress<ToolActivityEvent>>(), It.IsAny<ToolExecutionBudget>(), It.IsAny<LMLocal.Application.Usage.UsageCallContext>()))
                .ReturnsAsync(new ToolExecutionResult { Result = "ok", CompletionMessage = "done" });
            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(false);

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

        /// <summary>
        /// Stop pressed while a tool is running cancels the session token. The snapshot batch must
        /// still be closed, otherwise _isBatching stays true in memory and every later Discard/Accept
        /// in the Changes panel throws "Cannot perform ... while a batch is active" until VS restart.
        /// </summary>
        [Test]
        public async Task StopDuringToolCall_ClosesSnapshotBatch_WithNonCancelledToken()
        {
            var cts = new CancellationTokenSource();
            var messages = new List<WebView2ScriptMessage>();

            _chatServiceMock.Setup(s => s.GenerateStreamAsync(
                It.IsAny<GenerateStreamContext>(),
                It.IsAny<List<ToolResultMessage>>(),
                It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                It.IsAny<Func<StreamCompletionResult, Task>>(),
                It.IsAny<Func<RateLimitWaitInfo, Task>>(),
                It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, List<ToolResultMessage>, Func<TextStreamChunk, TokenGenerationStats, Task>, Func<StreamCompletionResult, Task>, Func<RateLimitWaitInfo, Task>, CancellationToken>(
                    async (gctx, toolResults, onChunk, onComplete, onRateLimitWait, ct) =>
                    {
                        var result = new StreamCompletionResult
                        {
                            StopReason = StreamStopReason.Completed,
                            ErrorMessage = null,
                            FinishReason = "tool_calls",
                            ToolCalls = new[]
                            {
                                new ToolCallRecord
                                {
                                    CallId = "call1",
                                    FunctionName = "write_file",
                                    ArgumentsJson = "{\"file\":\"a.txt\"}"
                                }
                            }
                        };

                        if (onComplete != null)
                            await onComplete(result).ConfigureAwait(false);
                    });

            _toolManagerMock.Setup(t => t.GetProcessingMessage(It.IsAny<ToolCallRecord>())).Returns("processing");

            // Reproduces what Stop does from the UI thread: the token is cancelled while the tool
            // runs and the tool surfaces OperationCanceledException.
            _toolManagerMock.Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(),
                    It.IsAny<IProgress<ToolActivityEvent>>(), It.IsAny<ToolExecutionBudget>(), It.IsAny<LMLocal.Application.Usage.UsageCallContext>()))
                .Returns((ToolCallRecord call, CancellationToken toolCt, IProgress<ToolActivityEvent> progress,
                        ToolExecutionBudget budget, LMLocal.Application.Usage.UsageCallContext usage) =>
                    {
                        cts.Cancel();
                        return Task.FromException<ToolExecutionResult>(new OperationCanceledException(toolCt));
                    });

            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(false);

            var orchestrator = CreateOrchestrator();

            Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                return Task.CompletedTask;
            }

            var context = new GenerateStreamContext { Prompt = "prompt", ModelId = "m" };

            await orchestrator.RunSessionAsync(context, OnMessage, cts.Token).ConfigureAwait(false);

            Assert.That(cts.IsCancellationRequested, Is.True, "the test must reproduce a real Stop");

            _snapshotManagerMock.Verify(
                m => m.BeginBatchAsync(It.IsAny<CancellationToken>()), Times.Once,
                "the tool round must open exactly one snapshot batch");

            // The important part: cleanup runs with a token that is NOT cancelled, even though the
            // session token already is at this point.
            _snapshotManagerMock.Verify(
                m => m.EndBatchAsync(It.Is<CancellationToken>(t => !t.IsCancellationRequested)), Times.Once,
                "the snapshot batch must be closed with a non-cancelled token after Stop");

            _snapshotManagerMock.Verify(
                m => m.CancelBatchAsync(It.IsAny<CancellationToken>()), Times.Never,
                "cancelling the batch would drop the event/manifest update the Changes panel relies on");

            Assert.That(messages.Any(m => m.Type == WebView2MessageType.ChatSessionCancelled), Is.True,
                "the session should end in the Cancelled state");
        }

        /// <summary>
        /// StreamToolEnd carries the tool's UI data (the todo_write list) to the WebView.
        /// </summary>
        [Test]
        public async Task StreamToolEnd_CarriesUiData_FromTheToolResponse()
        {
            var messages = new List<WebView2ScriptMessage>();

            int callCount = 0;
            _chatServiceMock.Setup(s => s.GenerateStreamAsync(
                It.IsAny<GenerateStreamContext>(),
                It.IsAny<List<ToolResultMessage>>(),
                It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                It.IsAny<Func<StreamCompletionResult, Task>>(),
                It.IsAny<Func<RateLimitWaitInfo, Task>>(),
                It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, List<ToolResultMessage>, Func<TextStreamChunk, TokenGenerationStats, Task>, Func<StreamCompletionResult, Task>, Func<RateLimitWaitInfo, Task>, CancellationToken>(
                    async (gctx, toolResults, onChunk, onComplete, onRateLimitWait, ct) =>
                    {
                        callCount++;

                        var result = callCount == 1
                            ? new StreamCompletionResult
                            {
                                StopReason = StreamStopReason.Completed,
                                ErrorMessage = null,
                                FinishReason = "tool_calls",
                                ToolCalls = new[] { new ToolCallRecord { CallId = "call_todo", FunctionName = "todo_write", ArgumentsJson = "{}" } }
                            }
                            : new StreamCompletionResult
                            {
                                StopReason = StreamStopReason.Completed,
                                ErrorMessage = null,
                                FinishReason = "stop",
                                ToolCalls = new ToolCallRecord[0]
                            };

                        if (onComplete != null)
                            await onComplete(result).ConfigureAwait(false);
                    });

            var uiData = new[] { new { text = "Read the file", status = "done" } };

            _toolManagerMock.Setup(t => t.GetProcessingMessage(It.IsAny<ToolCallRecord>())).Returns("Updating task list...");
            _toolManagerMock.Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<ToolActivityEvent>>(), It.IsAny<ToolExecutionBudget>(), It.IsAny<LMLocal.Application.Usage.UsageCallContext>()))
                .ReturnsAsync(new ToolExecutionResult { Result = "stored", CompletionMessage = "Tasks updated: 1/1 done.", UiData = uiData });

            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(false);

            var orchestrator = CreateOrchestrator();

            Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                return Task.CompletedTask;
            }

            var context = new GenerateStreamContext { Prompt = "prompt", ModelId = "m" };

            await orchestrator.RunSessionAsync(context, OnMessage, CancellationToken.None).ConfigureAwait(false);

            var endMsg = messages.FirstOrDefault(m => m.Type == WebView2MessageType.StreamToolEnd) as WebView2ToolCallMessage;

            Assert.That(endMsg, Is.Not.Null, "StreamToolEnd must be sent");
            Assert.That(endMsg.UiData, Is.SameAs(uiData));
            Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(endMsg), Does.Contain("UiData"));
        }
    }
}
