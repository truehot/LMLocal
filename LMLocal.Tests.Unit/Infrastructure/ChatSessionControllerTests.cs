using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.WebView.Controllers;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class ChatSessionControllerTests
    {
        private Mock<IChatHistoryManager> _chatHistoryManagerMock;
        private ChatSessionController _controller;

        [SetUp]
        public void SetUp()
        {
            _chatHistoryManagerMock = new Mock<IChatHistoryManager>();
            _controller = new ChatSessionController(_chatHistoryManagerMock.Object);
        }

        [TearDown]
        public void TearDown()
        {
            _controller = null;
            _chatHistoryManagerMock = null;
        }

        [Test]
        public void Constructor_NullHistoryManager_Throws()
        {
            Assert.That(() => new ChatSessionController(null), Throws.ArgumentNullException);
        }

        [Test]
        public async Task GetLastChatSessionAsync_WhenMessagesExist_ReturnsJsonWithHasSessionTrue()
        {
            var toolCalls = new object[] { new { id = "call_1", type = "function" } };
            _chatHistoryManagerMock
                .Setup(h => h.LoadLastSessionAsync())
                .ReturnsAsync(new List<ChatMessage>
                {
                    new ChatMessage("user", "hello"),
                    new ChatMessage("assistant", "hi", toolCallId: null) { ToolCalls = toolCalls }
                });

            var json = await _controller.GetLastChatSessionAsync();
            var result = JObject.Parse(json);

            Assert.That((bool)result["hasSession"], Is.True);
            Assert.That(result["messages"], Has.Count.EqualTo(2));
            Assert.That((string)result["messages"][0]["role"], Is.EqualTo("user"));
            Assert.That((string)result["messages"][0]["content"], Is.EqualTo("hello"));
            Assert.That((string)result["messages"][1]["role"], Is.EqualTo("assistant"));
            Assert.That((string)result["messages"][1]["content"], Is.EqualTo("hi"));
            Assert.That((string)result["messages"][1]["toolCalls"][0]["id"], Is.EqualTo("call_1"));
        }

        [Test]
        public async Task GetLastChatSessionAsync_WhenNoMessages_ReturnsJsonWithHasSessionFalse()
        {
            _chatHistoryManagerMock
                .Setup(h => h.LoadLastSessionAsync())
                .ReturnsAsync(new List<ChatMessage>());

            var json = await _controller.GetLastChatSessionAsync();
            var result = JObject.Parse(json);

            Assert.That((bool)result["hasSession"], Is.False);
            Assert.That(result["messages"], Has.Count.EqualTo(0));
        }

        [Test]
        public async Task GetLastChatSessionAsync_WhenLoadThrows_ReturnsEmptyJson()
        {
            _chatHistoryManagerMock
                .Setup(h => h.LoadLastSessionAsync())
                .ThrowsAsync(new InvalidOperationException("boom"));

            var json = await _controller.GetLastChatSessionAsync();
            var result = JObject.Parse(json);

            Assert.That((bool)result["hasSession"], Is.False);
            Assert.That(result["messages"], Has.Count.EqualTo(0));
        }

        [Test]
        public async Task GetChatSessionsAsync_ReturnsJsonWithSessions()
        {
            _chatHistoryManagerMock
                .Setup(h => h.GetChatSessionsAsync(It.IsAny<int>()))
                .ReturnsAsync(new List<ChatSessionSummary>
                {
                    new ChatSessionSummary { SessionId = "s1", Prompt = "hello", Timestamp = "2025-01-01T00:00:00Z", MessageCount = 2 }
                });

            var json = await _controller.GetChatSessionsAsync();
            var result = JObject.Parse(json);

            Assert.That(result["sessions"], Has.Count.EqualTo(1));
            Assert.That((string)result["sessions"][0]["sessionId"], Is.EqualTo("s1"));
            Assert.That((string)result["sessions"][0]["prompt"], Is.EqualTo("hello"));
            Assert.That((int)result["sessions"][0]["messageCount"], Is.EqualTo(2));
        }

        [Test]
        public async Task GetChatSessionsAsync_WhenThrows_ReturnsEmptyJson()
        {
            _chatHistoryManagerMock
                .Setup(h => h.GetChatSessionsAsync(It.IsAny<int>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var json = await _controller.GetChatSessionsAsync();
            var result = JObject.Parse(json);

            Assert.That(result["sessions"], Has.Count.EqualTo(0));
        }

        [Test]
        public async Task GetChatSessionByIdAsync_ReturnsMessages()
        {
            _chatHistoryManagerMock
                .Setup(h => h.LoadSessionByIdAsync("s1"))
                .ReturnsAsync(new List<ChatMessage>
                {
                    new ChatMessage("user", "q"),
                    new ChatMessage("assistant", "a")
                });

            var json = await _controller.GetChatSessionByIdAsync("s1");
            var result = JObject.Parse(json);

            Assert.That((bool)result["hasSession"], Is.True);
            Assert.That(result["messages"], Has.Count.EqualTo(2));
            Assert.That((string)result["messages"][0]["content"], Is.EqualTo("q"));
        }

        [Test]
        public async Task GetChatSessionByIdAsync_WhenNullId_ReturnsEmptyJson()
        {
            var json = await _controller.GetChatSessionByIdAsync(null);
            var result = JObject.Parse(json);

            Assert.That((bool)result["hasSession"], Is.False);
            Assert.That(result["messages"], Has.Count.EqualTo(0));
        }

        [Test]
        public async Task GetChatSessionByIdAsync_WhenThrows_ReturnsEmptyJson()
        {
            _chatHistoryManagerMock
                .Setup(h => h.LoadSessionByIdAsync("s1"))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var json = await _controller.GetChatSessionByIdAsync("s1");
            var result = JObject.Parse(json);

            Assert.That((bool)result["hasSession"], Is.False);
            Assert.That(result["messages"], Has.Count.EqualTo(0));
        }
    }
}
