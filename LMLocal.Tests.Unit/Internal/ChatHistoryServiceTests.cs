using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Core.Models;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class ChatHistoryServiceTests
    {
        private static ChatHistoryService CreateService(
            Mock<IChatHistoryManager> historyManager,
            Mock<IHistoryCompactor> compactor,
            Mock<ISessionManager> session)
        {
            return new ChatHistoryService(historyManager.Object, compactor.Object, session.Object);
        }

        [Test]
        public async Task ResetHistory_WhenSessionRunning_ReturnsFalse()
        {
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();
            var mockSession = new Mock<ISessionManager>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(true);

            var service = CreateService(mockHistoryManager, mockCompactor, mockSession);

            var reset = await service.ResetHistoryAsync("none").ConfigureAwait(false);

            Assert.That(reset, Is.False);
            mockHistoryManager.Verify(h => h.Clear(), Times.Never);
        }

        [Test]
        public async Task ResetHistory_Default_ClearsHistory()
        {
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();
            var mockSession = new Mock<ISessionManager>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);

            var service = CreateService(mockHistoryManager, mockCompactor, mockSession);

            var reset = await service.ResetHistoryAsync("none").ConfigureAwait(false);

            Assert.That(reset, Is.True);
            mockHistoryManager.Verify(h => h.Clear(), Times.Once);
        }

        [Test]
        public async Task ResetHistory_LastPrompt_CallsMoveLastExchange()
        {
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();
            var mockSession = new Mock<ISessionManager>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);

            var service = CreateService(mockHistoryManager, mockCompactor, mockSession);

            var reset = await service.ResetHistoryAsync("last-prompt").ConfigureAwait(false);

            Assert.That(reset, Is.True);
            mockHistoryManager.Verify(h => h.MoveLastExchangeToNewSessionAsync(), Times.Once);
            mockHistoryManager.Verify(h => h.Clear(), Times.Never);
        }

        [Test]
        public async Task ResetHistory_LastExchange_CallsConsolidateLastExchange()
        {
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();
            var mockSession = new Mock<ISessionManager>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);

            var service = CreateService(mockHistoryManager, mockCompactor, mockSession);

            var reset = await service.ResetHistoryAsync("last-exchange").ConfigureAwait(false);

            Assert.That(reset, Is.True);
            mockHistoryManager.Verify(h => h.ConsolidateLastExchangeAsync(), Times.Once);
            mockHistoryManager.Verify(h => h.Clear(), Times.Never);
        }

        [Test]
        public async Task SummarizeAndCompact_SessionRunning_ReturnsFalse()
        {
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();
            var mockSession = new Mock<ISessionManager>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(true);

            var service = CreateService(mockHistoryManager, mockCompactor, mockSession);

            var result = await service.SummarizeAndCompactAsync("model1").ConfigureAwait(false);

            Assert.That(result, Is.False);
        }

        [Test]
        public async Task SummarizeAndCompact_NoModel_ReturnsFalse()
        {
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();
            var mockSession = new Mock<ISessionManager>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);

            var service = CreateService(mockHistoryManager, mockCompactor, mockSession);

            var result = await service.SummarizeAndCompactAsync(null).ConfigureAwait(false);

            Assert.That(result, Is.False);
        }

        [Test]
        public async Task SummarizeAndCompact_EmptyHistory_ReturnsTrue()
        {
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();
            var mockSession = new Mock<ISessionManager>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);
            mockHistoryManager.Setup(h => h.GetHistoryCopy()).Returns(new List<ChatMessage>());

            var service = CreateService(mockHistoryManager, mockCompactor, mockSession);

            var result = await service.SummarizeAndCompactAsync("model1").ConfigureAwait(false);

            Assert.That(result, Is.True);
            mockHistoryManager.Verify(h => h.Clear(), Times.Once);
        }

        [Test]
        public async Task SummarizeAndCompact_Success_AddsPair()
        {
            var mockHistoryManager = new Mock<IChatHistoryManager>();
            var mockCompactor = new Mock<IHistoryCompactor>();
            var mockSession = new Mock<ISessionManager>();

            mockSession.SetupGet(s => s.IsSessionRunning).Returns(false);

            mockHistoryManager.Setup(h => h.GetHistoryCopy()).Returns(new List<ChatMessage>
            {
                new ChatMessage("user", "hello"),
                new ChatMessage("assistant", "hi")
            });

            mockCompactor.Setup(c => c.SummarizeAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), "model1", It.IsAny<CancellationToken>()))
                .ReturnsAsync("compacted summary");

            var service = CreateService(mockHistoryManager, mockCompactor, mockSession);

            var result = await service.SummarizeAndCompactAsync("model1").ConfigureAwait(false);

            Assert.That(result, Is.True);
            mockHistoryManager.Verify(h => h.ClearAndSaveMessagesAsync(
                It.Is<IEnumerable<ChatMessage>>(messages =>
                    messages != null &&
                    messages.Count() == 2 &&
                    messages.ElementAt(0).Role == "user" &&
                    messages.ElementAt(1).Role == "assistant" &&
                    (string)messages.ElementAt(1).Content == "compacted summary")),
                Times.Once);
        }
    }
}
