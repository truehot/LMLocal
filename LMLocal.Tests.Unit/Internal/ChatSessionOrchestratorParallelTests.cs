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
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.WebView;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Internal
{
    /// <summary>
    /// Integration tests for the SubAgent fan-out path of <see cref="ChatSessionOrchestrator"/>:
    /// a round with several parallel-enabled SubAgent calls must run them concurrently, keep the
    /// tool results in the original call order, and never let one failing agent abort the group.
    /// The real <see cref="SubAgentsParallelRunner"/> is used; only the tool manager and policy are mocked.
    /// </summary>
    [TestFixture]
    public class ChatSessionOrchestratorParallelTests
    {
        private Mock<IChatStreamService> _chatServiceMock;
        private Mock<IToolExecutionManager> _toolManagerMock;
        private Mock<IHistoryCompactor> _compactorMock;
        private Mock<ISnapshotManager> _snapshotManagerMock;
        private Mock<IToolCallLoopDetector> _loopDetectorMock;
        private Mock<ISubAgentsParallelPolicy> _parallelPolicyMock;

        private List<ToolResultMessage> _secondRoundResults;

        [SetUp]
        public void SetUp()
        {
            _chatServiceMock = new Mock<IChatStreamService>();
            _toolManagerMock = new Mock<IToolExecutionManager>();
            _compactorMock = new Mock<IHistoryCompactor>();
            _snapshotManagerMock = new Mock<ISnapshotManager>();
            _loopDetectorMock = new Mock<IToolCallLoopDetector>();
            _parallelPolicyMock = new Mock<ISubAgentsParallelPolicy>();
            _secondRoundResults = null;

            _toolManagerMock.Setup(t => t.GetProcessingMessage(It.IsAny<ToolCallRecord>())).Returns("processing");
            _compactorMock.Setup(c => c.NeedsCompaction()).Returns(false);
        }

        private ChatSessionOrchestrator CreateOrchestrator()
        {
            return new ChatSessionOrchestrator(
                _chatServiceMock.Object,
                _toolManagerMock.Object,
                _compactorMock.Object,
                _snapshotManagerMock.Object,
                _loopDetectorMock.Object,
                _parallelPolicyMock.Object,
                new SubAgentsParallelRunner());
        }

        private void TrackMax(ref int max, int value)
        {
            int prev;
            do
            {
                prev = Volatile.Read(ref max);
                if (value <= prev)
                    return;
            }
            while (Interlocked.CompareExchange(ref max, value, prev) != prev);
        }

        /// <summary>
        /// First generation returns two parallel-eligible agent calls; the second completes the session.
        /// </summary>
        private void SetupTwoAgentRound()
        {
            _parallelPolicyMock.Setup(p => p.IsParallelEnabled(It.IsAny<string>())).Returns(true);

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
                            await onComplete(new StreamCompletionResult
                            {
                                FinishReason = "tool_calls",
                                ToolCalls = new[]
                                {
                                    new ToolCallRecord { CallId = "c1", FunctionName = "agent_a", ArgumentsJson = "{}" },
                                    new ToolCallRecord { CallId = "c2", FunctionName = "agent_b", ArgumentsJson = "{}" }
                                }
                            }).ConfigureAwait(false);
                        }
                        else
                        {
                            if (toolResults != null)
                                _secondRoundResults = toolResults.ToList();

                            await onComplete(new StreamCompletionResult
                            {
                                FinishReason = "stop",
                                ToolCalls = Array.Empty<ToolCallRecord>()
                            }).ConfigureAwait(false);
                        }
                    });
        }

        [Test]
        public async Task ParallelGroup_RunsConcurrently_AndPreservesResultOrder()
        {
            var messages = new List<WebView2ScriptMessage>();
            int active = 0;
            int max = 0;

            SetupTwoAgentRound();

            _toolManagerMock
                .Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<ToolActivityEvent>>()))
                .Returns<ToolCallRecord, CancellationToken, IProgress<ToolActivityEvent>>(async (call, ct, progress) =>
                {
                    var now = Interlocked.Increment(ref active);
                    TrackMax(ref max, now);
                    await Task.Delay(120, ct).ConfigureAwait(false);
                    Interlocked.Decrement(ref active);
                    return new ToolExecutionResult { ToolName = call.FunctionName, Result = call.FunctionName, CompletionMessage = "done" };
                });

            var orchestrator = CreateOrchestrator();

            Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                return Task.CompletedTask;
            }

            await orchestrator.RunSessionAsync(
                new GenerateStreamContext { Prompt = "p", ModelId = "m" },
                OnMessage,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(max, Is.GreaterThanOrEqualTo(2), "Both SubAgents should run concurrently.");

            Assert.That(_secondRoundResults, Is.Not.Null);
            Assert.That(_secondRoundResults.Select(r => r.ToolCallId).ToArray(), Is.EqualTo(new[] { "c1", "c2" }),
                "Tool results must be handed back to the model in the original call order.");

            var toolCalls = messages.OfType<WebView2ToolCallMessage>()
                .Where(m => m.Type == WebView2MessageType.StreamToolCall)
                .ToList();
            Assert.That(toolCalls.Count, Is.EqualTo(2));
            Assert.That(toolCalls.Select(m => m.CallId).Where(id => !string.IsNullOrEmpty(id)).Distinct().Count(), Is.EqualTo(2),
                "Each tool card must receive its own non-empty UI call id (provider ids may repeat).");

            var toolEnds = messages.OfType<WebView2ToolCallMessage>()
                .Where(m => m.Type == WebView2MessageType.StreamToolEnd)
                .ToList();
            Assert.That(toolEnds.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task ParallelGroup_OneAgentFails_GroupStillCompletes()
        {
            var messages = new List<WebView2ScriptMessage>();

            SetupTwoAgentRound();

            _toolManagerMock
                .Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<ToolActivityEvent>>()))
                .Returns<ToolCallRecord, CancellationToken, IProgress<ToolActivityEvent>>((call, ct, progress) =>
                {
                    if (call.FunctionName == "agent_b")
                        return Task.FromResult(new ToolExecutionResult { ToolName = call.FunctionName, Error = "boom", UserMessage = "boom" });

                    return Task.FromResult(new ToolExecutionResult { ToolName = call.FunctionName, Result = "ok", CompletionMessage = "done" });
                });

            var orchestrator = CreateOrchestrator();

            Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                return Task.CompletedTask;
            }

            await orchestrator.RunSessionAsync(
                new GenerateStreamContext { Prompt = "p", ModelId = "m" },
                OnMessage,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(_secondRoundResults, Is.Not.Null);
            Assert.That(_secondRoundResults.Count, Is.EqualTo(2));
            Assert.That(_secondRoundResults[0].Error, Is.Null.Or.Empty);
            Assert.That(_secondRoundResults[1].Error, Is.EqualTo("boom"));

            Assert.That(messages.Any(m => m.Type == WebView2MessageType.ChatSessionComplete), Is.True,
                "A single failing agent must not abort the session.");

            var endMsgs = messages.OfType<WebView2ToolCallMessage>()
                .Where(m => m.Type == WebView2MessageType.StreamToolEnd)
                .ToList();
            Assert.That(endMsgs.Count(m => m.IsError), Is.EqualTo(1));
        }

        [Test]
        public async Task ParallelGroup_EmitsStartProgressAndEnd_HeaderMessages()
        {
            var messages = new List<WebView2ScriptMessage>();

            SetupTwoAgentRound();

            _toolManagerMock
                .Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<ToolActivityEvent>>()))
                .Returns<ToolCallRecord, CancellationToken, IProgress<ToolActivityEvent>>((call, ct, progressSink) =>
                    Task.FromResult(new ToolExecutionResult { ToolName = call.FunctionName, Result = "ok", CompletionMessage = "done" }));

            var orchestrator = CreateOrchestrator();

            Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                return Task.CompletedTask;
            }

            await orchestrator.RunSessionAsync(
                new GenerateStreamContext { Prompt = "p", ModelId = "m" },
                OnMessage,
                CancellationToken.None).ConfigureAwait(false);

            var groups = messages.OfType<WebView2ToolGroupMessage>().ToList();

            var starts = groups.Where(g => g.Type == WebView2MessageType.StreamToolGroupStart).ToList();
            var progress = groups.Where(g => g.Type == WebView2MessageType.StreamToolGroupProgress).ToList();
            var ends = groups.Where(g => g.Type == WebView2MessageType.StreamToolGroupEnd).ToList();

            Assert.That(starts.Count, Is.EqualTo(1), "Exactly one group start for the fan-out round.");
            Assert.That(progress.Count, Is.EqualTo(2), "One progress message per finished agent.");
            Assert.That(ends.Count, Is.EqualTo(1), "Exactly one group end.");

            var groupId = starts[0].GroupId;
            Assert.That(string.IsNullOrEmpty(groupId), Is.False);
            Assert.That(starts[0].Total, Is.EqualTo(2));
            Assert.That(starts[0].Completed, Is.EqualTo(0));
            Assert.That(starts[0].CallIds, Is.EquivalentTo(new[] { "c1", "c2" }));

            Assert.That(ends[0].GroupId, Is.EqualTo(groupId));
            Assert.That(ends[0].Completed, Is.EqualTo(2));
            Assert.That(ends[0].CallIds, Is.EquivalentTo(new[] { "c1", "c2" }));

            Assert.That(groups.All(g => g.GroupId == groupId), Is.True, "All group messages share one group id.");
            Assert.That(groups.All(g => g.Total == 2), Is.True, "All group messages carry the same total.");

            Assert.That(progress.Select(p => p.Completed).OrderBy(c => c), Is.EqualTo(new[] { 1, 2 }),
                "Progress counts run 1..N regardless of completion order.");
        }

        /// <summary>
        /// Regression: a parallel group of identical agents whose provider tool-call ids are equal
        /// (or empty) must still produce one distinct UI call id per card, so the WebView renders all
        /// of them; the provider id is preserved for the tool result handed back to the model.
        /// </summary>
        [Test]
        public async Task ParallelGroup_IdenticalProviderCallIds_ProduceDistinctUiCallIds()
        {
            var messages = new List<WebView2ScriptMessage>();

            _parallelPolicyMock.Setup(p => p.IsParallelEnabled(It.IsAny<string>())).Returns(true);

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
                            await onComplete(new StreamCompletionResult
                            {
                                FinishReason = "tool_calls",
                                ToolCalls = new[]
                                {
                                    new ToolCallRecord { CallId = "dup", FunctionName = "code_explorer", ArgumentsJson = "{}" },
                                    new ToolCallRecord { CallId = "dup", FunctionName = "code_explorer", ArgumentsJson = "{}" }
                                }
                            }).ConfigureAwait(false);
                        }
                        else
                        {
                            if (toolResults != null)
                                _secondRoundResults = toolResults.ToList();

                            await onComplete(new StreamCompletionResult
                            {
                                FinishReason = "stop",
                                ToolCalls = Array.Empty<ToolCallRecord>()
                            }).ConfigureAwait(false);
                        }
                    });

            _toolManagerMock
                .Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<IProgress<ToolActivityEvent>>()))
                .Returns<ToolCallRecord, CancellationToken, IProgress<ToolActivityEvent>>((call, ct, progress) =>
                    Task.FromResult(new ToolExecutionResult { ToolName = call.FunctionName, Result = "ok", CompletionMessage = "done" }));

            var orchestrator = CreateOrchestrator();

            Task OnMessage(WebView2ScriptMessage msg)
            {
                messages.Add(msg);
                return Task.CompletedTask;
            }

            await orchestrator.RunSessionAsync(
                new GenerateStreamContext { Prompt = "p", ModelId = "m" },
                OnMessage,
                CancellationToken.None).ConfigureAwait(false);

            var callMsgs = messages.OfType<WebView2ToolCallMessage>()
                .Where(m => m.Type == WebView2MessageType.StreamToolCall)
                .ToList();
            var endMsgs = messages.OfType<WebView2ToolCallMessage>()
                .Where(m => m.Type == WebView2MessageType.StreamToolEnd)
                .ToList();

            Assert.That(callMsgs.Count, Is.EqualTo(2));
            Assert.That(callMsgs.All(m => !string.IsNullOrEmpty(m.CallId)), Is.True);
            Assert.That(callMsgs.Select(m => m.CallId).Distinct().Count(), Is.EqualTo(2),
                "Two identical provider CallIds must still map to two distinct UI call ids.");

            Assert.That(endMsgs.Count, Is.EqualTo(2));
            Assert.That(endMsgs.Select(m => m.CallId).OrderBy(c => c),
                Is.EquivalentTo(callMsgs.Select(m => m.CallId).OrderBy(c => c)),
                "StreamToolEnd must reuse the same UI call id as the matching StreamToolCall.");

            Assert.That(_secondRoundResults, Is.Not.Null);
            Assert.That(_secondRoundResults.Select(r => r.ToolCallId), Is.All.EqualTo("dup"),
                "Provider call ids must be preserved unchanged for model correlation.");
        }
    }
}


