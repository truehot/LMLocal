using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.SubAgents;
using LMLocal.Application.Tool;
using LMLocal.Application.Usage;
using LMLocal.Core.Models;
using LMLocal.Core.Models.Usage;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Requests;
using LMLocal.Infrastructure.LlmApi.Responses;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.WebView;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.Usage
{
    /// <summary>
    /// Covers who writes the usage of a chat request and what the record says.
    /// </summary>
    [TestFixture]
    public class UsageRecordingTests
    {
        private static readonly Guid SessionA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid SessionB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        private Mock<IChatStreamService> _chatService;
        private Mock<IToolExecutionManager> _toolManager;
        private Mock<IHistoryCompactor> _compactor;
        private Mock<ISnapshotManager> _snapshotManager;
        private Mock<ISubAgentsParallelPolicy> _parallelPolicy;
        private Mock<ISubAgentsParallelRunner> _parallelRunner;
        private Mock<IActiveModelContext> _activeModel;
        private Mock<ISettingsManager> _settings;
        private Mock<IChatHistoryManager> _history;
        private Mock<IUsageRecorder> _recorder;
        private List<UsageRecord> _records;

        [SetUp]
        public void SetUp()
        {
            _chatService = new Mock<IChatStreamService>();
            _toolManager = new Mock<IToolExecutionManager>();
            _compactor = new Mock<IHistoryCompactor>();
            _snapshotManager = new Mock<ISnapshotManager>();
            _parallelPolicy = new Mock<ISubAgentsParallelPolicy>();
            _parallelRunner = new Mock<ISubAgentsParallelRunner>();
            _activeModel = new Mock<IActiveModelContext>();
            _settings = new Mock<ISettingsManager>();
            _history = new Mock<IChatHistoryManager>();
            _recorder = new Mock<IUsageRecorder>();
            _records = new List<UsageRecord>();

            _activeModel.Setup(m => m.MaxContextLength).Returns(32768);
            _settings
                .Setup(s => s.Current)
                .Returns(new AppSettings { Provider = "lmstudio", LmStudioBaseUrl = "http://localhost:1234" });
            _history.Setup(h => h.CurrentSessionId).Returns(SessionA);
            _history
                .Setup(h => h.GetHistoryCopy())
                .Returns(new List<ChatMessage> { new ChatMessage("user", "first prompt") });

            _recorder
                .Setup(r => r.AppendAsync(It.IsAny<UsageRecord>(), It.IsAny<CancellationToken>()))
                .Callback((UsageRecord record, CancellationToken token) => _records.Add(record))
                .Returns(Task.CompletedTask);
        }

        /// <summary>Builds the orchestrator with all its dependencies, the way the DI container builds it.</summary>
        private ChatSessionOrchestrator CreateOrchestrator()
        {
            return new OrchestratorTestBuilder
            {
                ChatService = _chatService,
                ToolManager = _toolManager,
                Compactor = _compactor,
                History = _history,
                SnapshotManager = _snapshotManager,
                ParallelPolicy = _parallelPolicy,
                ParallelRunner = _parallelRunner.Object,
                ActiveModelContext = _activeModel,
                SettingsManager = _settings,
                UsageRecorder = _recorder
            }.Build();
        }

        /// <summary>Runs one request of the main chat.</summary>
        private async Task RunSessionAsync(string replaceLast = null)
        {
            await CreateOrchestrator()
                .RunSessionAsync(
                    new GenerateStreamContext
                    {
                        Prompt = "first prompt",
                        ModelId = "qwen3-coder-30b",
                        ReplaceLast = replaceLast,
                        ReplaceLastPrompt = replaceLast != null ? "first prompt" : null
                    },
                    _ => Task.CompletedTask,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        /// <summary>Answers the single generation round with <paramref name="result"/>.</summary>
        private void StreamAnswers(StreamCompletionResult result, Action afterResult = null)
        {
            _chatService
                .Setup(s => s.GenerateStreamAsync(
                    It.IsAny<GenerateStreamContext>(),
                    It.IsAny<List<ToolResultMessage>>(),
                    It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(),
                    It.IsAny<Func<StreamCompletionResult, Task>>(),
                    It.IsAny<Func<RateLimitWaitInfo, Task>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<GenerateStreamContext, List<ToolResultMessage>, Func<TextStreamChunk, TokenGenerationStats, Task>, Func<StreamCompletionResult, Task>, Func<RateLimitWaitInfo, Task>, CancellationToken>(
                    async (context, toolResults, onChunk, onComplete, onRateLimitWait, ct) =>
                    {
                        if (onComplete != null)
                            await onComplete(result).ConfigureAwait(false);

                        afterResult?.Invoke();
                    });
        }

        private static StreamCompletionResult CompletedRound(int prompt, int completion)
        {
            return new StreamCompletionResult
            {
                FinishReason = "stop",
                StopReason = StreamStopReason.Completed,
                TokensPerSecond = 20,
                TokenUsage = new TokenUsageMetadata
                {
                    PromptTokens = prompt,
                    CompletionTokens = completion,
                    TotalTokens = prompt + completion
                }
            };
        }

        [Test]
        public async Task Orchestrator_WritesOneMainRecord_ForTheRequest()
        {
            StreamAnswers(CompletedRound(1000, 100));

            await RunSessionAsync();

            Assert.That(_records, Has.Count.EqualTo(1),
                "The orchestrator must write the usage of its request.");

            UsageRecord record = _records[0];
            Assert.That(record.Source, Is.EqualTo(UsageSource.Main));
            Assert.That(record.Outcome, Is.EqualTo(UsageOutcome.Ok));
            Assert.That(record.SessionId, Is.EqualTo(SessionA.ToString("D")), "Session ids are written like the chat log writes them.");
            Assert.That(record.RequestId, Is.Not.Null.And.Not.Empty);
            Assert.That(record.Provider, Is.EqualTo("lmstudio"));
            Assert.That(record.BaseUrl, Is.EqualTo("http://localhost:1234"));
            Assert.That(record.Model, Is.EqualTo("qwen3-coder-30b"));
            Assert.That(record.Title, Is.EqualTo("first prompt"));
            Assert.That(record.Prompt, Is.EqualTo(1000));
            Assert.That(record.Completion, Is.EqualTo(100));
            Assert.That(record.ContextMax, Is.EqualTo(32768),
                "The active model context must reach the orchestrator, otherwise peak ctx cannot be computed.");
        }

        [Test]
        public async Task SessionId_IsTheOneOfTheStart_EvenWhenACompactionReplacedTheSessionMeanwhile()
        {
            // A compaction inside the request starts a new log session; the record must stay with the old one.
            StreamAnswers(CompletedRound(1000, 100), () => _history.Setup(h => h.CurrentSessionId).Returns(SessionB));

            await RunSessionAsync();

            Assert.That(_records, Has.Count.EqualTo(1));
            Assert.That(_records[0].SessionId, Is.EqualTo(SessionA.ToString("D")),
                "All records of one request belong to the session the request started in.");
        }

        [Test]
        public async Task Regenerate_RecordsTheRequest_UnderTheSessionTheTruncationStarted()
        {
            // A regenerate truncates the history and starts a new log session BEFORE the first round. The Current tab
            // shows the live session, so the request must be recorded under the new one, not the one of the click.
            _history
                .Setup(h => h.TruncateLastExchangeAsync(It.IsAny<bool>(), It.IsAny<string>()))
                .Callback(() => _history.Setup(h => h.CurrentSessionId).Returns(SessionB))
                .ReturnsAsync(TruncateLastExchangeOutcome.Replaced);
            StreamAnswers(CompletedRound(1000, 100));

            await RunSessionAsync(ReplaceLastModes.Answer);

            _history.Verify(h => h.TruncateLastExchangeAsync(It.IsAny<bool>(), It.IsAny<string>()), Times.Once);
            Assert.That(_records, Has.Count.EqualTo(1));
            Assert.That(_records[0].SessionId, Is.EqualTo(SessionB.ToString("D")),
                "The session is read after the regenerate replaced it, right before the first round.");
        }

        [Test]
        public async Task FailedRound_IsRecordedToo_WithItsOutcomeAndError()
        {
            StreamAnswers(new StreamCompletionResult
            {
                StopReason = StreamStopReason.Error,
                ErrorMessage = "Provider stream error: boom"
            });

            await RunSessionAsync();

            Assert.That(_records, Has.Count.EqualTo(1), "A failed request spent tokens too.");
            Assert.That(_records[0].Outcome, Is.EqualTo(UsageOutcome.Error));
            Assert.That(_records[0].Error, Is.EqualTo("Provider stream error: boom"));
        }

        [Test]
        public async Task ManualSummarize_OutsideAChatRequest_IsRecordedUnderTheLiveSession()
        {
            var client = new Mock<IOpenApiAdapter>();
            client
                .Setup(c => c.SendChatAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SendChatResponse
                {
                    Choices = new List<ChatChoice>
                    {
                        new ChatChoice
                        {
                            FinishReason = "stop",
                            Message = new AssistantMessage { Role = "assistant", Content = "summary" }
                        }
                    },
                    Usage = new CompletionUsage { PromptTokens = 900, CompletionTokens = 90, TotalTokens = 990 }
                });

            var compactor = new HistoryCompactor(
                _history.Object, client.Object, _settings.Object, _activeModel.Object, null, null, _recorder.Object);

            string summary = await compactor
                .SummarizeAsync(new List<ChatMessage> { new ChatMessage("user", "hi") }, "qwen3-coder-30b", CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(summary, Is.EqualTo("summary"));
            Assert.That(_records, Has.Count.EqualTo(1),
                "The manual summary runs outside a chat request and spends tokens, so it must be recorded as well.");
            Assert.That(_records[0].Source, Is.EqualTo(UsageSource.Compaction));
            Assert.That(_records[0].SessionId, Is.EqualTo(SessionA.ToString("D")));
            Assert.That(_records[0].RequestId, Is.Null, "A manual summary belongs to no user request.");
            Assert.That(_records[0].Model, Is.EqualTo("qwen3-coder-30b"));
            Assert.That(_records[0].Prompt, Is.EqualTo(900));
            Assert.That(_records[0].Completion, Is.EqualTo(90));
            Assert.That(_records[0].Outcome, Is.EqualTo(UsageOutcome.Ok));
        }
    }
}
