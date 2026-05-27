using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LMLocal.Application.Chat;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Api;
using LMLocal.Infrastructure.Api.Responses;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Settings;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class HistoryCompactorTests
    {
        [Test]
        public void NeedsCompaction_ReturnsTrue_WhenOverThreshold()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableHistoryCompaction = true });

            var history = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);
            for (int i = 0; i < 100; i++)
            {
                history.AddUserMessage(new string('a', 100));
            }

            var mockClient = new Mock<IOpenApiAdapter>();
            var mockActiveModelContext = new Mock<IActiveModelContext>();
            // Use a smaller max context so compaction threshold is exceeded by the test messages
            mockActiveModelContext.SetupGet(a => a.MaxContextLength).Returns(100);
            var compactor = new HistoryCompactor(history, mockClient.Object, mockSettings.Object, mockActiveModelContext.Object);

            var needs = compactor.NeedsCompaction();

            Assert.That(needs, Is.True);
        }

        [Test]
        public void CompactIfNeededAsync_ReplacesHistory_OnNonEmptySummary()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableHistoryCompaction = true });

            // Prepare a snapshot of history with many messages
            var snapshot = new System.Collections.Generic.List<ChatMessage>();
            for (int i = 0; i < 50; i++)
            {
                snapshot.Add(new ChatMessage("user", "message " + i));
            }

            var mockHistory = new Mock<IChatHistoryManager>();
            mockHistory.Setup(h => h.GetHistoryCopy()).Returns(snapshot);

            var mockClient = new Mock<IOpenApiAdapter>();
            var mockActiveModelContext = new Mock<IActiveModelContext>();
            // Use small max context to force compaction
            mockActiveModelContext.SetupGet(a => a.MaxContextLength).Returns(100);

            var response = new SendChatResponse
            {
                Choices = new System.Collections.Generic.List<ChatChoice>
                {
                    new ChatChoice { Message = new AssistantMessage { Content = "summary content" } }
                }
            };

            mockClient.Setup(c => c.SendChatAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(response);

            // Expect ReplaceHistory to be called with summary and recent messages
            mockHistory.Setup(h => h.ReplaceHistory(It.IsAny<string>(), It.IsAny<System.Collections.Generic.IEnumerable<ChatMessage>>(), It.IsAny<int>())).Returns(true).Verifiable();

            var compactor = new HistoryCompactor(mockHistory.Object, mockClient.Object, mockSettings.Object, mockActiveModelContext.Object);

            // run compaction
            compactor.CompactIfNeededAsync("m", CancellationToken.None).GetAwaiter().GetResult();

            // Verify ReplaceHistory was called with parsed summary
            mockHistory.Verify(h => h.ReplaceHistory(
                It.Is<string>(s => s == "summary content"),
                It.IsAny<System.Collections.Generic.IEnumerable<ChatMessage>>(),
                It.Is<int>(n => n == snapshot.Count)), Times.Once);
        }

        [Test]
        public void NeedsCompaction_ReturnsFalse_WhenBelowThreshold()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableHistoryCompaction = true });

            var history = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);
            history.AddUserMessage("short");
            var mockClient = new Mock<IOpenApiAdapter>();
            var mockActiveModelContext = new Mock<IActiveModelContext>();
            mockActiveModelContext.SetupGet(a => a.MaxContextLength).Returns(10000);
            var compactor = new HistoryCompactor(history, mockClient.Object, mockSettings.Object, mockActiveModelContext.Object);

            var needs = compactor.NeedsCompaction();

            Assert.That(needs, Is.False);
        }

        [Test]
        public void NeedsCompaction_WithDynamicSettings_ReadsCurrentSetting()
        {
            var mockHistory = new Mock<IChatHistoryManager>();
            mockHistory.Setup(h => h.GetHistoryCopy()).Returns(new List<ChatMessage>());
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableHistoryCompaction = true });

            var mockActiveModelContext = new Mock<IActiveModelContext>();
            mockActiveModelContext.SetupGet(a => a.MaxContextLength).Returns(100);
            var compactor = new HistoryCompactor(mockHistory.Object, new Mock<IOpenApiAdapter>().Object, mockSettings.Object, mockActiveModelContext.Object);

            var result = compactor.NeedsCompaction();

            Assert.That(result, Is.False);
        }

        [Test]
        public void NeedsCompaction_WithSettingsDisabled_ReturnsFalse()
        {
            var mockHistory = new Mock<IChatHistoryManager>();
            mockHistory.Setup(h => h.GetHistoryCopy()).Returns(
                System.Linq.Enumerable.Range(0, 100).Select(i => new ChatMessage("user", "x")).ToList()
            );
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableHistoryCompaction = false });

            var mockActiveModelContext = new Mock<IActiveModelContext>();
            mockActiveModelContext.SetupGet(a => a.MaxContextLength).Returns(100);
            var compactor = new HistoryCompactor(mockHistory.Object, new Mock<IOpenApiAdapter>().Object, mockSettings.Object, mockActiveModelContext.Object);

            var result = compactor.NeedsCompaction();

            Assert.That(result, Is.False);
        }
    }
}
