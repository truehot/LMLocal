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
                .Setup(p => p.MarkNewSessionAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            mockPersistence
                .Setup(p => p.SaveMessagesAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
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
                p => p.SaveMessagesAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()),
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
        public async Task MoveLastExchangeToNewSessionAsync_WaitsForNewSessionMarkerBeforeSaving()
        {
            var mockSettings = CreateSettingsMock();
            var markerTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var mockPersistence = new Mock<IChatPersistenceService>();
            mockPersistence
                .Setup(p => p.MarkNewSessionAsync(It.IsAny<CancellationToken>()))
                .Returns(markerTcs.Task);
            mockPersistence
                .Setup(p => p.SaveMessagesAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var manager = new ChatHistoryManager(mockSettings.Object, mockPersistence.Object);
            manager.AddUserMessage("last");
            manager.AddAssistantMessage("response");

            var moveTask = manager.MoveLastExchangeToNewSessionAsync();

            await Task.Delay(50);

            mockPersistence.Verify(
                p => p.SaveMessagesAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "SaveMessagesAsync must not run until MarkNewSessionAsync completes");

            markerTcs.SetResult(true);
            await moveTask;
        }

        [Test]
        public async Task ConsolidateLastExchangeAsync_WaitsForNewSessionMarkerBeforeSaving()
        {
            var mockSettings = CreateSettingsMock();
            var markerTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var mockPersistence = new Mock<IChatPersistenceService>();
            mockPersistence
                .Setup(p => p.MarkNewSessionAsync(It.IsAny<CancellationToken>()))
                .Returns(markerTcs.Task);
            mockPersistence
                .Setup(p => p.SaveMessagesAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var manager = new ChatHistoryManager(mockSettings.Object, mockPersistence.Object);
            manager.AddUserMessage("last");
            manager.AddAssistantMessage("response");

            var consolidateTask = manager.ConsolidateLastExchangeAsync();

            await Task.Delay(50);

            mockPersistence.Verify(
                p => p.SaveMessagesAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "SaveMessagesAsync must not run until MarkNewSessionAsync completes");

            markerTcs.SetResult(true);
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

            mockPersistence.Verify(
                p => p.MarkNewSessionAsync(It.IsAny<CancellationToken>()),
                Times.Once);
            mockPersistence.Verify(
                p => p.SaveMessagesAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task ClearAndSaveMessagesAsync_WaitsForNewSessionMarkerBeforeSaving()
        {
            var mockSettings = CreateSettingsMock();
            var markerTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var mockPersistence = new Mock<IChatPersistenceService>();
            mockPersistence
                .Setup(p => p.MarkNewSessionAsync(It.IsAny<CancellationToken>()))
                .Returns(markerTcs.Task);
            mockPersistence
                .Setup(p => p.SaveMessagesAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var manager = new ChatHistoryManager(mockSettings.Object, mockPersistence.Object);

            var task = manager.ClearAndSaveMessagesAsync(new List<ChatMessage>
            {
                new ChatMessage("user", "summary prompt"),
                new ChatMessage("assistant", "summary response")
            });

            await Task.Delay(50);

            mockPersistence.Verify(
                p => p.SaveMessagesAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()),
                Times.Never,
                "SaveMessagesAsync must not run until MarkNewSessionAsync completes");

            markerTcs.SetResult(true);
            await task;
        }
    }
}


















