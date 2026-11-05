using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Chat;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi.Requests;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class ChatHistoryManagerClearTests
    {
        private static Mock<ISettingsManager> CreateSettingsMock()
        {
            var mock = new Mock<ISettingsManager>();
            mock.Setup(s => s.SystemPrompt).Returns("sys");
            mock.Setup(s => s.Current).Returns(new AppSettings());
            return mock;
        }

        private static void SetupCompletedPersistence(Mock<IChatPersistenceService> mockPersistence)
        {
            mockPersistence
                .Setup(p => p.StartSessionWithMessagesAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
        }

        [Test]
        public async Task MoveLastExchangeToNewSessionAsync_KeepsOnlyLastExchange()
        {
            var mockSettings = CreateSettingsMock();
            var mockPersistence = new Mock<IChatPersistenceService>();
            SetupCompletedPersistence(mockPersistence);

            var manager = new ChatHistoryManager(mockSettings.Object, mockPersistence.Object);

            manager.AddUserMessage("first");
            manager.AddAssistantMessage("first response");
            manager.AddUserMessage("last");
            manager.AddAssistantMessage("last response");

            await manager.MoveLastExchangeToNewSessionAsync();

            var history = manager.GetHistoryCopy();
            Assert.That(history.Count, Is.EqualTo(2));
            Assert.That(history[0].Role, Is.EqualTo("user"));
            Assert.That(history[0].Content, Is.EqualTo("last"));
            Assert.That(history[1].Role, Is.EqualTo("assistant"));
            Assert.That(history[1].Content, Is.EqualTo("last response"));

            mockPersistence.Verify(
                p => p.StartSessionWithMessagesAsync(
                    It.Is<IReadOnlyList<ChatMessage>>(m => m.Count == 2),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ConsolidateLastExchangeAsync_WithToolResults_ConsolidatesIntoUserMessage()
        {
            var mockSettings = CreateSettingsMock();
            var mockPersistence = new Mock<IChatPersistenceService>();
            SetupCompletedPersistence(mockPersistence);

            var mockFormatter = new Mock<IToolResultMarkdownFormatter>();
            mockFormatter
                .Setup(f => f.FormatToolResults(It.IsAny<IEnumerable<(string FunctionName, string JsonContent)>>()))
                .Returns("FORMATTED_TOOL_RESULTS");

            var manager = new ChatHistoryManager(mockSettings.Object, mockPersistence.Object, mockFormatter.Object);

            manager.AddUserMessage("read file");
            manager.AddAssistantMessage("using tool", new List<ToolCallRecord>
            {
                new ToolCallRecord { CallId = "c1", FunctionName = "read_file_lines", ArgumentsJson = "{}" }
            });
            manager.AddToolExecutionResultMessages(new[]
            {
                new ChatMessage("tool", "{\"success\":true}", "c1")
            });
            manager.AddAssistantMessage("final answer");

            await manager.ConsolidateLastExchangeAsync();

            var history = manager.GetHistoryCopy();
            Assert.That(history.Count, Is.EqualTo(2));
            Assert.That(history[0].Role, Is.EqualTo("user"));
            Assert.That(history[0].Content as string, Does.Contain("read file"));
            Assert.That(history[0].Content as string, Does.Contain("FORMATTED_TOOL_RESULTS"));
            Assert.That(history[1].Role, Is.EqualTo("assistant"));
            Assert.That(history[1].Content, Is.EqualTo("final answer"));
        }

        [Test]
        public async Task MoveLastExchangeToNewSession_AwaitsAtomicSessionWrite()
        {
            var mockSettings = CreateSettingsMock();
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            IReadOnlyList<ChatMessage> written = null;

            var mockPersistence = new Mock<IChatPersistenceService>();
            mockPersistence
                .Setup(p => p.StartSessionWithMessagesAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<ChatMessage>, CancellationToken>((messages, _) => written = messages)
                .Returns(gate.Task);

            var manager = new ChatHistoryManager(mockSettings.Object, mockPersistence.Object);
            manager.AddUserMessage("last");
            manager.AddAssistantMessage("response");

            var moveTask = manager.MoveLastExchangeToNewSessionAsync();

            await Task.Delay(50);

            Assert.That(moveTask.IsCompleted, Is.False,
                "The session write must be awaited, not fire-and-forget");
            mockPersistence.Verify(
                p => p.MarkNewSessionAsync(It.IsAny<CancellationToken>()),
                Times.Never,
                "The marker belongs to the atomic session write; it must not be written as a separate operation");

            gate.SetResult(true);
            await moveTask;

            Assert.That(written, Is.Not.Null);
            Assert.That(written.Count, Is.EqualTo(2));
            Assert.That(written[0].Content, Is.EqualTo("last"));
            Assert.That(written[1].Content, Is.EqualTo("response"));
        }

        [Test]
        public async Task ConsolidateLastExchange_AwaitsAtomicSessionWrite()
        {
            var mockSettings = CreateSettingsMock();
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var mockPersistence = new Mock<IChatPersistenceService>();
            mockPersistence
                .Setup(p => p.StartSessionWithMessagesAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
                .Returns(gate.Task);

            var manager = new ChatHistoryManager(mockSettings.Object, mockPersistence.Object);
            manager.AddUserMessage("last");
            manager.AddAssistantMessage("response");

            var consolidateTask = manager.ConsolidateLastExchangeAsync();

            await Task.Delay(50);

            Assert.That(consolidateTask.IsCompleted, Is.False,
                "The session write must be awaited, not fire-and-forget");
            mockPersistence.Verify(
                p => p.MarkNewSessionAsync(It.IsAny<CancellationToken>()),
                Times.Never,
                "The marker belongs to the atomic session write; it must not be written as a separate operation");

            gate.SetResult(true);
            await consolidateTask;
        }

        [Test]
        public async Task ClearAndSaveMessagesAsync_PersistsProvidedMessages()
        {
            var mockSettings = CreateSettingsMock();
            var mockPersistence = new Mock<IChatPersistenceService>();
            SetupCompletedPersistence(mockPersistence);

            var manager = new ChatHistoryManager(mockSettings.Object, mockPersistence.Object);

            var messages = new List<ChatMessage>
            {
                new ChatMessage("user", "summary prompt"),
                new ChatMessage("assistant", "summary response")
            };

            await manager.ClearAndSaveMessagesAsync(messages);

            var history = manager.GetHistoryCopy();
            Assert.That(history.Count, Is.EqualTo(2));
            Assert.That(history[0].Role, Is.EqualTo("user"));
            Assert.That(history[0].Content, Is.EqualTo("summary prompt"));
            Assert.That(history[1].Role, Is.EqualTo("assistant"));
            Assert.That(history[1].Content, Is.EqualTo("summary response"));

            // Marker + messages are written by a single atomic call: a half-written session cannot appear.
            mockPersistence.Verify(
                p => p.StartSessionWithMessagesAsync(
                    It.Is<IReadOnlyList<ChatMessage>>(m =>
                        m.Count == 2
                        && (string)m[0].Content == "summary prompt"
                        && (string)m[1].Content == "summary response"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            mockPersistence.Verify(
                p => p.MarkNewSessionAsync(It.IsAny<CancellationToken>()),
                Times.Never);
            mockPersistence.Verify(
                p => p.SaveMessagesAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ClearAndSaveMessagesAsync_WithNoMessages_StillStartsNewSession()
        {
            var mockSettings = CreateSettingsMock();
            var mockPersistence = new Mock<IChatPersistenceService>();
            SetupCompletedPersistence(mockPersistence);

            var manager = new ChatHistoryManager(mockSettings.Object, mockPersistence.Object);
            manager.AddUserMessage("stale");
            manager.AddAssistantMessage("stale response");

            await manager.ClearAndSaveMessagesAsync(new List<ChatMessage>());

            Assert.That(manager.GetHistoryCopy().Count, Is.EqualTo(0));

            // An empty payload still rotates the session: the marker alone is the boundary.
            mockPersistence.Verify(
                p => p.StartSessionWithMessagesAsync(
                    It.Is<IReadOnlyList<ChatMessage>>(m => m.Count == 0),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ClearAndSaveMessagesAsync_AwaitsAtomicSessionWrite()
        {
            var mockSettings = CreateSettingsMock();
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var mockPersistence = new Mock<IChatPersistenceService>();
            mockPersistence
                .Setup(p => p.StartSessionWithMessagesAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
                .Returns(gate.Task);

            var manager = new ChatHistoryManager(mockSettings.Object, mockPersistence.Object);

            var task = manager.ClearAndSaveMessagesAsync(new List<ChatMessage>
            {
                new ChatMessage("user", "summary prompt"),
                new ChatMessage("assistant", "summary response")
            });

            await Task.Delay(50);

            Assert.That(task.IsCompleted, Is.False,
                "The session write must be awaited, not fire-and-forget");
            mockPersistence.Verify(
                p => p.MarkNewSessionAsync(It.IsAny<CancellationToken>()),
                Times.Never);

            gate.SetResult(true);
            await task;
        }
    }
}
