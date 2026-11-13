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
    /// Summary sits after a forced compaction. The prompt is pinned, the past goes in front of
    /// it, and in a history that is nothing but the current cycle the summary rides on the first protected assistant
    /// message, because two assistant messages in a row are rejected by strict llama.cpp templates.
    /// </summary>
    [TestFixture]
    public class HistoryCompactorPlacementTests
    {
        private Mock<IChatHistoryManager> _history;
        private Mock<ISettingsManager> _settings;
        private List<string> _summaryInputs;
        private List<string> _summaries;
        private int _maxContext;

        [SetUp]
        public void SetUp()
        {
            _history = new Mock<IChatHistoryManager>();
            _settings = new Mock<ISettingsManager>();
            _settings.Setup(s => s.Current).Returns(new AppSettings { EnableHistoryCompaction = true });

            _summaryInputs = new List<string>();
            _summaries = new List<string> { "summary text" };
            _maxContext = 16384;
        }

        private HistoryCompactor CreateCompactor()
        {
            var activeModelContext = new Mock<IActiveModelContext>();
            activeModelContext.SetupGet(a => a.MaxContextLength).Returns(_maxContext);

            var client = new Mock<IOpenApiAdapter>();
            client
                .Setup(c => c.SendChatAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .Returns<MessageContext, ModelContext, CancellationToken>((ctx, model, ct) =>
                {
                    _summaryInputs.Add((string)ctx.Input[1].Content);

                    string summary = _summaries[System.Math.Min(_summaryInputs.Count - 1, _summaries.Count - 1)];
                    return System.Threading.Tasks.Task.FromResult(new SendChatResponse
                    {
                        Choices = new List<ChatChoice>
                        {
                            new ChatChoice { Message = new AssistantMessage { Content = summary } }
                        }
                    });
                });

            return new HistoryCompactor(_history.Object, client.Object, _settings.Object, activeModelContext.Object);
        }

        private (CompactionOutcome Outcome, List<ChatMessage> Messages) Run(int attempt = 0, CancellationToken ct = default)
        {
            var (outcome, messages) = CreateCompactor()
                .CompactForcedAsync("m", attempt, ct)
                .GetAwaiter().GetResult();

            return (outcome, messages?.ToList());
        }

        private static ChatMessage Call(string id, string name, string arguments = "{}", string text = null)
        {
            return new ChatMessage("assistant", text)
            {
                ToolCalls = new List<ToolCall>
                {
                    new ToolCall
                    {
                        Id = id,
                        Type = "function",
                        Function = new FunctionCallDetails { Name = name, Arguments = arguments }
                    }
                }
            };
        }

        /// <summary>
        /// One prompt and a cycle of <paramref name="rounds"/> tool rounds; there is nothing before the prompt.
        /// </summary>
        private static List<ChatMessage> CyclicOnly(int rounds)
        {
            var messages = new List<ChatMessage> { new ChatMessage("user", "do the work") };

            for (int i = 1; i <= rounds; i++)
            {
                messages.Add(Call("c" + i, "read_file_lines", "{\"file_path\":\"src/A" + i + ".cs\"}"));
                messages.Add(new ChatMessage("tool", "body " + i, "c" + i));
            }

            return messages;
        }

        [Test]
        public void PastExists_SummarySitsInFrontOfThePinnedPrompt()
        {
            var history = new List<ChatMessage>
            {
                new ChatMessage("user", "first question"),
                new ChatMessage("assistant", "first answer"),
                new ChatMessage("user", "second question"),
                new ChatMessage("assistant", "second answer"),
                new ChatMessage("user", "the prompt"),
                Call("c1", "read_file_lines"),
                new ChatMessage("tool", "body", "c1")
            };
            _history.Setup(h => h.GetHistoryCopy()).Returns(history);

            var (outcome, messages) = Run();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.Success));
            Assert.That(messages[0].Role, Is.EqualTo("user"), "placeholder");
            Assert.That(messages[1].Role, Is.EqualTo("assistant"), "summary");
            Assert.That((string)messages[1].Content, Is.EqualTo("summary text"));
            Assert.That(messages[2].Role, Is.EqualTo("user"));
            Assert.That((string)messages[2].Content, Is.EqualTo("the prompt"), "the prompt is pinned right after the summary");
            Assert.That(messages[3].Role, Is.EqualTo("assistant"), "the current cycle follows verbatim");
        }

        [Test]
        public void PromptIsNeverPartOfTheSummaryInput()
        {
            var history = new List<ChatMessage>
            {
                new ChatMessage("user", "old question"),
                new ChatMessage("assistant", "old answer"),
                new ChatMessage("user", "UNIQUE-PROMPT-MARKER"),
                Call("c1", "read_file_lines"),
                new ChatMessage("tool", "body", "c1")
            };
            _history.Setup(h => h.GetHistoryCopy()).Returns(history);

            Run();

            Assert.That(_summaryInputs.Count, Is.GreaterThan(0));
            foreach (var input in _summaryInputs)
                Assert.That(input, Does.Not.Contain("UNIQUE-PROMPT-MARKER"), "the prompt must never be summarized");
        }

        [Test]
        public void PromptAppearsExactlyOnceInTheResult()
        {
            var history = new List<ChatMessage>
            {
                new ChatMessage("user", "old question"),
                new ChatMessage("assistant", "old answer"),
                new ChatMessage("user", "prompt once"),
                Call("c1", "read_file_lines"),
                new ChatMessage("tool", "body", "c1")
            };
            _history.Setup(h => h.GetHistoryCopy()).Returns(history);

            var (_, messages) = Run();

            Assert.That(messages.Count(m => m.Role == "user" && (string)m.Content == "prompt once"), Is.EqualTo(1),
                "AddUserMessage is skipped on a retry, so a duplicated prompt would be sent twice");
        }

        [Test]
        public void NoPast_LongCycle_SummaryRidesOnFirstProtectedAssistant()
        {
            _history.Setup(h => h.GetHistoryCopy()).Returns(CyclicOnly(6));

            var (outcome, messages) = Run();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.Success));
            Assert.That(messages[0].Role, Is.EqualTo("user"));
            Assert.That((string)messages[0].Content, Is.EqualTo("do the work"), "the prompt stays first and verbatim");

            var firstAssistant = messages[1];
            Assert.That(firstAssistant.Role, Is.EqualTo("assistant"));
            Assert.That((string)firstAssistant.Content, Does.StartWith("Progress so far:"),
                "the cycle summary goes into the content of a tool-calling assistant message");
            Assert.That(firstAssistant.ToolCalls, Is.Not.Null, "that message must keep its tool calls");

            for (int i = 1; i < messages.Count - 1; i++)
            {
                Assert.That(!(messages[i].Role == "assistant" && messages[i + 1].Role == "assistant"),
                    $"two assistant messages in a row at {i} would be rejected by strict llama.cpp templates");
            }
        }

        [Test]
        public void NoPast_LongCycle_KeepsTheNewestRoundsVerbatim()
        {
            var history = CyclicOnly(6);
            _history.Setup(h => h.GetHistoryCopy()).Returns(history);

            var (_, messages) = Run();

            // The three newest rounds (KeepRecentRounds) stay verbatim; only the tool bodies of older ones are gone.
            var toolBodies = messages.Where(m => m.Role == "tool").Select(m => (string)m.Content).ToList();
            Assert.That(toolBodies, Does.Contain("body 4"));
            Assert.That(toolBodies, Does.Contain("body 5"));
            Assert.That(toolBodies, Does.Contain("body 6"));
        }

        [Test]
        public void NoPastAndNoEarlyRounds_ReportsNothingChanged()
        {
            // One prompt and a single short round: there is no earlier round to summarize and the newest round is
            // the work in progress, so truncating would drop it for no gain. Reporting Success here would retry an
            // unchanged history and fail with the same provider error again.
            _history.Setup(h => h.GetHistoryCopy()).Returns(CyclicOnly(1));

            var (outcome, messages) = Run();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.NothingChanged));
            Assert.That(messages, Is.Null);
            Assert.That(_summaryInputs, Is.Empty, "nothing could be summarized, so no LLM call must be spent");
        }

        [Test]
        public void LastAttempt_ProtectsOnlyTheNewestRound()
        {
            _history.Setup(h => h.GetHistoryCopy()).Returns(CyclicOnly(6));

            var (outcome, messages) = Run(attempt: 1);

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.Success));
            Assert.That(messages.Count(m => m.Role == "tool"), Is.LessThan(3),
                "the last attempt protects fewer rounds than the first one");
        }

        [Test]
        public void EmptySummary_Fails()
        {
            _summaries = new List<string> { "   " };
            _history.Setup(h => h.GetHistoryCopy()).Returns(CyclicOnly(6));

            var (outcome, messages) = Run();

            Assert.That(outcome, Is.EqualTo(CompactionOutcome.Failed));
            Assert.That(messages, Is.Null);
        }

        [Test]
        public void Cancelled_ReturnsCancelled()
        {
            _history.Setup(h => h.GetHistoryCopy()).Returns(CyclicOnly(6));

            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.That(Run(ct: cts.Token).Outcome, Is.EqualTo(CompactionOutcome.Cancelled));
            }
        }
    }
}
