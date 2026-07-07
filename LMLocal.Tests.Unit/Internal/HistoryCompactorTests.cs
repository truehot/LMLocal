using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LMLocal.Application.Chat;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Responses;
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

            var snapshot = new System.Collections.Generic.List<ChatMessage>();
            for (int i = 0; i < 50; i++)
            {
                snapshot.Add(new ChatMessage("user", "message " + i));
            }

            var mockHistory = new Mock<IChatHistoryManager>();
            mockHistory.Setup(h => h.GetHistoryCopy()).Returns(snapshot);

            var mockClient = new Mock<IOpenApiAdapter>();
            var mockActiveModelContext = new Mock<IActiveModelContext>();
            mockActiveModelContext.SetupGet(a => a.MaxContextLength).Returns(100);

            var response = new SendChatResponse
            {
                Choices = new System.Collections.Generic.List<ChatChoice>
                {
                    new ChatChoice { Message = new AssistantMessage { Content = "summary content" } }
                }
            };

            mockClient.Setup(c => c.SendChatAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(response);
            mockHistory.Setup(h => h.ReplaceHistory(It.IsAny<string>(), It.IsAny<System.Collections.Generic.IEnumerable<ChatMessage>>(), It.IsAny<int>())).Returns(true).Verifiable();

            var compactor = new HistoryCompactor(mockHistory.Object, mockClient.Object, mockSettings.Object, mockActiveModelContext.Object);
            compactor.CompactIfNeededAsync("m", CancellationToken.None).GetAwaiter().GetResult();

            mockHistory.Verify(h => h.ReplaceHistory(
                It.Is<string>(s => s == "summary content"),
                It.Is<System.Collections.Generic.IEnumerable<ChatMessage>>(r => r.Count() == 10),
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

        [Test]
        public void SummarizeAsync_NullOrEmptyHistory_ReturnsNull()
        {
            var mockSettings = new Mock<ISettingsManager>();
            var mockClient = new Mock<IOpenApiAdapter>();
            var mockActiveModelContext = new Mock<IActiveModelContext>();
            mockActiveModelContext.SetupGet(a => a.MaxContextLength).Returns(16384);
            var compactor = new HistoryCompactor(
                new Mock<IChatHistoryManager>().Object,
                mockClient.Object, mockSettings.Object, mockActiveModelContext.Object);

            var r1 = compactor.SummarizeAsync(null, "m", CancellationToken.None).GetAwaiter().GetResult();
            var r2 = compactor.SummarizeAsync(new List<ChatMessage>(), "m", CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(r1, Is.Null);
            Assert.That(r2, Is.Null);
        }

        [Test]
        public void SummarizeAsync_Success_ReturnsSummary()
        {
            var mockSettings = new Mock<ISettingsManager>();
            var mockActiveModelContext = new Mock<IActiveModelContext>();
            mockActiveModelContext.SetupGet(a => a.MaxContextLength).Returns(16384);

            var mockClient = new Mock<IOpenApiAdapter>();
            mockClient.Setup(c => c.SendChatAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SendChatResponse
                {
                    Choices = new List<ChatChoice>
                    {
                        new ChatChoice { Message = new AssistantMessage { Content = "summary text" } }
                    }
                });

            var history = new List<ChatMessage>
            {
                new ChatMessage("user", "hello"),
                new ChatMessage("assistant", "hi")
            };

            var compactor = new HistoryCompactor(
                new Mock<IChatHistoryManager>().Object,
                mockClient.Object, mockSettings.Object, mockActiveModelContext.Object);

            var result = compactor.SummarizeAsync(history, "m", CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result, Is.EqualTo("summary text"));
        }

        [Test]
        public void SummarizeAsync_SkipsToolsAndEmptyAssistant()
        {
            var mockSettings = new Mock<ISettingsManager>();
            var mockActiveModelContext = new Mock<IActiveModelContext>();
            mockActiveModelContext.SetupGet(a => a.MaxContextLength).Returns(16384);

            MessageContext captured = null;
            var mockClient = new Mock<IOpenApiAdapter>();
            mockClient.Setup(c => c.SendChatAsync(It.IsAny<MessageContext>(), It.IsAny<ModelContext>(), It.IsAny<CancellationToken>()))
                .Callback<MessageContext, ModelContext, CancellationToken>((ctx, mc, ct) => captured = ctx)
                .ReturnsAsync(new SendChatResponse
                {
                    Choices = new List<ChatChoice>
                    {
                        new ChatChoice { Message = new AssistantMessage { Content = "ok" } }
                    }
                });

            var history = new List<ChatMessage>
            {
                new ChatMessage("user", "hello"),
                new ChatMessage("assistant", null) { ToolCalls = new List<ToolCall>() },
                new ChatMessage("tool", "{}", "c1"),
                new ChatMessage("assistant", "answer"),
            };

            var compactor = new HistoryCompactor(
                new Mock<IChatHistoryManager>().Object,
                mockClient.Object, mockSettings.Object, mockActiveModelContext.Object);

            compactor.SummarizeAsync(history, "m", CancellationToken.None).GetAwaiter().GetResult();

            var text = captured.Input[1].Content.ToString();
            Assert.That(text, Does.Contain("user: hello"));
            Assert.That(text, Does.Contain("assistant: answer"));
            Assert.That(text, Does.Not.Contain("tool"));
            Assert.That(text, Does.Not.Contain("{}"));
        }
    }
}