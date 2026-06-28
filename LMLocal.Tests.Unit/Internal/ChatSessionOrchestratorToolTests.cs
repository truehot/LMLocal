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

namespace LMLocal.Tests.Unit.Internal
{
    [TestFixture]
    public class ChatSessionOrchestratorToolTests
    {
        private Mock<IChatStreamService> _chatServiceMock;
        private Mock<IToolExecutionManager> _toolManagerMock;
        private Mock<IHistoryCompactor> _compactorMock;
        private Mock<ISnapshotManager> _snapshotManagerMock;

        [SetUp]
        public void SetUp()
        {
            _chatServiceMock = new Mock<IChatStreamService>();
            _toolManagerMock = new Mock<IToolExecutionManager>();
            _compactorMock = new Mock<IHistoryCompactor>();
            _snapshotManagerMock = new Mock<ISnapshotManager>();
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

            var orchestrator = new ChatSessionOrchestrator(_chatServiceMock.Object, _toolManagerMock.Object, _compactorMock.Object, _snapshotManagerMock.Object);

            async Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                await Task.CompletedTask;
            }

            var context = new GenerateStreamContext { Prompt = "prompt", ModelId = "m" };

            await orchestrator.RunSessionAsync(context, OnMessage, CancellationToken.None).ConfigureAwait(false);

            // Check that tool call and tool end messages were sent
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.StreamToolCall && (m as WebView2ToolCallMessage)?.FunctionName == "tool1"), Is.True);
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.StreamToolEnd && (m as WebView2ToolCallMessage)?.FunctionName == "tool1"), Is.True);

            // Check that iteration marker was sent
            Assert.That(messages.Any(m => m.Type == WebView2MessageType.ChatSessionIterating), Is.True);

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
            _toolManagerMock.Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ToolExecutionResult { Error = "failed", CompletionMessage = null });

            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(false);

            var orchestrator = new ChatSessionOrchestrator(_chatServiceMock.Object, _toolManagerMock.Object, _compactorMock.Object, _snapshotManagerMock.Object);

            async Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                await Task.CompletedTask;
            }

            var context = new GenerateStreamContext { Prompt = "prompt", ModelId = "m" };

            await orchestrator.RunSessionAsync(context, OnMessage, CancellationToken.None).ConfigureAwait(false);

            var callMsg = messages.FirstOrDefault(m => m.Type == WebView2MessageType.StreamToolCall) as WebView2ToolCallMessage;
            var endMsg = messages.FirstOrDefault(m => m.Type == WebView2MessageType.StreamToolEnd) as WebView2ToolCallMessage;
            Assert.That(callMsg, Is.Not.Null, $"StreamToolCall should exist. Messages: {messages.Count}, types: {string.Join(", ", messages.Take(10).Select(m => m.Type))}");
            Assert.That(endMsg, Is.Not.Null, $"StreamToolEnd should exist. Messages: {messages.Count}, types: {string.Join(", ", messages.Take(10).Select(m => m.Type))}");
            Assert.That(endMsg.IsError, Is.True);
            Assert.That(endMsg.Error, Is.EqualTo("failed"));
            Assert.That(endMsg.Error, Is.EqualTo("failed"));
        }
    }
}
