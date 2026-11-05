using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Chat;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi.Requests;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    /// <summary>
    /// A failed round can commit a truncated answer. The retry does not add the prompt again, so that answer has to be
    /// removed first, otherwise the model continues it instead of answering the prompt.
    /// </summary>
    [TestFixture]
    public class ChatHistoryManagerFailedRoundTests
    {
        private static ChatHistoryManager CreateManager()
        {
            var settings = new Mock<ISettingsManager>();
            settings.Setup(s => s.SystemPrompt).Returns("sys");
            settings.Setup(s => s.Current).Returns(new AppSettings());

            var persistence = new Mock<IChatPersistenceService>();
            persistence
                .Setup(p => p.SaveLastMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            persistence
                .Setup(p => p.SaveMessagesAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            return new ChatHistoryManager(settings.Object, persistence.Object);
        }

        [Test]
        public void DiscardLastAssistantFromFailedRound_RemovesTheTruncatedAnswer()
        {
            var manager = CreateManager();
            manager.AddUserMessage("the prompt");
            manager.AddAssistantMessage("half an ans");

            manager.DiscardLastAssistantFromFailedRound();

            var history = manager.GetHistoryCopy();
            Assert.That(history.Count, Is.EqualTo(1));
            Assert.That(history[0].Role, Is.EqualTo("user"));
            Assert.That((string)history[0].Content, Is.EqualTo("the prompt"));
        }

        [Test]
        public void DiscardLastAssistantFromFailedRound_KeepsTheUserPrompt_WhenNothingWasCommitted()
        {
            // An overflow that arrives before the stream started commits no assistant message at all.
            var manager = CreateManager();
            manager.AddUserMessage("the prompt");

            manager.DiscardLastAssistantFromFailedRound();

            Assert.That(manager.GetHistoryCopy().Count, Is.EqualTo(1));
        }

        [Test]
        public void DiscardLastAssistantFromFailedRound_KeepsToolResults()
        {
            // The last message here is a tool result from a round that completed before the failure; it is not part
            // of the failed answer and must survive.
            var manager = CreateManager();
            manager.AddUserMessage("the prompt");
            manager.AddAssistantMessage(null, new List<ToolCallRecord>
            {
                new ToolCallRecord { CallId = "c1", FunctionName = "read_file_lines", ArgumentsJson = "{}" }
            });
            manager.AddToolExecutionResultMessages(new[] { new ChatMessage("tool", "file body", "c1") });

            manager.DiscardLastAssistantFromFailedRound();

            var history = manager.GetHistoryCopy();
            Assert.That(history.Count, Is.EqualTo(3));
            Assert.That(history[history.Count - 1].Role, Is.EqualTo("tool"));
        }

        [Test]
        public void DiscardLastAssistantFromFailedRound_DropsPendingAssistant()
        {
            var manager = CreateManager();
            manager.AddUserMessage("the prompt");
            manager.SetPendingAssistant("about to call a tool", new List<ToolCallRecord>
            {
                new ToolCallRecord { CallId = "c1", FunctionName = "read_file_lines", ArgumentsJson = "{}" }
            });

            manager.DiscardLastAssistantFromFailedRound();
            manager.AddToolExecutionResultMessages(new[] { new ChatMessage("tool", "body", "c1") });

            var history = manager.GetHistoryCopy();
            Assert.That(history.Any(m => m.Role == "assistant"), Is.False,
                "a pending assistant from a failed round must not be committed by the next round");
        }

        [Test]
        public void DiscardLastAssistantFromFailedRound_OnEmptyHistory_DoesNothing()
        {
            var manager = CreateManager();

            manager.DiscardLastAssistantFromFailedRound();

            Assert.That(manager.GetHistoryCopy().Count, Is.EqualTo(0));
        }
    }
}
