using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Core.Exceptions;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Responses;
using LMLocal.Infrastructure.ModelsConfig;
using LMLocal.Infrastructure.Tooling;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Internal
{
    /// <summary>
    /// Retry behaviour of <see cref="StreamingRoundService.RunAsync"/>. The retry is structurally limited to obtaining
    /// the stream, so an accepted stream must never be requested again (that would duplicate chunks in the UI).
    /// </summary>
    [TestFixture]
    public class RateLimitRetryTests
    {
        private Mock<IOpenApiAdapter> _adapter;
        private Mock<IStreamProcessor> _processor;
        private Mock<IStreamProcessorFactory> _factory;
        private Mock<IModelsConfigManager> _modelsConfig;
        private Mock<IChatHistoryManager> _history;
        private AppSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = new AppSettings { EnableRateLimitHandling = true };

            var settingsManager = new Mock<ISettingsManager>();
            settingsManager.Setup(s => s.Current).Returns(_settings);
            settingsManager.Setup(s => s.BatchIntervalMs).Returns(100);

            _adapter = new Mock<IOpenApiAdapter>();

            _processor = new Mock<IStreamProcessor>();
            _processor
                .Setup(p => p.ProcessStreamAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>(), It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(), It.IsAny<int>()))
                .ReturnsAsync(new StreamCompletionResult { ContentResponse = "ok", StopReason = StreamStopReason.Completed, FinishReason = "stop" });

            _factory = new Mock<IStreamProcessorFactory>();
            _factory.Setup(f => f.Create(It.IsAny<string>())).Returns(_processor.Object);

            _modelsConfig = new Mock<IModelsConfigManager>();
            _modelsConfig
                .Setup(m => m.GetAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ModelsConfigFile { Models = new List<ModelDefinition>() });

            _history = new Mock<IChatHistoryManager>();
            _history.Setup(h => h.BuildUserMessagesWithHistory(It.IsAny<string>())).Returns(new List<ChatMessage>());

            Service = new StreamingRoundService(_adapter.Object, settingsManager.Object, _factory.Object, _modelsConfig.Object);
        }

        private StreamingRoundService Service { get; set; }

        private static StreamingResponse NewStreamingResponse()
        {
            return new StreamingResponse(new MemoryStream(), new HttpResponseMessage(), new HttpRequestMessage(), new StringContent(string.Empty));
        }

        private static ApiException RateLimited(double? hintSeconds, int status = 429, string type = null)
        {
            var info = new ApiErrorInfo
            {
                Message = "rate limited",
                Code = status,
                Type = type,
                RetryAfterSeconds = hintSeconds,
                RetryAfterSource = hintSeconds.HasValue ? RetryAfterSource.Header : RetryAfterSource.None
            };

            return new ApiException(info, status);
        }

        /// <summary>Fails the first call with the supplied exception, then succeeds. Returns the call counter.</summary>
        private int SetupFailThenSucceed(ApiException failure)
        {
            int calls = 0;

            _adapter
                .Setup(a => a.SendChatStreamingAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .Returns<MessageContext, ModelContext, CancellationToken>((context, model, token) =>
                {
                    calls++;
                    if (calls == 1)
                        throw failure;

                    return Task.FromResult(NewStreamingResponse());
                });

            return calls;
        }

        private int SetupAlwaysFailing(ApiException failure)
        {
            int calls = 0;

            _adapter
                .Setup(a => a.SendChatStreamingAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .Returns<MessageContext, ModelContext, CancellationToken>((context, model, token) =>
                {
                    Interlocked.Increment(ref calls);
                    throw failure;
                });

            return calls;
        }

        private StreamRoundRequest NewRequest(Func<RateLimitWaitInfo, Task> onRateLimitWait = null)
        {
            return new StreamRoundRequest
            {
                History = _history.Object,
                Prompt = "hello",
                ModelId = "model",
                OnRateLimitWait = onRateLimitWait
            };
        }

        [Test]
        public async Task RunAsync_HandlingDisabled_DoesNotRetry()
        {
            _settings.EnableRateLimitHandling = false;
            int calls = 0;

            _adapter
                .Setup(a => a.SendChatStreamingAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .Returns<MessageContext, ModelContext, CancellationToken>((context, model, token) =>
                {
                    calls++;
                    throw RateLimited(0);
                });

            var result = await Service.RunAsync(NewRequest(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(result.Outcome, Is.EqualTo(StreamRoundOutcome.Error));
        }

        [Test]
        public async Task RunAsync_RateLimitedThenSucceeds_CompletesAfterOneRetry()
        {
            SetupFailThenSucceed(RateLimited(0));

            var result = await Service.RunAsync(NewRequest(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(StreamRoundOutcome.Completed));
            _adapter.Verify(
                a => a.SendChatStreamingAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        [Test]
        public async Task RunAsync_StillRateLimitedOnRetry_GivesUpAfterTwoAttempts()
        {
            SetupAlwaysFailing(RateLimited(0));

            var result = await Service.RunAsync(NewRequest(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(StreamRoundOutcome.Error));
            _adapter.Verify(
                a => a.SendChatStreamingAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2), "the per-round budget is one retry, so the loop must not continue");
        }

        [Test]
        public async Task RunAsync_RateLimited_NotifiesBeginThenEnd()
        {
            SetupFailThenSucceed(RateLimited(0));

            var events = new List<RateLimitWaitInfo>();
            var result = await Service.RunAsync(
                NewRequest(info =>
                {
                    lock (events)
                    {
                        events.Add(info);
                    }

                    return Task.CompletedTask;
                }),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(StreamRoundOutcome.Completed));
            Assert.That(events, Has.Count.EqualTo(2));
            Assert.That(events[0].Phase, Is.EqualTo(RateLimitWaitPhase.Begin));
            Assert.That(events[1].Phase, Is.EqualTo(RateLimitWaitPhase.End));
            Assert.That(events[0].WaitId, Is.EqualTo(events[1].WaitId), "both phases must belong to the same wait");
            Assert.That(events[0].Attempt, Is.EqualTo(2));
            Assert.That(events[0].MaxAttempts, Is.EqualTo(2));
        }

        [Test]
        public async Task RunAsync_NullCallback_StillRetries()
        {
            // The SubAgent path passes no callback and must keep retrying silently.
            SetupFailThenSucceed(RateLimited(0));

            var result = await Service.RunAsync(NewRequest(null), CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(StreamRoundOutcome.Completed));
            _adapter.Verify(
                a => a.SendChatStreamingAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        [Test]
        public async Task RunAsync_HintAboveCap_ReturnsOriginalErrorWithoutRetry()
        {
            // The policy cap is 300 s, so a 600 s hint is never waited out.
            SetupAlwaysFailing(RateLimited(600));

            var result = await Service.RunAsync(NewRequest(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(StreamRoundOutcome.Error));
            Assert.That(result.Stream.ErrorMessage, Is.EqualTo("rate limited"));
            _adapter.Verify(
                a => a.SendChatStreamingAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()),
                Times.Once, "a hint beyond the cap means the request is not retried at all");
        }

        [Test]
        public async Task RunAsync_NonRateLimitError_IsNotRetried()
        {
            SetupAlwaysFailing(new ApiException(new ApiErrorInfo { Message = "bad request", Code = 400 }, 400));

            var result = await Service.RunAsync(NewRequest(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(StreamRoundOutcome.Error));
            _adapter.Verify(
                a => a.SendChatStreamingAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task RunAsync_503WithRetryAfterOnly_IsNotRetried()
        {
            SetupAlwaysFailing(RateLimited(30, status: 503));

            var result = await Service.RunAsync(NewRequest(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(StreamRoundOutcome.Error));
            _adapter.Verify(
                a => a.SendChatStreamingAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task RunAsync_ThrowingCallback_DoesNotBreakTheRetry()
        {
            SetupFailThenSucceed(RateLimited(0));

            var result = await Service.RunAsync(
                NewRequest(info =>
                {
                    throw new InvalidOperationException("UI is gone");
                }),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(StreamRoundOutcome.Completed));
        }

        [Test]
        public Task RunAsync_CancelledDuringWait_ThrowsAndStillNotifiesEnd()
        {
            // The 30 s hint fits under the policy cap, so the wait is planned and can be cancelled.
            SetupFailThenSucceed(RateLimited(30));

            var events = new List<RateLimitWaitInfo>();
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(TimeSpan.FromMilliseconds(150));

                Assert.ThrowsAsync<TaskCanceledException>(async () => await Service.RunAsync(
                    NewRequest(info =>
                    {
                        lock (events)
                        {
                            events.Add(info);
                        }

                        return Task.CompletedTask;
                    }),
                    cts.Token).ConfigureAwait(false));
            }

            Assert.That(events, Has.Count.EqualTo(2), "the End notification is best effort even when the wait is cancelled");
            Assert.That(events[1].Phase, Is.EqualTo(RateLimitWaitPhase.End));

            return Task.CompletedTask;
        }

        [Test]
        public Task RunAsync_ProcessorThrowsApiException_IsNotRetried()
        {
            // Regression on the retry boundary: once the stream is accepted, an API error from the processor is
            // reported as-is, because a second request would duplicate the chunks already streamed to the UI.
            _adapter
                .Setup(a => a.SendChatStreamingAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => NewStreamingResponse());

            _processor
                .Setup(p => p.ProcessStreamAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>(), It.IsAny<Func<TextStreamChunk, TokenGenerationStats, Task>>(), It.IsAny<int>()))
                .ThrowsAsync(RateLimited(0));

            Assert.ThrowsAsync<ApiException>(async () => await Service.RunAsync(NewRequest(), CancellationToken.None).ConfigureAwait(false));

            _adapter.Verify(
                a => a.SendChatStreamingAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()),
                Times.Once);

            return Task.CompletedTask;
        }
    }
}
