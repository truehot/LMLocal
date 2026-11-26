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
using LMLocal.Application.Usage;
using LMLocal.Core.Models;
using LMLocal.Core.Models.Usage;
using LMLocal.Infrastructure.LlmApi.Provider;
using LMLocal.Infrastructure.Persistence;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.Usage
{
    /// <summary>
    /// Covers the usage record a subagent run writes: it belongs to the parent chat session and request, it sums every
    /// round, and it is written on every way the run can end, because a failed or stopped run spent tokens too.
    /// </summary>
    [TestFixture]
    public class SubAgentUsageRecordingTests
    {
        private const string ParentSession = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        private const string RequestId = "request-1";
        private static readonly Guid RunSession = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        private Mock<ISettingsManager> _settingsManager;
        private Mock<IStreamingRoundService> _roundService;
        private Mock<IToolExecutionManager> _toolManager;
        private Mock<IToolQueueProvider> _queueProvider;
        private Mock<IChatPersistenceFactory> _persistenceFactory;
        private Mock<IChatPersistenceService> _persistence;
        private Mock<IUsageRecorder> _recorder;
        private List<UsageRecord> _records;

        [SetUp]
        public void SetUp()
        {
            _settingsManager = new Mock<ISettingsManager>();
            _settingsManager.SetupGet(s => s.LocalAppDataFolder).Returns("LMLocalChat.Tests");
            _settingsManager.SetupGet(s => s.SystemPrompt).Returns("system");

            _roundService = new Mock<IStreamingRoundService>();
            _toolManager = new Mock<IToolExecutionManager>();
            _queueProvider = new Mock<IToolQueueProvider>();
            _persistenceFactory = new Mock<IChatPersistenceFactory>();
            _persistence = new Mock<IChatPersistenceService>();
            _recorder = new Mock<IUsageRecorder>();
            _records = new List<UsageRecord>();

            _queueProvider
                .Setup(q => q.GetSubAgentQueue(It.IsAny<SubAgentRunRequest>()))
                .Returns(ToolQueue.ForSubAgent("agent", new List<ToolDefinition> { new ToolDefinition { Name = "find_files" } }));

            _toolManager
                .Setup(t => t.ExecuteToolAsync(
                    It.IsAny<ToolCallRecord>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<ToolQueue>(),
                    It.IsAny<ToolExecutionBudget>(),
                    It.IsAny<UsageCallContext>()))
                .ReturnsAsync(new ToolExecutionResult { ToolId = "call_1", ToolName = "find_files", Result = "src\\A.cs" });

            _persistence.Setup(p => p.MarkNewSessionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            _persistence.SetupGet(p => p.CurrentSessionId).Returns(RunSession);
            _persistenceFactory.Setup(f => f.Create(It.IsAny<string>())).Returns(_persistence.Object);

            _recorder
                .Setup(r => r.AppendAsync(It.IsAny<UsageRecord>(), It.IsAny<CancellationToken>()))
                .Callback((UsageRecord record, CancellationToken token) => _records.Add(record))
                .Returns(Task.CompletedTask);
        }

        private SubAgentsService CreateService()
        {
            return new SubAgentsService(
                _settingsManager.Object,
                new Mock<IFileSystem>().Object,
                _roundService.Object,
                () => _toolManager.Object,
                _queueProvider.Object,
                _persistenceFactory.Object,
                new ProviderPolicy(),
                SubAgentsService.DefaultLoopRecoveryEnabled,
                _recorder.Object);
        }

        private static SubAgentRunRequest BuildRequest(string origin = null, int? timeoutSeconds = null)
        {
            return new SubAgentRunRequest
            {
                AgentName = "code_explorer_subagent",
                Prompt = "find A.cs",
                ProviderType = "lmstudio",
                BaseUrl = "http://localhost:1234",
                Model = "qwen3-coder-30b",
                TimeoutSeconds = timeoutSeconds,
                MaxRounds = 10,
                ParentSessionId = ParentSession,
                RequestId = RequestId,
                Origin = origin
            };
        }

        private static StreamCompletionResult Usage(int prompt, int completion)
        {
            return new StreamCompletionResult
            {
                TokensPerSecond = 50,
                TokenUsage = new TokenUsageMetadata { PromptTokens = prompt, CompletionTokens = completion, TotalTokens = prompt + completion }
            };
        }

        private static StreamRoundResult ToolRound(int prompt, int completion)
        {
            var stream = Usage(prompt, completion);
            stream.ToolCalls = new[]
            {
                new ToolCallRecord { CallId = "call_1", FunctionName = "find_files", ArgumentsJson = "{\"file_name\":\"A.cs\"}" }
            };

            return new StreamRoundResult { Outcome = StreamRoundOutcome.ToolCalls, Stream = stream };
        }

        private static StreamRoundResult EndRound(StreamRoundOutcome outcome, int prompt, int completion, string content = null, string error = null)
        {
            var stream = Usage(prompt, completion);
            stream.ContentResponse = content;
            stream.ErrorMessage = error;
            if (outcome == StreamRoundOutcome.Cancelled)
                stream.StopReason = StreamStopReason.Cancelled;

            return new StreamRoundResult { Outcome = outcome, Stream = stream };
        }

        private void SetupRounds(params StreamRoundResult[] rounds)
        {
            var queue = new Queue<StreamRoundResult>(rounds);

            _roundService
                .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                .Returns((StreamRoundRequest req, CancellationToken ct) => Task.FromResult(queue.Dequeue()));
        }

        private static Task<StreamRoundResult> RoundThatNeverCompletes(CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<StreamRoundResult>();
            ct.Register(() => tcs.TrySetCanceled(ct));
            return tcs.Task;
        }

        private UsageRecord SingleRecord()
        {
            Assert.That(_records, Has.Count.EqualTo(1), "Every run writes exactly one usage record.");
            return _records[0];
        }

        [Test]
        public async Task CompletedRun_IsRecordedUnderTheParentSessionAndRequest_WithEveryRoundSummed()
        {
            SetupRounds(ToolRound(1000, 50), EndRound(StreamRoundOutcome.Completed, 1500, 200, "A.cs is in src."));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.True, response.Error);

            UsageRecord record = SingleRecord();
            Assert.That(record.Source, Is.EqualTo(UsageSource.Subagent));
            Assert.That(record.SessionId, Is.EqualTo(ParentSession), "The record belongs to the chat session that ran the agent.");
            Assert.That(record.RequestId, Is.EqualTo(RequestId));
            Assert.That(record.Origin, Is.EqualTo(UsageOrigins.Chat));
            Assert.That(record.Agent, Is.EqualTo("code_explorer_subagent"));
            Assert.That(record.RunSessionId, Is.EqualTo(RunSession.ToString("D")), "The run's own log session, in the chat log format.");
            Assert.That(record.Provider, Is.EqualTo("lmstudio"));
            Assert.That(record.BaseUrl, Is.EqualTo("http://localhost:1234"));
            Assert.That(record.Model, Is.EqualTo("qwen3-coder-30b"));
            Assert.That(record.Rounds, Is.EqualTo(2));
            Assert.That(record.Prompt, Is.EqualTo(2500), "Every round counts, not only the last one.");
            Assert.That(record.Completion, Is.EqualTo(250));
            Assert.That(record.ContextPeak, Is.EqualTo(1500));
            Assert.That(record.Outcome, Is.EqualTo(UsageOutcome.Ok));
            Assert.That(record.Error, Is.Null);

            Assert.That(response.PromptTokens, Is.EqualTo(2500), "The response shown to the main model carries the sum as well.");
        }

        [Test]
        public async Task StreamError_IsRecordedAsError_WithTheTokensOfTheEarlierRounds()
        {
            SetupRounds(ToolRound(1000, 50), EndRound(StreamRoundOutcome.Error, 0, 0, error: "boom"));

            var response = await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            Assert.That(response.Success, Is.False);

            UsageRecord record = SingleRecord();
            Assert.That(record.Outcome, Is.EqualTo(UsageOutcome.Error));
            Assert.That(record.Error, Does.Contain("boom"));
            Assert.That(record.Prompt, Is.EqualTo(1000), "The rounds before the failure spent tokens and stay in the record.");
            Assert.That(record.Rounds, Is.EqualTo(2));
        }

        [Test]
        public async Task IncompleteRound_IsRecordedAsIncomplete()
        {
            SetupRounds(EndRound(StreamRoundOutcome.Incomplete, 900, 4000, "partial", "The answer was cut off (length)."));

            await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            UsageRecord record = SingleRecord();
            Assert.That(record.Outcome, Is.EqualTo(UsageOutcome.Incomplete));
            Assert.That(record.Completion, Is.EqualTo(4000));
        }

        [Test]
        public async Task CancelledStreamWithoutStop_IsRecordedAsError()
        {
            SetupRounds(EndRound(StreamRoundOutcome.Cancelled, 800, 10));

            await CreateService().ExecutePromptAsync(BuildRequest(timeoutSeconds: 360), CancellationToken.None);

            UsageRecord record = SingleRecord();
            Assert.That(record.Outcome, Is.EqualTo(UsageOutcome.Error),
                "A stream that ended as cancelled while nobody stopped it is a failure of the run.");
        }

        [Test]
        public async Task UserStop_IsRecordedAsCancelled_WithoutError()
        {
            _roundService
                .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                .Returns((StreamRoundRequest req, CancellationToken ct) => RoundThatNeverCompletes(ct));

            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                await CreateService().ExecutePromptAsync(BuildRequest(timeoutSeconds: 360), cts.Token);
            }

            UsageRecord record = SingleRecord();
            Assert.That(record.Outcome, Is.EqualTo(UsageOutcome.Cancelled));
            Assert.That(record.Error, Is.Null, "A user stop is not a failure, so no error text is kept.");
        }

        [Test]
        public async Task InternalTimeout_IsRecordedAsTimeout()
        {
            _roundService
                .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                .Returns((StreamRoundRequest req, CancellationToken ct) => RoundThatNeverCompletes(ct));

            await CreateService().ExecutePromptAsync(BuildRequest(timeoutSeconds: 1), CancellationToken.None);

            UsageRecord record = SingleRecord();
            Assert.That(record.Outcome, Is.EqualTo(UsageOutcome.Timeout));
            Assert.That(record.Error, Does.StartWith("SubAgent timed out"));
        }

        [Test]
        public async Task ProviderException_IsRecordedAsError()
        {
            _roundService
                .Setup(r => r.RunAsync(It.IsAny<StreamRoundRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("connection refused"));

            await CreateService().ExecutePromptAsync(BuildRequest(), CancellationToken.None);

            UsageRecord record = SingleRecord();
            Assert.That(record.Outcome, Is.EqualTo(UsageOutcome.Error));
            Assert.That(record.Error, Does.Contain("connection refused"));
        }

        [Test]
        public async Task IpcRun_IsRecordedWithIpcOrigin()
        {
            SetupRounds(EndRound(StreamRoundOutcome.Completed, 500, 20, "done"));

            var request = BuildRequest(UsageOrigins.Ipc);
            request.ParentSessionId = null;

            await CreateService().ExecutePromptAsync(request, CancellationToken.None);

            UsageRecord record = SingleRecord();
            Assert.That(record.Origin, Is.EqualTo(UsageOrigins.Ipc), "IPC benchmark runs are written but kept out of the dialog.");
            Assert.That(record.SessionId, Is.Null, "An IPC run has no parent chat session.");
        }
    }
}
