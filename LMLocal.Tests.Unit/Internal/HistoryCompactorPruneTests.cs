using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Chat;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Requests;
using LMLocal.Infrastructure.LlmApi.Responses;
using Moq;
using NUnit.Framework;
using ToolCall = LMLocal.Infrastructure.LlmApi.Requests.ToolCall;

namespace LMLocal.Tests.Unit
{
    /// <summary>
    /// Covers the role of the prune pass in a forced compaction: it runs first and never spends an LLM call of its own,
    /// but it only replaces the summary when there is nothing left to summarize.
    /// </summary>
    [TestFixture]
    public class HistoryCompactorPruneTests
    {
        private Mock<IChatHistoryManager> _history;
        private Mock<IOpenApiAdapter> _client;
        private Mock<ISettingsManager> _settings;

        [SetUp]
        public void SetUp()
        {
            _history = new Mock<IChatHistoryManager>();
            _client = new Mock<IOpenApiAdapter>();
            _settings = new Mock<ISettingsManager>();
            _settings.Setup(s => s.Current).Returns(new AppSettings { EnableHistoryCompaction = true });

            _client
                .Setup(c => c.SendChatAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SendChatResponse
                {
                    Choices = new List<ChatChoice>
                    {
                        new ChatChoice { Message = new AssistantMessage { Content = "summary text" } }
                    }
                });
        }

        private HistoryCompactor CreateCompactor()
        {
            var activeModelContext = new Mock<IActiveModelContext>();
            activeModelContext.SetupGet(a => a.MaxContextLength).Returns(16384);
            return new HistoryCompactor(_history.Object, _client.Object, _settings.Object, activeModelContext.Object);
        }

        private static string LargeBody(int lines)
        {
            var line = new string('x', 100);
            return string.Join("\n", Enumerable.Range(0, lines).Select(_ => line));
        }

        private static List<ChatMessage> Round(int number, int bodyLines)
        {
            return new List<ChatMessage>
            {
                new ChatMessage("assistant", null)
                {
                    ToolCalls = new List<ToolCall>
                    {
                        new ToolCall
                        {
                            Id = "c" + number,
                            Type = "function",
                            Function = new FunctionCallDetails
                            {
                                Name = "read_file_lines",
                                Arguments = "{\"file_path\":\"src/A" + number + ".cs\"}"
                            }
                        }
                    }
                },
                new ChatMessage("tool", LargeBody(bodyLines), "c" + number)
            };
        }

        /// <summary>
        /// One prompt and a single tool round: the round is the work in progress, so there is nothing the summarizer
        /// could be given and pruning is the whole compaction.
        /// </summary>
        private static List<ChatMessage> SingleRoundOnly(int bodyLines)
        {
            var messages = new List<ChatMessage> { new ChatMessage("user", "do the work") };
            messages.AddRange(Round(1, bodyLines));
            return messages;
        }

        /// <summary>
        /// One prompt followed by several tool rounds, so the rounds older than the protected window can be summarized.
        /// </summary>
        private static List<ChatMessage> SeveralRounds(int rounds, int bodyLines)
        {
            var messages = new List<ChatMessage> { new ChatMessage("user", "do the work") };

            for (int i = 1; i <= rounds; i++)
                messages.AddRange(Round(i, bodyLines));

            return messages;
        }

        [Test]
        public void CompactForced_NothingToSummarize_PruneIsEnough_SucceedsWithoutCallingSummarizer()
        {
            var history = SingleRoundOnly(600);
            _history.Setup(h => h.GetHistoryCopy()).Returns(history);

            var (outcome, messages) = CreateCompactor()
                .CompactForcedAsync("m", 0, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.Success));
            Assert.That(messages, Is.Not.Null);
            Assert.That(messages.Count, Is.EqualTo(history.Count), "pruning never drops messages");

            _client.Verify(
                c => c.SendChatAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "there was nothing to summarize, so no LLM may be called");
        }

        [Test]
        public void CompactForced_PruneResult_KeepsThePromptAndTheToolCalls()
        {
            var history = SingleRoundOnly(600);
            _history.Setup(h => h.GetHistoryCopy()).Returns(history);

            var (outcome, messages) = CreateCompactor()
                .CompactForcedAsync("m", 0, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.Success));
            Assert.That(messages[0].Role, Is.EqualTo("user"));
            Assert.That((string)messages[0].Content, Is.EqualTo("do the work"));

            var callIds = messages
                .Where(m => m.ToolCalls is List<ToolCall>)
                .SelectMany(m => (List<ToolCall>)m.ToolCalls)
                .Select(c => c.Id)
                .ToList();
            Assert.That(callIds, Is.EqualTo(new[] { "c1" }), "the tool call keeps its id so the tool result still pairs with it");
        }

        [Test]
        public void CompactForced_PruneIsEnough_DoesNotApplyOrPersistHistory()
        {
            var history = SingleRoundOnly(600);
            _history.Setup(h => h.GetHistoryCopy()).Returns(history);

            CreateCompactor().CompactForcedAsync("m", 0, CancellationToken.None).GetAwaiter().GetResult();

            _history.Verify(h => h.ReplaceHistoryInMemory(It.IsAny<IEnumerable<ChatMessage>>()), Times.Never);
            _history.Verify(
                h => h.ReplaceHistoryAndPersistAsync(It.IsAny<string>(), It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<int>()),
                Times.Never);
        }

        [Test]
        public void CompactForced_PruneFits_StillSummarizesTheEarlierRounds()
        {
            // Four rounds: the three newest are protected, so round 1 can be summarized. Pruning alone would fit, but
            // leaving the whole cycle verbatim only postpones the next overflow to the following round.
            var history = SeveralRounds(4, 600);
            _history.Setup(h => h.GetHistoryCopy()).Returns(history);

            var (outcome, messages) = CreateCompactor()
                .CompactForcedAsync("m", 0, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.Success));
            Assert.That(messages, Is.Not.Null);

            _client.Verify(
                c => c.SendChatAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()),
                Times.Once,
                "pruning fitting the budget is not a reason to skip the summary");

            Assert.That(
                messages.Any(m => m.Role == "assistant"
                                  && ConversationSegmenter.HasToolCalls(m)
                                  && ((string)m.Content ?? string.Empty).StartsWith("Progress so far:")),
                Is.True,
                "the summary of the earlier rounds goes into the content of the first protected tool-calling message");
        }
    }
}
