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
    /// Contract of <see cref="HistoryCompactor.CompactForcedAsync"/>: it returns the compacted history instead of
    /// applying it, it prunes before it summarizes, and it never loses the tool cycle the retry still has to answer.
    /// </summary>
    [TestFixture]
    public class HistoryCompactorForcedTests
    {
        private Mock<IChatHistoryManager> _history;
        private Mock<IOpenApiAdapter> _client;
        private Mock<ISettingsManager> _settings;
        private List<ChatMessage> _captured;
        private string _capturedSystemPrompt;

        [SetUp]
        public void SetUp()
        {
            _history = new Mock<IChatHistoryManager>();
            _client = new Mock<IOpenApiAdapter>();
            _settings = new Mock<ISettingsManager>();
            _settings.Setup(s => s.Current).Returns(new AppSettings { EnableHistoryCompaction = true });

            _captured = null;
            _capturedSystemPrompt = null;

            _client
                .Setup(c => c.SendChatAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .Callback<MessageContext, ModelContext, CancellationToken>((ctx, model, ct) =>
                {
                    var systemMessage = ctx?.Input?.FirstOrDefault(m => m.Role == "system");
                    _capturedSystemPrompt = systemMessage?.Content as string;
                })
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

        /// <summary>
        /// Runs a forced compaction and keeps the returned messages for assertions.
        /// </summary>
        private (CompactionOutcome Outcome, List<ChatMessage> Messages) Run(int attempt = 0, CancellationToken ct = default)
        {
            var (outcome, messages) = CreateCompactor().CompactForcedAsync("m", attempt, ct).GetAwaiter().GetResult();
            _captured = messages?.ToList();
            return (outcome, _captured);
        }

        /// <summary>
        /// Builds history with the given number of completed steps, each "user + assistant with text".
        /// </summary>
        private static List<ChatMessage> Steps(int count)
        {
            var messages = new List<ChatMessage>();
            for (int i = 1; i <= count; i++)
            {
                messages.Add(new ChatMessage("user", "prompt " + i));
                messages.Add(new ChatMessage("assistant", "answer " + i));
            }
            return messages;
        }

        private static ChatMessage AssistantWithToolCalls(string callId, string arguments = "{}")
        {
            return new ChatMessage("assistant", null)
            {
                ToolCalls = new List<ToolCall>
                {
                    new ToolCall
                    {
                        Id = callId,
                        Type = "function",
                        Function = new FunctionCallDetails { Name = "read_file_lines", Arguments = arguments }
                    }
                }
            };
        }

        [Test]
        public void CompactForced_GateDisabled_ReturnsFailed()
        {
            _settings.Setup(s => s.Current).Returns(new AppSettings { EnableHistoryCompaction = false });
            _history.Setup(h => h.GetHistoryCopy()).Returns(Steps(5));

            var (outcome, messages) = Run();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.Failed));
            Assert.That(messages, Is.Null);
        }

        [Test]
        public void CompactForced_EmptyHistory_ReturnsNothingChanged()
        {
            _history.Setup(h => h.GetHistoryCopy()).Returns(new List<ChatMessage>());

            var (outcome, _) = Run();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.NothingChanged));
        }

        [TestCase(10)]
        [TestCase(4)]
        [TestCase(3)]
        [TestCase(2)]
        public void CompactForced_SummarizesThePast_AndPinsThePrompt(int totalSteps)
        {
            var history = Steps(totalSteps);
            // The retry prompt is the last message, as it is after a failed round.
            history.Add(new ChatMessage("user", "retry prompt"));
            _history.Setup(h => h.GetHistoryCopy()).Returns(history);

            var (outcome, _) = Run();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.Success));
            Assert.That(_captured, Is.Not.Null);

            // The prompt is pinned: the past is summarized, the prompt stays verbatim right after the summary,
            // and there is no current cycle in this history to keep.
            Assert.That(_captured.Count, Is.EqualTo(3), "history must be [placeholder, summary, prompt]");
            Assert.That(_captured[0].Role, Is.EqualTo("user"));
            Assert.That(_captured[1].Role, Is.EqualTo("assistant"));
            Assert.That(_captured[1].Content as string, Is.EqualTo("summary text"));
            Assert.That(_captured[2].Role, Is.EqualTo("user"));
            Assert.That(_captured[2].Content as string, Is.EqualTo("retry prompt"),
                "the prompt must never be summarized away");
        }

        [Test]
        public void CompactForced_KeepsCurrentToolCycle_Verbatim()
        {
            // The retry still has to answer the current prompt, so the rejected cycle must survive the compaction:
            // dropping it is what made the model forget what it had already read and changed.
            var history = Steps(3);
            history.Add(new ChatMessage("user", "retry prompt"));
            history.Add(AssistantWithToolCalls("c1"));
            history.Add(new ChatMessage("tool", "file content", "c1"));
            _history.Setup(h => h.GetHistoryCopy()).Returns(history);

            var (outcome, messages) = Run();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.Success));
            Assert.That(messages, Is.Not.Null);

            Assert.That(messages.Any(m => m.Role == "user" && (string)m.Content == "retry prompt"), Is.True,
                "the prompt must be kept verbatim");
            Assert.That(messages[messages.Count - 1].Role, Is.EqualTo("tool"),
                "the current cycle must be kept verbatim");
            Assert.That(messages.Count(m => m.Role == "tool"), Is.EqualTo(1));
        }

        [Test]
        public void CompactForced_NoCompletedSteps_ReturnsNothingChanged_AndKeepsCycle()
        {
            // Nothing before the prompt can be summarized. Truncating to the prompt instead would destroy the
            // work in progress, so the outcome must be honest rather than silently lossy.
            var history = new List<ChatMessage>
            {
                new ChatMessage("user", "retry prompt"),
                AssistantWithToolCalls("c1"),
                new ChatMessage("tool", "file content", "c1")
            };
            _history.Setup(h => h.GetHistoryCopy()).Returns(history);

            var (outcome, messages) = Run();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.NothingChanged));
            Assert.That(messages, Is.Null, "nothing may be applied when nothing was compacted");
            _client.Verify(c => c.SendChatAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public void CompactForced_NoCompletedStepsAndNothingToCut_ReturnsNothingChanged()
        {
            _history.Setup(h => h.GetHistoryCopy()).Returns(new List<ChatMessage> { new ChatMessage("user", "retry prompt") });

            var (outcome, _) = Run();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.NothingChanged));
        }

        [Test]
        public void CompactForced_LastAttempt_SummarizesEverything_KeepingOnlyLastUserMessage()
        {
            var history = Steps(4);
            history.Add(new ChatMessage("user", "retry prompt"));
            _history.Setup(h => h.GetHistoryCopy()).Returns(history);

            var (outcome, _) = Run(attempt: 1);

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.Success));
            Assert.That(_captured, Is.Not.Null);
            Assert.That(_captured.Count, Is.EqualTo(3), "placeholder + summary + last user message");
            Assert.That(_captured[2].Content as string, Is.EqualTo("retry prompt"));
        }

        [Test]
        public void CompactForced_EmptySummary_ReturnsFailed()
        {
            _client
                .Setup(c => c.SendChatAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SendChatResponse
                {
                    Choices = new List<ChatChoice>
                    {
                        new ChatChoice { Message = new AssistantMessage { Content = "   " } }
                    }
                });
            _history.Setup(h => h.GetHistoryCopy()).Returns(Steps(5));

            var (outcome, messages) = Run();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.Failed));
            Assert.That(messages, Is.Null);
        }

        [Test]
        public void CompactForced_SummarizerThrows_ReturnsFailed()
        {
            _client
                .Setup(c => c.SendChatAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new System.InvalidOperationException("provider down"));
            _history.Setup(h => h.GetHistoryCopy()).Returns(Steps(5));

            var (outcome, _) = Run();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.Failed));
        }

        [Test]
        public void CompactForced_UsesForcedPrompt_NotEndOfSessionPrompt()
        {
            _history.Setup(h => h.GetHistoryCopy()).Returns(Steps(5));

            Run();

            Assert.That(_capturedSystemPrompt, Is.Not.Null);
            Assert.That(_capturedSystemPrompt, Does.Not.Contain("new session"));
            Assert.That(_capturedSystemPrompt, Does.Not.Contain("Copy all code blocks"));
            Assert.That(_capturedSystemPrompt, Does.Contain("same session"));
        }

        [Test]
        public void CompactForced_NeverTouchesHistoryOrDisk()
        {
            _history.Setup(h => h.GetHistoryCopy()).Returns(Steps(5));

            Run();

            _history.Verify(h => h.ReplaceHistoryInMemory(It.IsAny<IEnumerable<ChatMessage>>()), Times.Never,
                "the compactor returns history, the caller applies it");
            _history.Verify(
                h => h.ReplaceHistoryAndPersistAsync(It.IsAny<string>(), It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<int>()),
                Times.Never);
        }

        [Test]
        public void CompactForced_Cancelled_ReturnsCancelled()
        {
            _history.Setup(h => h.GetHistoryCopy()).Returns(Steps(5));

            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                var (outcome, _) = Run(ct: cts.Token);

                Assert.That(outcome, Is.EqualTo(CompactionOutcome.Cancelled));
            }
        }
    }
}
