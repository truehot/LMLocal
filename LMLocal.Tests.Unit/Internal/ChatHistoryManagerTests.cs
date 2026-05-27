using LMLocal.Application.Chat;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Settings;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class ChatHistoryManagerTests
    {
        [Test]
        public void AddUserMessage_AddsMessageToHistory()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("hello");
            var history = manager.GetHistoryCopy();

            Assert.That(history.Count, Is.EqualTo(1));
            Assert.That(history[0].Role, Is.EqualTo("user"));
            Assert.That(history[0].Content, Is.EqualTo("hello"));
        }

        [Test]
        public void AddAssistantToolRequestMessage_AddsToolCallsAndSaves()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            var mockPersistence = new Mock<IChatPersistenceService>();

            var manager = new ChatHistoryManager(mockSettings.Object, mockPersistence.Object);

            var toolCalls = new System.Collections.Generic.List<ToolCallRecord>
            {
                new ToolCallRecord { CallId = "c1", FunctionName = "f1", ArgumentsJson = "{}" }
            };

            manager.AddAssistantToolRequestMessage(toolCalls);

            var history = manager.GetHistoryCopy();
            Assert.That(history.Count, Is.EqualTo(1));
            Assert.That(history[0].Role, Is.EqualTo("assistant"));
            Assert.That(history[0].ToolCalls, Is.Not.Null);

            mockPersistence.Verify(p => p.SaveLastMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }

        [Test]
        public void ReplaceHistory_ReturnsFalse_WhenSizeMismatch()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("a");

            var result = manager.ReplaceHistory("summary", new System.Collections.Generic.List<ChatMessage> { new ChatMessage("user", "recent") }, expectedSize: 0);

            Assert.That(result, Is.False);
            Assert.That(manager.GetHistoryCopy().Count, Is.EqualTo(1));
        }

        [Test]
        public void ReplaceHistory_Replaces_WhenSizeMatches()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("a");
            manager.AddAssistantMessage("b");

            var recent = new System.Collections.Generic.List<ChatMessage> { new ChatMessage("user", "recent") };
            var result = manager.ReplaceHistory("summary", recent, expectedSize: 2);

            Assert.That(result, Is.True);
            var hist = manager.GetHistoryCopy();
            Assert.That(hist.Count, Is.EqualTo(2));
            Assert.That(hist[0].Role, Is.EqualTo("assistant"));
            Assert.That(hist[0].Content, Is.EqualTo("summary"));
        }

        [Test]
        public void AddAssistantMessage_DoesNotAddEmpty()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddAssistantMessage("");
            var history = manager.GetHistoryCopy();

            Assert.That(history.Count, Is.EqualTo(0));
        }

        [Test]
        public void AddAssistantMessage_CallsPersistenceService()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            var mockPersistence = new Mock<IChatPersistenceService>();
            var manager = new ChatHistoryManager(mockSettings.Object, mockPersistence.Object);

            manager.AddUserMessage("hello");
            manager.AddAssistantMessage("response");

            mockPersistence.Verify(p => p.SaveLastMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<System.Threading.CancellationToken>()), Times.Exactly(2));
        }

        [Test]
        public void Constructor_WithNullPersistence_ThrowsArgumentNullException()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            Assert.Throws<System.ArgumentNullException>(() => new ChatHistoryManager(mockSettings.Object, null));
        }

        [Test]
        public void AddUserMessage_WithDynamicSettings_UsesCurrentCompressionSetting()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableHistoryCompression = true });
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("**bold**");
            var history = manager.GetHistoryCopy();

            Assert.That(history[0].Content, Does.Not.Contain("**"));
        }

        [Test]
        public void AddAssistantMessage_WithDynamicSettings_UsesCurrentCompressionSetting()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableHistoryCompression = true });
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddAssistantMessage("**bold**");
            var history = manager.GetHistoryCopy();

            Assert.That(history[0].Content, Does.Not.Contain("**"));
        }

        [Test]
        public void Clear_RemovesAllMessages()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);
            manager.AddUserMessage("a");
            manager.AddAssistantMessage("b");

            manager.Clear();
            var history = manager.GetHistoryCopy();

            Assert.That(history.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// Integration test: Verifies ChatHistoryManager can be instantiated with dependencies.
        /// Validates that ISettingsManager dependency injection works correctly.
        /// </summary>
        [Test]
        public void DependencyInjection_ChatHistoryManager_CreatesSuccessfullyWithDependencies()
        {
            // Arrange
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("Test system prompt");

            // Act
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            // Assert
            Assert.That(manager, Is.Not.Null);
            Assert.That(manager, Is.InstanceOf<ChatHistoryManager>());
        }
    }
}
