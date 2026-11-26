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
using LMLocal.Infrastructure.LlmApi.Provider;
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
                _persistenceFactoryMock.Object,
                new ProviderPolicy(),
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

        /// <summary>Records every progress event the service reports, so its token events can be asserted.</summary>
        private sealed class RecordingProgress : IProgress<ToolActivityEvent>
        {
            internal List<ToolActivityEvent> Events { get; } = new List<ToolActivityEvent>();

            public void Report(ToolActivityEvent value)
            {
                Events.Add(value);
            }
        }

        [Test]
        public async Task ExecutePromptAsync_ReportsTheTokenUsageOfTheRoundToTheProgressSink()
        {
            var recorder = new RecordingProgress();
            var request = BuildRequest(maxRounds: 1);
            request.Progress = recorder;

            _roundServiceMock
                .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                .Returns((StreamRoundRequest roundRequest, CancellationToken token) =>
                {
                    if (roundRequest.OnChunk != null)
                    {
                        roundRequest.OnChunk(
                                new TextStreamChunk("Hello", ChunkKind.Content),
                                new TokenGenerationStats(12, 4.5))
                            .GetAwaiter().GetResult();

                        roundRequest.OnChunk(
                                new TextStreamChunk(" world", ChunkKind.Content),
                                new TokenGenerationStats(20, 6.0))
                            .GetAwaiter().GetResult();
                    }

                    return Task.FromResult(new StreamRoundResult
                    {
                        Outcome = StreamRoundOutcome.Completed,
                        Stream = new StreamCompletionResult { ContentResponse = "Hello world" }
                    });
                });

            var response = await CreateService().ExecutePromptAsync(request, CancellationToken.None);

            Assert.That(response.Success, Is.True);
            Assert.That(recorder.Events, Is.Not.Empty, "the run must report its tokens, or the status bar stays frozen");
            Assert.That(recorder.Events[0].Tokens, Is.EqualTo(12), "the first live value of the round");
            Assert.That(recorder.Events.Count, Is.GreaterThanOrEqualTo(2), "the round is closed with a final event");
            Assert.That(recorder.Events[recorder.Events.Count - 1].Tokens, Is.EqualTo(20));
            Assert.That(recorder.Events[recorder.Events.Count - 1].TokensPerSecond, Is.EqualTo(0),
                "after the round no tokens flow, so a speed there would look frozen");
            Assert.That(recorder.Events.TrueForAll(e => e.Step == null), Is.True,
                "token events must not reach the step line of the card");
        }

        [Test]
        public async Task ExecutePromptAsync_RoundWithoutTextChunks_ReportsTheServerCompletionTokens()
        {
            var recorder = new RecordingProgress();
            var request = BuildRequest(maxRounds: 1);
            request.Progress = recorder;

            _roundServiceMock
                .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StreamRoundResult
                {
                    Outcome = StreamRoundOutcome.Completed,
                    Stream = new StreamCompletionResult
                    {
                        ContentResponse = "Done",
                        TokenUsage = new TokenUsageMetadata { CompletionTokens = 35 }
                    }
                });

            await CreateService().ExecutePromptAsync(request, CancellationToken.None);

            Assert.That(recorder.Events, Is.Not.Empty);
            Assert.That(recorder.Events[recorder.Events.Count - 1].Tokens, Is.EqualTo(35),
                "Tool-call arguments produce no text chunk; the round's tokens come from the server's count");
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

            _toolExecutionManagerMock
                .Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<ToolQueue>(), It.IsAny<ToolExecutionBudget>(), It.IsAny<LMLocal.Application.Usage.UsageCallContext>()))
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


            _toolExecutionManagerMock
                .Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<ToolQueue>(), It.IsAny<ToolExecutionBudget>(), It.IsAny<LMLocal.Application.Usage.UsageCallContext>()))
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
            Assert.That(recoveredHistory[0].Content, Is.EqualTo(SubAgentRecoveryPolicy.DiscardedTurnPlaceholder));
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


            _toolExecutionManagerMock
                .Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<ToolQueue>(), It.IsAny<ToolExecutionBudget>(), It.IsAny<LMLocal.Application.Usage.UsageCallContext>()))
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
            // The queue has no definitions, so every call is rejected before execution.
            // Recovery is pinned off: this test guards the rejection, not the recovery path, and the arguments differ
            // per round, so the loop guard cannot end the run before its round limit.
            SetupToolCallsWithVaryingArguments("write_file");
            var response = await CreateService(loopRecoveryEnabled: false)
                .ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.Contain("maximum rounds"));

            _toolExecutionManagerMock.Verify(
                t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<ToolQueue>(), It.IsAny<ToolExecutionBudget>(), It.IsAny<LMLocal.Application.Usage.UsageCallContext>()),
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
                t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<ToolQueue>(), It.IsAny<ToolExecutionBudget>(), It.IsAny<LMLocal.Application.Usage.UsageCallContext>()),
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
            Assert.That(history[0].Content, Is.EqualTo(SubAgentRecoveryPolicy.DiscardedTurnPlaceholder));
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
        public async Task ExecutePromptAsync_MalformedToolCallOnLastRound_AnswersWithoutTools()
        {
            // No round is left for a retry: the round limit guard asks for an answer in one extra round without tools.
            AllowTool("find_files");
            var requests = SetupRounds(AbortedToolRound(), CompletedRound("nothing was checked"));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(maxRounds: 1), CancellationToken.None);

            Assert.That(response.Success, Is.True, response.Error);
            Assert.That(response.Content, Is.EqualTo("nothing was checked"));
            Assert.That(response.Recoveries, Has.Count.EqualTo(1));
            Assert.That(response.Recoveries[0], Does.Contain("round limit (1)"));
            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[1].Tools, Is.Empty);
        }

        // =====================================================================
        // Round limit: one extra round without tools instead of a failed run
        // =====================================================================

        private static StreamRoundResult FindFilesRound(string fileName)
        {
            return new StreamRoundResult
            {
                Outcome = StreamRoundOutcome.ToolCalls,
                Stream = new StreamCompletionResult
                {
                    ToolCalls = new[]
                    {
                        new ToolCallRecord { CallId = "call_" + fileName, FunctionName = "find_files", ArgumentsJson = "{\"file_name\":\"" + fileName + "\"}" }
                    }
                }
            };
        }

        [Test]
        public async Task ExecutePromptAsync_ToolCallsInLastRound_AnswerFromGatheredResults()
        {
            _settingsManagerMock.SetupGet(s => s.AssistantPlaceholder).Returns("INTERRUPTED");
            AllowTool("find_files");
            _toolExecutionManagerMock
                .Setup(t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<ToolQueue>(), It.IsAny<ToolExecutionBudget>(), It.IsAny<LMLocal.Application.Usage.UsageCallContext>()))
                .ReturnsAsync(new ToolExecutionResult { ToolId = "call_A.cs", ToolName = "find_files", Result = "src\\A.cs" });

            var requests = SetupRounds(FindFilesRound("A.cs"), FindFilesRound("B.cs"), CompletedRound("A.cs is in src; B.cs was not checked."));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(maxRounds: 2), CancellationToken.None);

            Assert.That(response.Success, Is.True, response.Error);
            Assert.That(response.Content, Is.EqualTo("A.cs is in src; B.cs was not checked."));
            Assert.That(response.Rounds, Is.EqualTo(3), "The final answer comes in one extra round after the limit.");
            Assert.That(response.Recoveries, Has.Count.EqualTo(1));
            Assert.That(response.Recoveries[0], Is.EqualTo("SubAgent reached the round limit (2) and answered from the results gathered so far."));

            _toolExecutionManagerMock.Verify(
                t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<ToolQueue>(), It.IsAny<ToolExecutionBudget>(), It.IsAny<LMLocal.Application.Usage.UsageCallContext>()),
                Times.Once,
                "Only the first round's call runs: the call of the last round has no round left for its result.");

            Assert.That(requests, Has.Count.EqualTo(3));
            Assert.That(requests[1].Tools, Is.Not.Empty);
            Assert.That(requests[2].Tools, Is.Empty, "The final-answer round is sent without tools.");
            Assert.That(requests[2].IsToolRound, Is.False);
            Assert.That(requests[2].Prompt, Is.EqualTo(SubAgentRecoveryPolicy.RoundLimitNudge));

            var history = requests[2].History.GetHistoryCopy();
            Assert.That(history.Select(m => m.Role), Is.EqualTo(new[] { "assistant" }),
                "The unfinished tool turn is closed like in the other recoveries.");
        }

        [Test]
        public async Task ExecutePromptAsync_ToolCallsInFinalAnswerRound_Fail()
        {
            AllowTool("find_files");
            SetupRounds(FindFilesRound("A.cs"), FindFilesRound("B.cs"));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(maxRounds: 1), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Is.EqualTo("SubAgent exceeded maximum rounds (1). " + SubAgentRecoveryPolicy.LimitRetryHint));
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

        /// <summary>
        /// Every round asks for the same tool with different arguments, so the loop guard has nothing to catch and only
        /// the round limit can end the run.
        /// </summary>
        private void SetupToolCallsWithVaryingArguments(string toolName)
        {
            int round = 0;

            _roundServiceMock
                .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    int current = ++round;

                    return Task.FromResult(new StreamRoundResult
                    {
                        Outcome = StreamRoundOutcome.ToolCalls,
                        Stream = new StreamCompletionResult
                        {
                            ToolCalls = new[]
                            {
                                new ToolCallRecord
                                {
                                    CallId = "call_1",
                                    FunctionName = toolName,
                                    ArgumentsJson = $"{{\"path\":\"a{current}.cs\"}}"
                                }
                            }
                        }
                    });
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
        // Budget: the reserve survives to the answer round
        // =====================================================================

        [Test]
        public async Task ExecutePromptAsync_BudgetNearlySpent_AnswersFromGatheredResultsInOneRound()
        {
            AllowTool("find_files");
            _toolExecutionManagerMock
                .Setup(t => t.ExecuteToolAsync(
                    It.IsAny<ToolCallRecord>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<ToolQueue>(),
                    It.IsAny<ToolExecutionBudget>(),
                    It.IsAny<LMLocal.Application.Usage.UsageCallContext>()))
                .ReturnsAsync(new ToolExecutionResult { ToolId = "call_A.cs", ToolName = "find_files", Result = "src\\A.cs" });

            var requests = SetupRounds(FindFilesRound("A.cs"), CompletedRound("A.cs is in src."));

            // A 1 s budget is far below the reserve, so the first tool round already spends the last of it.
            var response = await CreateService()
                .ExecutePromptAsync(BuildRequest(timeoutSeconds: 1), CancellationToken.None);

            Assert.That(response.Success, Is.True, response.Error);
            Assert.That(response.Content, Is.EqualTo("A.cs is in src."));
            Assert.That(response.Recoveries, Has.Count.EqualTo(1));
            Assert.That(response.Recoveries[0], Does.Contain("budget is nearly exhausted"));

            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[1].Tools, Is.Empty,
                "The answer round must not offer tools the budget cannot pay for.");
            Assert.That(requests[1].IsToolRound, Is.True,
                "The results of the round that spent the budget must still reach the model.");
            Assert.That(requests[1].ToolResults, Is.Not.Null.And.Not.Empty);
            Assert.That(requests[1].RoundNote, Is.EqualTo(SubAgentRecoveryPolicy.BudgetExhaustedNudge),
                "Without the nudge the model sees results but is never told that the time is up.");
        }

        [Test]
        public async Task ExecutePromptAsync_NoTimeout_KeepsToolsToTheEnd()
        {
            AllowTool("find_files");
            _toolExecutionManagerMock
                .Setup(t => t.ExecuteToolAsync(
                    It.IsAny<ToolCallRecord>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<ToolQueue>(),
                    It.IsAny<ToolExecutionBudget>(),
                    It.IsAny<LMLocal.Application.Usage.UsageCallContext>()))
                .ReturnsAsync(new ToolExecutionResult { ToolId = "call_A.cs", ToolName = "find_files", Result = "src\\A.cs" });

            var requests = SetupRounds(FindFilesRound("A.cs"), CompletedRound("done"));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.True, response.Error);
            Assert.That(response.Recoveries, Is.Null, "No deadline means no budget guard.");
            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[1].Tools, Is.Not.Empty);
            Assert.That(requests[1].RoundNote, Is.Null);
        }

        [Test]
        public async Task ExecutePromptAsync_BudgetFinalRound_ToolCallStopsTheRun()
        {
            AllowTool("find_files");
            _toolExecutionManagerMock
                .Setup(t => t.ExecuteToolAsync(
                    It.IsAny<ToolCallRecord>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<ToolQueue>(),
                    It.IsAny<ToolExecutionBudget>(),
                    It.IsAny<LMLocal.Application.Usage.UsageCallContext>()))
                .ReturnsAsync(new ToolExecutionResult { ToolId = "call_A.cs", ToolName = "find_files", Result = "src\\A.cs" });

            // After the first tool round the 1 s budget is spent, so the next round has no tools; the model calls one anyway.
            SetupRounds(FindFilesRound("A.cs"), FindFilesRound("B.cs"));

            var response = await CreateService()
                .ExecutePromptAsync(BuildRequest(timeoutSeconds: 1), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Error, Does.Contain("final answer round"));
            Assert.That(response.Error, Does.Contain("agent budget exhausted"));

            _toolExecutionManagerMock.Verify(
                t => t.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<ToolQueue>(), It.IsAny<ToolExecutionBudget>(), It.IsAny<LMLocal.Application.Usage.UsageCallContext>()),
                Times.Once,
                "Only the first round's call runs: the budget-forced final answer round must not execute tool calls.");
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

        [Test]
        public async Task ExecutePromptAsync_OwnerCancellation_ReportsCallerCancellation()
        {
            _roundServiceMock
                .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                .Returns((StreamRoundRequest req, CancellationToken ct) => RoundThatNeverCompletes(ct));

            var request = BuildRequest(timeoutSeconds: 360);
            request.Cancellation = new SubAgentRunCancellationOwner();

            using (var cts = new CancellationTokenSource())
            {
                // The owner (a pipeline group) marks the reason before it cancels the token.
                request.Cancellation.MarkStoppedByCaller();
                cts.Cancel();

                var response = await CreateService().ExecutePromptAsync(request, cts.Token);

                Assert.That(response.Success, Is.False);
                Assert.That(response.FailureKind, Is.EqualTo(SubAgentFailureKind.Cancelled));
                Assert.That(response.Error, Does.StartWith("SubAgent stopped: cancelled by its caller"));
                Assert.That(response.Error, Does.Not.Contain("stopped by the user"),
                    "a sibling branch failure must not read like a user stop.");
                Assert.That(response.Error, Does.Not.Contain("timed out"));
                Assert.That(response.Error.Length, Is.LessThanOrEqualTo(120), response.Error);
            }
        }

        [Test]
        public async Task ExecutePromptAsync_CancellationWithoutStopOrTimeout_StaysTimeout()
        {
            // Neither the user nor the owner cancelled: previously reported as a timeout, and it still is.
            SetupRoundThrows(new OperationCanceledException());

            var response = await CreateService()
                .ExecutePromptAsync(BuildRequest(timeoutSeconds: 360), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.FailureKind, Is.EqualTo(SubAgentFailureKind.Timeout));
            Assert.That(response.Error, Does.StartWith("SubAgent timed out after 360s"));
            Assert.That(response.Error, Does.Not.Contain("cancelled"));
        }

        // =====================================================================
        // FailureKind: a pipeline step repeats a run only for provider / timeout / incomplete failures, so every
        // FailRun path must report the right kind. A wrong "Unexpected" silently disables the retries.
        // =====================================================================

        [Test]
        public async Task FailureKind_Success_IsNone()
        {
            SetupRounds(CompletedRound("done"));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.True, response.Error);
            Assert.That(response.FailureKind, Is.EqualTo(SubAgentFailureKind.None));
        }

        [Test]
        public async Task FailureKind_StreamError_IsProvider()
        {
            SetupRounds(new StreamRoundResult
            {
                Outcome = StreamRoundOutcome.Error,
                Stream = new StreamCompletionResult { ErrorMessage = "502 Bad Gateway" }
            });

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.FailureKind, Is.EqualTo(SubAgentFailureKind.Provider));
        }

        [Test]
        public async Task FailureKind_StreamCancelledWithoutStop_IsProvider()
        {
            SetupRounds(new StreamRoundResult
            {
                Outcome = StreamRoundOutcome.Cancelled,
                Stream = new StreamCompletionResult { StopReason = StreamStopReason.Cancelled }
            });

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.FailureKind, Is.EqualTo(SubAgentFailureKind.Provider));
        }

        [Test]
        public async Task FailureKind_ApiException404_IsProviderNotRetryable()
        {
            SetupRoundThrows(new ApiException(new ApiErrorInfo { Message = "Model not found" }, 404));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.FailureKind, Is.EqualTo(SubAgentFailureKind.ProviderNotRetryable));
        }

        [Test]
        public async Task FailureKind_ApiException503_IsProvider()
        {
            SetupRoundThrows(new ApiException(new ApiErrorInfo { Message = "Service unavailable", Code = 503 }, 503));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.FailureKind, Is.EqualTo(SubAgentFailureKind.Provider));
        }

        [TestCase(400, null, null)]
        [TestCase(401, null, null)]
        [TestCase(403, null, null)]
        [TestCase(404, "model_not_found", null)]
        [TestCase(413, null, null)]
        [TestCase(422, null, null)]
        [TestCase(429, null, null)]
        [TestCase(429, "insufficient_quota", null)]
        [TestCase(503, "rate_limit_exceeded", null)]
        [TestCase(400, "context_length_exceeded", null)]
        [TestCase(500, null, "exceed_context_size_error")]
        [TestCase(null, null, "exceed_context_size_error")]
        public async Task FailureKind_PermanentOrRateLimitedError_IsProviderNotRetryable(int? status, string code, string type)
        {
            SetupRounds(ErrorRound(status, code, type));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.FailureKind, Is.EqualTo(SubAgentFailureKind.ProviderNotRetryable));
        }

        [TestCase(408, null, null)]
        [TestCase(500, null, null)]
        [TestCase(502, null, null)]
        [TestCase(503, null, null)]
        [TestCase(504, null, null)]
        [TestCase(null, "server_error", "server_error")]
        [TestCase(null, null, null)]
        public async Task FailureKind_TransientError_IsProvider(int? status, string code, string type)
        {
            SetupRounds(ErrorRound(status, code, type));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.FailureKind, Is.EqualTo(SubAgentFailureKind.Provider));
        }

        private static StreamRoundResult ErrorRound(int? status, string code, string type)
        {
            return new StreamRoundResult
            {
                Outcome = StreamRoundOutcome.Error,
                Stream = new StreamCompletionResult
                {
                    ErrorMessage = "provider error",
                    ErrorCode = code ?? status?.ToString(),
                    ErrorType = type,
                    HttpStatusCode = status,
                    FinishReason = "error",
                    StopReason = StreamStopReason.Error
                }
            };
        }

        [Test]
        public async Task FailureKind_UnreachableProvider_IsProvider()
        {
            // The most common provider failure: the local server is not running.
            SetupRoundThrows(new HttpRequestException(
                GenericWrapper,
                new InvalidOperationException("No connection could be made because the target machine actively refused it")));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.FailureKind, Is.EqualTo(SubAgentFailureKind.Provider),
                "an unreachable server must be retryable as a provider failure");
            Assert.That(response.Error, Does.Contain("actively refused it"), "the message stays as before");
        }

        [Test]
        public async Task FailureKind_IncompleteAnswer_IsIncomplete()
        {
            SetupRounds(new StreamRoundResult
            {
                Outcome = StreamRoundOutcome.Incomplete,
                Stream = new StreamCompletionResult { ErrorMessage = "Response was cut off by the token limit." }
            });

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.FailureKind, Is.EqualTo(SubAgentFailureKind.Incomplete));
        }

        [Test]
        public async Task FailureKind_RoundLimit_IsAgent()
        {
            AllowTool("find_files");
            SetupRounds(FindFilesRound("A.cs"), FindFilesRound("B.cs"));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(maxRounds: 1), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.FailureKind, Is.EqualTo(SubAgentFailureKind.Agent),
                "a logical failure of the agent is never retried: the same agent would fail the same way");
        }

        [Test]
        public async Task FailureKind_MissingModel_IsInvalidRequest()
        {
            var request = BuildRequest();
            request.Model = "  ";

            var response = await CreateService().ExecutePromptAsync(request, CancellationToken.None);

            Assert.That(response.FailureKind, Is.EqualTo(SubAgentFailureKind.InvalidRequest));
        }

        [Test]
        public async Task FailureKind_UserStop_IsCancelled()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                SetupRoundThrows(new OperationCanceledException(cts.Token));

                var response = await CreateService().ExecutePromptAsync(BuildRequest(timeoutSeconds: 360), cts.Token);

                Assert.That(response.FailureKind, Is.EqualTo(SubAgentFailureKind.Cancelled));
                AssertReadsAsUserStop(response);
            }
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
