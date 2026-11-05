using System.Collections.Generic;
using System.Threading;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Chat;
using LMLocal.Core.Exceptions;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Responses;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    /// <summary>
    /// Covers the summary call of a forced compaction: it must carry an output budget, and a summary that does not fit must be retried once.
    /// </summary>
    [TestFixture]
    public class HistoryCompactorSummaryInputTests
    {
        private Mock<IChatHistoryManager> _history;
        private Mock<ISettingsManager> _settings;

        [SetUp]
        public void SetUp()
        {
            _history = new Mock<IChatHistoryManager>();
            _settings = new Mock<ISettingsManager>();
            _settings.Setup(s => s.Current).Returns(new AppSettings { EnableHistoryCompaction = true });
        }

        private HistoryCompactor CreateCompactor(Mock<IOpenApiAdapter> client)
        {
            var activeModelContext = new Mock<IActiveModelContext>();
            activeModelContext.SetupGet(a => a.MaxContextLength).Returns(16384);
            return new HistoryCompactor(_history.Object, client.Object, _settings.Object, activeModelContext.Object);
        }

        [Test]
        public void CompactForced_PassesAnOutputBudgetToTheSummaryCall()
        {
            var maxTokens = new List<int?>();
            var client = SummarizerReturning("summary text", maxTokens: maxTokens);

            var history = new List<ChatMessage>
            {
                new ChatMessage("user", "first"),
                new ChatMessage("assistant", "answer"),
                new ChatMessage("user", "retry prompt")
            };
            _history.Setup(h => h.GetHistoryCopy()).Returns(history);

            var (outcome, _) = CreateCompactor(client)
                .CompactForcedAsync("m", 0, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.Success));
            Assert.That(maxTokens.Count, Is.EqualTo(1));
            Assert.That(maxTokens[0], Is.Not.Null);
            Assert.That(maxTokens[0].Value, Is.GreaterThan(0));
        }

        [Test]
        public void CompactForced_SummaryOverflows_RetriesOnceWithSmallerInput()
        {
            var inputs = new List<string>();
            int attempt = 0;

            var client = new Mock<IOpenApiAdapter>();
            client
                .Setup(c => c.SendChatAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .Returns<MessageContext, ModelContext, CancellationToken>((ctx, model, ct) =>
                {
                    inputs.Add((string)ctx.Input[1].Content);
                    attempt++;

                    if (attempt == 1)
                    {
                        throw new ApiException(
                            new ApiErrorInfo { Message = "prompt is too long", RawCode = "context_length_exceeded" },
                            400);
                    }

                    return System.Threading.Tasks.Task.FromResult(new SendChatResponse
                    {
                        Choices = new List<ChatChoice>
                        {
                            new ChatChoice { Message = new AssistantMessage { Content = "summary text" } }
                        }
                    });
                });

            var history = new List<ChatMessage>
            {
                new ChatMessage("user", "first"),
                new ChatMessage("assistant", "answer"),
                new ChatMessage("user", "retry prompt")
            };
            _history.Setup(h => h.GetHistoryCopy()).Returns(history);

            var (outcome, _) = CreateCompactor(client)
                .CompactForcedAsync("m", 0, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.Success), "the retry succeeded");
            Assert.That(inputs.Count, Is.EqualTo(2), "exactly one retry");
            Assert.That(inputs[1].Length, Is.LessThanOrEqualTo(inputs[0].Length));
        }

        [Test]
        public void CompactForced_SummaryOverflowsTwice_ReturnsFailed()
        {
            int calls = 0;

            var client = new Mock<IOpenApiAdapter>();
            client
                .Setup(c => c.SendChatAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .Returns<MessageContext, ModelContext, CancellationToken>((ctx, model, ct) =>
                {
                    calls++;
                    throw new ApiException(
                        new ApiErrorInfo { Message = "prompt is too long", RawCode = "context_length_exceeded" },
                        400);
                });

            var history = new List<ChatMessage>
            {
                new ChatMessage("user", "first"),
                new ChatMessage("assistant", "answer"),
                new ChatMessage("user", "retry prompt")
            };
            _history.Setup(h => h.GetHistoryCopy()).Returns(history);

            var (outcome, messages) = CreateCompactor(client)
                .CompactForcedAsync("m", 0, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.Failed));
            Assert.That(messages, Is.Null);
            Assert.That(calls, Is.EqualTo(2), "one attempt plus exactly one retry, never a loop");
        }

        [Test]
        public void CompactForced_SummaryFailsForAnotherReason_DoesNotRetry()
        {
            int calls = 0;

            var client = new Mock<IOpenApiAdapter>();
            client
                .Setup(c => c.SendChatAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .Returns<MessageContext, ModelContext, CancellationToken>((ctx, model, ct) =>
                {
                    calls++;
                    // 401 is auth: halving the input cannot help, so it must not be retried.
                    throw new ApiException(new ApiErrorInfo { Message = "Invalid API key" }, 401);
                });

            var history = new List<ChatMessage>
            {
                new ChatMessage("user", "first"),
                new ChatMessage("assistant", "answer"),
                new ChatMessage("user", "retry prompt")
            };
            _history.Setup(h => h.GetHistoryCopy()).Returns(history);

            var (outcome, _) = CreateCompactor(client)
                .CompactForcedAsync("m", 0, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.Failed));
            Assert.That(calls, Is.EqualTo(1));
        }

        private static Mock<IOpenApiAdapter> SummarizerReturning(string summary, List<int?> maxTokens = null)
        {
            var client = new Mock<IOpenApiAdapter>();
            client
                .Setup(c => c.SendChatAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .Returns<MessageContext, ModelContext, CancellationToken>((ctx, model, ct) =>
                {
                    maxTokens?.Add(model.MaxOutputTokens);
                    return System.Threading.Tasks.Task.FromResult(new SendChatResponse
                    {
                        Choices = new List<ChatChoice>
                        {
                            new ChatChoice { Message = new AssistantMessage { Content = summary } }
                        }
                    });
                });
            return client;
        }
    }
}
