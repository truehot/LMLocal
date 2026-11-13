using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.SubAgents;
using LMLocal.Application.Tool;
using LMLocal.Core.Exceptions;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    /// <summary>
    /// Guards the user-facing error text surfaced by <see cref="SubAgentsService"/>:
    /// raw exception messages must be routed through <c>ExceptionFormatter</c>, so the
    /// generic .NET wrapper ("An error occurred while sending the request.") never leaks
    /// to the chat when the local LLM server is not running.
    /// </summary>
    [TestFixture]
    public class SubAgentsServiceTests
    {
        private const string GenericWrapper = "An error occurred while sending the request";

        private Mock<ISettingsManager> _settingsManagerMock;
        private Mock<IFileSystem> _fileSystemMock;
        private Mock<IStreamingRoundService> _roundServiceMock;
        private Mock<IToolExecutionManager> _toolExecutionManagerMock;
        private Mock<IToolQueueProvider> _queueProviderMock;
        private Mock<IToolCallLoopDetector> _loopDetectorMock;
        private Mock<IChatPersistenceFactory> _persistenceFactoryMock;
        private Mock<IChatPersistenceService> _persistenceMock;

        [SetUp]
        public void SetUp()
        {
            _settingsManagerMock = new Mock<ISettingsManager>();
            _settingsManagerMock.SetupGet(s => s.LocalAppDataFolder).Returns("LMLocalChat.Tests");
            _settingsManagerMock.SetupGet(s => s.SystemPrompt).Returns("system");

            _fileSystemMock = new Mock<IFileSystem>();
            _roundServiceMock = new Mock<IStreamingRoundService>();
            _toolExecutionManagerMock = new Mock<IToolExecutionManager>();
            _queueProviderMock = new Mock<IToolQueueProvider>();
            _loopDetectorMock = new Mock<IToolCallLoopDetector>();
            _persistenceFactoryMock = new Mock<IChatPersistenceFactory>();
            _persistenceMock = new Mock<IChatPersistenceService>();

            _queueProviderMock
                .Setup(q => q.GetSubAgentQueue(It.IsAny<SubAgentRunRequest>()))
                .Returns(ToolQueue.ForSubAgent("agent", new List<ToolDefinition>()));

            _persistenceMock
                .Setup(p => p.MarkNewSessionAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _persistenceFactoryMock
                .Setup(f => f.Create(It.IsAny<string>()))
                .Returns(_persistenceMock.Object);
        }

        /// <summary>
        /// Builds the service under test. <paramref name="loopRecoveryEnabled"/> defaults to the production
        /// configuration, so the suite covers what actually ships; tests that pin the hard-stop behaviour
        /// pass <c>false</c> explicitly.
        /// </summary>
        private SubAgentsService CreateService(bool loopRecoveryEnabled = SubAgentsService.DefaultLoopRecoveryEnabled)
        {
            return new SubAgentsService(
                _settingsManagerMock.Object,
                _fileSystemMock.Object,
                _roundServiceMock.Object,
                () => _toolExecutionManagerMock.Object,
                _queueProviderMock.Object,
                _loopDetectorMock.Object,
                _persistenceFactoryMock.Object,
                loopRecoveryEnabled);
        }

        private static SubAgentRunRequest BuildRequest(int? timeoutSeconds = null, int? maxRounds = null)
        {
            return new SubAgentRunRequest
            {
                AgentName = "agent",
                Prompt = "do it",
                Model = "m",
                TimeoutSeconds = timeoutSeconds,
                MaxRounds = maxRounds
            };
        }

        private void SetupRoundThrows(Exception exception)
        {
            _roundServiceMock
                .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);
        }

        [Test]
        public async Task ExecutePromptAsync_UnreachableProvider_ErrorExposesInnerCauseInsteadOfGenericWrapper()
        {
            var inner = new InvalidOperationException(
                "No connection could be made because the target machine actively refused it 127.0.0.1:1234");
            SetupRoundThrows(new HttpRequestException(GenericWrapper, inner));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.Contain("actively refused it"),
                "The inner cause must be surfaced to the user.");
            Assert.That(response.Error, Does.Not.Contain(GenericWrapper),
                "The generic .NET wrapper must not leak into the user-facing error.");
        }

        [Test]
        public async Task ExecutePromptAsync_ApiException_KeepsParsedProviderMessage()
        {
            SetupRoundThrows(new ApiException(
                new ApiErrorInfo { Message = "Model not found" },
                404));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Is.EqualTo("Provider API error: Model not found"));
        }

        [Test]
        public async Task ExecutePromptAsync_LoopRecoveryDisabled_LoopFailsWithoutRecoveries()
        {
            SetupRepeatedToolCalls("read_file_lines", "{\"path\":\"a.cs\"}");
            AllowTool("read_file_lines");

            _loopDetectorMock
                .Setup(d => d.AreSameToolCalls(
                    It.IsAny<IReadOnlyList<ToolCallRecord>>(),
                    It.IsAny<IReadOnlyList<ToolCallRecord>>()))
                .Returns(true);

            _toolExecutionManagerMock
                .Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<ToolQueue>()))
                .ReturnsAsync(new ToolExecutionResult
                {
                    ToolId = "call_1",
                    ToolName = "read_file_lines",
                    Result = "file text"
                });

            var response = await CreateService(loopRecoveryEnabled: false)
                .ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.Contain("repeated tool call loop"));
            Assert.That(response.Recoveries, Is.Null, "With loop recovery off the run fails on the first detected loop.");
        }

        [Test]
        public async Task ExecutePromptAsync_LoopRecoveryEnabled_ClosesTurnAndContinuesAsUserRound()
        {
            _settingsManagerMock.SetupGet(s => s.AssistantPlaceholder).Returns("INTERRUPTED");

            var call = new ToolCallRecord
            {
                CallId = "call_1",
                FunctionName = "read_file_lines",
                ArgumentsJson = "{\"path\":\"a.cs\"}"
            };

            var toolRound = new StreamRoundResult
            {
                Outcome = StreamRoundOutcome.ToolCalls,
                Stream = new StreamCompletionResult { ToolCalls = new[] { call } }
            };
            var completedRound = new StreamRoundResult
            {
                Outcome = StreamRoundOutcome.Completed,
                Stream = new StreamCompletionResult { ContentResponse = "done" }
            };

            var requests = new List<StreamRoundRequest>();

            // MaxDuplicateToolRounds = 2, so the loop is detected on the third identical round.
            var rounds = new Queue<StreamRoundResult>(new[] { toolRound, toolRound, toolRound, completedRound });

            _roundServiceMock
                .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                .Returns((StreamRoundRequest req, CancellationToken ct) =>
                {
                    requests.Add(req);
                    return Task.FromResult(rounds.Count > 0 ? rounds.Dequeue() : completedRound);
                });

            AllowTool("read_file_lines");
            _loopDetectorMock
                .Setup(d => d.AreSameToolCalls(
                    It.IsAny<IReadOnlyList<ToolCallRecord>>(),
                    It.IsAny<IReadOnlyList<ToolCallRecord>>()))
                .Returns(true);

            _toolExecutionManagerMock
                .Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<ToolQueue>()))
                .ReturnsAsync(new ToolExecutionResult { ToolId = "call_1", ToolName = "read_file_lines", Result = "file text" });

            var response = await CreateService(loopRecoveryEnabled: true)
                .ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.True, response.Error);
            Assert.That(response.Content, Is.EqualTo("done"));
            Assert.That(response.Recoveries, Has.Count.EqualTo(1));
            Assert.That(response.Recoveries[0], Does.Contain("repeated tool call loop"));

            Assert.That(requests, Has.Count.EqualTo(4));

            // Round 0 is the original user turn; rounds 1 and 2 repeat the same call.
            Assert.That(requests[0].IsToolRound, Is.False);
            Assert.That(requests[0].Prompt, Is.EqualTo("do it"));
            Assert.That(requests[1].IsToolRound, Is.True);
            Assert.That(requests[1].Prompt, Is.Null);
            Assert.That(requests[2].IsToolRound, Is.True);

            // Round 3 is the recovered user turn: it carries the nudge as its prompt, and the original prompt
            // is not sent twice.
            Assert.That(requests[3].IsToolRound, Is.False);
            Assert.That(requests[3].Prompt, Is.EqualTo(SubAgentRecoveryPolicy.LoopRecoveryNudge),
                "The nudge must be delivered as the round prompt, otherwise the model never sees it.");
            Assert.That(requests[3].ToolResults, Is.Null);

            var recoveredHistory = requests[3].History.GetHistoryCopy();
            Assert.That(
                recoveredHistory.Select(m => m.Role),
                Is.EqualTo(new[] { "assistant" }),
                "The tool turn is closed by an assistant message before the new user turn starts.");
            Assert.That(recoveredHistory[0].Content, Is.EqualTo("INTERRUPTED"));
        }

        [Test]
        public async Task ExecutePromptAsync_LoopRecoveryEnabled_ExhaustedAttemptsStillFail()
        {
            _settingsManagerMock.SetupGet(s => s.AssistantPlaceholder).Returns("INTERRUPTED");

            var call = new ToolCallRecord
            {
                CallId = "call_1",
                FunctionName = "read_file_lines",
                ArgumentsJson = "{\"path\":\"a.cs\"}"
            };
            var toolRound = new StreamRoundResult
            {
                Outcome = StreamRoundOutcome.ToolCalls,
                Stream = new StreamCompletionResult { ToolCalls = new[] { call } }
            };

            _roundServiceMock
                .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(toolRound);

            AllowTool("read_file_lines");
            _loopDetectorMock
                .Setup(d => d.AreSameToolCalls(
                    It.IsAny<IReadOnlyList<ToolCallRecord>>(),
                    It.IsAny<IReadOnlyList<ToolCallRecord>>()))
                .Returns(true);

            _toolExecutionManagerMock
                .Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<ToolQueue>()))
                .ReturnsAsync(new ToolExecutionResult { ToolId = "call_1", ToolName = "read_file_lines", Result = "file text" });

            var response = await CreateService(loopRecoveryEnabled: true)
                .ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.Contain("repeated tool call loop"),
                "Once the recovery budget is spent, the hard stop must remain the last line of defence.");
            Assert.That(response.Recoveries, Has.Count.EqualTo(2),
                "The run recovers at most LoopRecoveryMaxAttempts times.");
        }

        [Test]
        public async Task ExecutePromptAsync_RejectedToolCall_NeverExecuted()
        {
            SetupRepeatedToolCalls("write_file", "{\"path\":\"a.cs\"}");

            // The queue has no definitions, so every call is rejected before execution.
            // Recovery is pinned off: this test guards the rejection, not the recovery path.
            var response = await CreateService(loopRecoveryEnabled: false)
                .ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.Contain("maximum rounds"));

            _toolExecutionManagerMock.Verify(
                t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<ToolQueue>()),
                Times.Never,
                "A tool that is not in the SubAgent queue must never be executed.");
        }

        // =====================================================================
        // Recovery from a tool call aborted while streaming (malformed arguments)
        // =====================================================================

        /// <summary>
        /// A round whose stream was stopped on broken arguments: the call is invalid and sanitized to "{}".
        /// </summary>
        private static StreamRoundResult AbortedToolRound()
        {
            return new StreamRoundResult
            {
                Outcome = StreamRoundOutcome.ToolCalls,
                Stream = new StreamCompletionResult
                {
                    StopReason = StreamStopReason.ToolCallAborted,
                    ToolCalls = new[]
                    {
                        new ToolCallRecord { CallId = "call_1", FunctionName = "find_files", ArgumentsJson = "{}", IsInvalid = true }
                    }
                }
            };
        }

        private static StreamRoundResult CompletedRound(string content)
        {
            return new StreamRoundResult
            {
                Outcome = StreamRoundOutcome.Completed,
                Stream = new StreamCompletionResult { ContentResponse = content }
            };
        }

        /// <summary>
        /// Plays <paramref name="rounds"/> in order (then completes with "done") and records every round request.
        /// </summary>
        private List<StreamRoundRequest> SetupRounds(params StreamRoundResult[] rounds)
        {
            var requests = new List<StreamRoundRequest>();
            var queue = new Queue<StreamRoundResult>(rounds);

            _roundServiceMock
                .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                .Returns((StreamRoundRequest req, CancellationToken ct) =>
                {
                    requests.Add(req);
                    return Task.FromResult(queue.Count > 0 ? queue.Dequeue() : CompletedRound("done"));
                });

            return requests;
        }

        [Test]
        public async Task ExecutePromptAsync_MalformedToolCall_NotExecutedAndRetriedAsUserTurn()
        {
            _settingsManagerMock.SetupGet(s => s.AssistantPlaceholder).Returns("INTERRUPTED");
            AllowTool("find_files");

            var requests = SetupRounds(AbortedToolRound(), CompletedRound("found"));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.True, response.Error);
            Assert.That(response.Content, Is.EqualTo("found"));

            _toolExecutionManagerMock.Verify(
                t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<ToolQueue>()),
                Times.Never,
                "An aborted call must not run.");

            Assert.That(response.Recoveries, Has.Count.EqualTo(1));
            Assert.That(response.Recoveries[0], Does.Contain("malformed tool call"));

            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[1].IsToolRound, Is.False);
            Assert.That(requests[1].Prompt, Is.EqualTo(SubAgentRecoveryPolicy.MalformedToolCallNudge),
                "The nudge must be delivered as the round prompt, otherwise the model never sees it.");
            Assert.That(requests[1].ToolResults, Is.Null);

            var history = requests[1].History.GetHistoryCopy();
            Assert.That(history.Select(m => m.Role), Is.EqualTo(new[] { "assistant" }),
                "The aborted turn is closed by an assistant message, without the broken call.");
            Assert.That(history[0].Content, Is.EqualTo("INTERRUPTED"));
        }

        [Test]
        public async Task ExecutePromptAsync_MalformedToolCallTwice_ThenAnswer_Succeeds()
        {
            AllowTool("find_files");
            SetupRounds(AbortedToolRound(), AbortedToolRound(), CompletedRound("found"));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.True, response.Error);
            Assert.That(response.Recoveries, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task ExecutePromptAsync_MalformedToolCallExhaustedAttempts_FailsWithReason()
        {
            AllowTool("find_files");
            SetupRounds(AbortedToolRound(), AbortedToolRound(), AbortedToolRound(), CompletedRound("never"));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.Contain("malformed tool calls 3 times"));
            Assert.That(response.Recoveries, Has.Count.EqualTo(2),
                "The run recovers at most MalformedRecoveryMaxAttempts times.");
        }

        [Test]
        public async Task ExecutePromptAsync_LoopRecoveryDisabled_MalformedToolCallStillRecovered()
        {
            AllowTool("find_files");
            SetupRounds(AbortedToolRound(), CompletedRound("found"));

            var response = await CreateService(loopRecoveryEnabled: false)
                .ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.True, response.Error);
            Assert.That(response.Recoveries, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ExecutePromptAsync_MalformedToolCallOnLastRound_Fails()
        {
            // No round is left for the nudge, so recovering would only waste the last round.
            AllowTool("find_files");
            SetupRounds(AbortedToolRound());

            var response = await CreateService().ExecutePromptAsync(BuildRequest(maxRounds: 1), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.Contain("malformed tool calls"));
            Assert.That(response.Recoveries, Is.Null);
        }

        private void SetupRepeatedToolCalls(string toolName, string argumentsJson)
        {
            var call = new ToolCallRecord
            {
                CallId = "call_1",
                FunctionName = toolName,
                ArgumentsJson = argumentsJson
            };

            _roundServiceMock
                .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StreamRoundResult
                {
                    Outcome = StreamRoundOutcome.ToolCalls,
                    Stream = new StreamCompletionResult { ToolCalls = new[] { call } }
                });
        }

        private void AllowTool(params string[] toolNames)
        {
            var definitions = toolNames.Select(name => new ToolDefinition { Name = name }).ToList();

            _queueProviderMock
                .Setup(q => q.GetSubAgentQueue(It.IsAny<SubAgentRunRequest>()))
                .Returns(ToolQueue.ForSubAgent("agent", definitions));
        }

        // =====================================================================
        // Recovery from a final answer with unresolved ref markers
        // =====================================================================

        [Test]
        public async Task ExecutePromptAsync_UnresolvedMarker_AnswerSentBackOnce_WithNudge()
        {
            _settingsManagerMock.SetupGet(s => s.AssistantPlaceholder).Returns("INTERRUPTED");
            var requests = SetupRounds(CompletedRound("Body:\n@src\\Program.cs:210-247"), CompletedRound("Body was not read."));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.True, response.Error);
            Assert.That(response.Content, Is.EqualTo("Body was not read."));
            Assert.That(response.Rounds, Is.EqualTo(2));
            Assert.That(response.UnresolvedRefCount, Is.EqualTo(0));
            Assert.That(response.Recoveries, Is.EqualTo(new[] { "SubAgent recovered from unresolved ref markers (attempt 1 of 1)." }));

            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[1].IsToolRound, Is.False);
            Assert.That(requests[1].ToolResults, Is.Null);
            Assert.That(requests[1].Prompt, Does.StartWith("[AUTOMATED GUARD: UNRESOLVED REF MARKERS]\n"));
            Assert.That(requests[1].Prompt, Does.Contain("@src\\Program.cs:210-247"));
            Assert.That(requests[1].History.GetHistoryCopy(), Is.Empty,
                "a final answer is already a closed turn: no placeholder is added");
        }

        [Test]
        public async Task ExecutePromptAsync_UnresolvedMarkerAgain_AnswerReturnedAsIs()
        {
            SetupRounds(CompletedRound("@src\\Program.cs:1-5"), CompletedRound("Still: @src\\Program.cs:1-5"));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.True, response.Error);
            Assert.That(response.Content, Is.EqualTo("Still: @src\\Program.cs:1-5"));
            Assert.That(response.UnresolvedRefCount, Is.EqualTo(1));
            Assert.That(response.Recoveries, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ExecutePromptAsync_UnresolvedMarkerInLastRound_AnswerReturnedAsIs()
        {
            var requests = SetupRounds(CompletedRound("@src\\Program.cs:1-5"));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(maxRounds: 1), CancellationToken.None);

            Assert.That(response.Success, Is.True, response.Error);
            Assert.That(response.Content, Is.EqualTo("@src\\Program.cs:1-5"));
            Assert.That(response.Recoveries, Is.Null);
            Assert.That(requests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ExecutePromptAsync_PlainAnswer_IsNotSentBack()
        {
            var requests = SetupRounds(CompletedRound("Yes. 12 matches in src\\Program.cs."));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.True, response.Error);
            Assert.That(response.Content, Is.EqualTo("Yes. 12 matches in src\\Program.cs."));
            Assert.That(response.Recoveries, Is.Null);
            Assert.That(requests, Has.Count.EqualTo(1));
        }

        // =====================================================================
        // Timeout / cancellation reporting
        // =====================================================================

        [Test]
        public async Task ExecutePromptAsync_InternalTimeout_ReportsTimeoutWithSeconds()
        {
            _roundServiceMock
                .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                .Returns((StreamRoundRequest req, CancellationToken ct) => RoundThatNeverCompletes(ct));

            var response = await CreateService()
                .ExecutePromptAsync(BuildRequest(timeoutSeconds: 1, maxRounds: 50), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.StartWith("SubAgent timed out after 1s"),
                "The reason and the number must survive the 120-char UI truncation.");
            Assert.That(response.Error, Does.Contain("Split the task into smaller pieces"),
                "A timeout must tell the main model how to retry.");
            Assert.That(response.Error, Does.Not.Contain("cancelled"),
                "An internal timeout must not be reported as a cancellation.");
            Assert.That(response.Error.Length, Is.LessThanOrEqualTo(120), response.Error);
        }

        [Test]
        public async Task ExecutePromptAsync_ParentCancellation_ReportsStoppedByUser()
        {
            _roundServiceMock
                .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                .Returns((StreamRoundRequest req, CancellationToken ct) => RoundThatNeverCompletes(ct));

            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                var response = await CreateService()
                    .ExecutePromptAsync(BuildRequest(timeoutSeconds: 360), cts.Token);

                AssertReadsAsUserStop(response);
            }
        }

        [Test]
        public async Task ExecutePromptAsync_ParentCancellationMidRound_ReportsRoundAndNoTimeoutSeconds()
        {
            using (var cts = new CancellationTokenSource())
            {
                _roundServiceMock
                    .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                    .Returns((StreamRoundRequest req, CancellationToken ct) =>
                    {
                        cts.Cancel(); // user presses Stop while the round is streaming
                        return Task.FromResult(new StreamRoundResult
                        {
                            Outcome = StreamRoundOutcome.Cancelled,
                            Stream = new StreamCompletionResult { StopReason = StreamStopReason.Cancelled }
                        });
                    });

                var response = await CreateService()
                    .ExecutePromptAsync(BuildRequest(timeoutSeconds: 360, maxRounds: 20), cts.Token);

                AssertReadsAsUserStop(response);
                Assert.That(response.Error, Does.Contain("(round 1/20)"));
            }
        }

        [Test]
        public async Task ExecutePromptAsync_CancelledOutcomeWithoutStop_ReportsOutcomeAndRound()
        {
            _roundServiceMock
                .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StreamRoundResult
                {
                    Outcome = StreamRoundOutcome.Cancelled,
                    Stream = new StreamCompletionResult { StopReason = StreamStopReason.Cancelled }
                });

            var response = await CreateService()
                .ExecutePromptAsync(BuildRequest(timeoutSeconds: 360, maxRounds: 20), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.StartWith("SubAgent stream ended as cancelled without a stop request"));
            Assert.That(response.Error, Does.Contain("(round 1/20)"));
            Assert.That(response.Error.Length, Is.LessThanOrEqualTo(120), response.Error);
        }

        private static void AssertReadsAsUserStop(SubAgentsRunResponse response)
        {
            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.StartWith("SubAgent stopped: the request was stopped by the user"));
            Assert.That(response.Error, Does.Not.Contain("timed out"));
            Assert.That(response.Error, Does.Not.Contain("360"),
                "A user stop must not show the timeout seconds, or it reads like a timeout.");
            Assert.That(response.Error.Length, Is.LessThanOrEqualTo(120), response.Error);
            Assert.That(response.Error, Does.Not.Contain(".."));
        }

        private static Task<StreamRoundResult> RoundThatNeverCompletes(CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<StreamRoundResult>();
            ct.Register(() => tcs.TrySetCanceled(ct));
            return tcs.Task;
        }
    }
}
