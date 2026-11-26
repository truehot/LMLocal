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
            var toolCalls = new List<ChatToolCall>
            {
                new ChatToolCall { Id = "call_1", Name = "read_file", Arguments = "{}" }
            };
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
        public async Task GetCurrentChatHistoryAsync_WhenMemoryHasMessages_ReturnsJsonWithHasSessionTrue()
        {
            _chatHistoryManagerMock
                .Setup(h => h.GetHistoryCopy())
                .Returns(new List<ChatMessage>
                {
                    new ChatMessage("user", "hello"),
                    new ChatMessage("assistant", "hi")
                });

            var json = await _controller.GetCurrentChatHistoryAsync();
            var result = JObject.Parse(json);

            Assert.That((bool)result["hasSession"], Is.True);
            Assert.That(result["messages"], Has.Count.EqualTo(2));
            Assert.That((string)result["messages"][0]["role"], Is.EqualTo("user"));
            Assert.That((string)result["messages"][0]["content"], Is.EqualTo("hello"));

            // Memory-only path must not touch persistence.
            _chatHistoryManagerMock.Verify(h => h.LoadLastSessionAsync(), Times.Never);
        }

        [Test]
        public async Task GetCurrentChatHistoryAsync_WhenMemoryEmpty_ReturnsHasSessionFalse()
        {
            _chatHistoryManagerMock
                .Setup(h => h.GetHistoryCopy())
                .Returns(new List<ChatMessage>());

            var json = await _controller.GetCurrentChatHistoryAsync();
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

        [Test]
        public async Task GetLastChatSessionAsync_MessageWithModel_ReturnsModelId()
        {
            _chatHistoryManagerMock
                .Setup(h => h.LoadLastSessionAsync())
                .ReturnsAsync(new List<ChatMessage>
                {
                    new ChatMessage("user", "hello"),
                    new ChatMessage("assistant", "hi") { ModelId = "qwen3-coder" }
                });

            var json = await _controller.GetLastChatSessionAsync();
            var result = JObject.Parse(json);

            Assert.That((string)result["messages"][1]["modelId"], Is.EqualTo("qwen3-coder"));
            Assert.That(result["messages"][0]["modelId"], Is.Null, "user messages carry no model");
        }

        [Test]
        public async Task GetLastChatSessionAsync_MessageWithoutModel_OmitsModelId()
        {
            _chatHistoryManagerMock
                .Setup(h => h.LoadLastSessionAsync())
                .ReturnsAsync(new List<ChatMessage> { new ChatMessage("assistant", "legacy answer") });

            var json = await _controller.GetLastChatSessionAsync();
            var message = (JObject)JObject.Parse(json)["messages"][0];

            Assert.That(message.ContainsKey("modelId"), Is.False,
                "an unknown model must not be serialized at all, so legacy logs keep the old payload shape");
        }

        [Test]
        public async Task GetChatSessionsAsync_ReturnsSessionModels()
        {
            _chatHistoryManagerMock
                .Setup(h => h.GetChatSessionsAsync(It.IsAny<int>()))
                .ReturnsAsync(new List<ChatSessionSummary>
                {
                    new ChatSessionSummary
                    {
                        SessionId = "s1",
                        Prompt = "hello",
                        Timestamp = "2025-01-01T00:00:00Z",
                        MessageCount = 2,
                        Models = new List<string> { "alpha", "beta" }
                    },
                    new ChatSessionSummary
                    {
                        SessionId = "s2",
                        Prompt = "legacy",
                        Timestamp = "2025-01-01T00:00:00Z",
                        MessageCount = 1
                    }
                });

            var json = await _controller.GetChatSessionsAsync();
            var result = JObject.Parse(json);

            Assert.That(result["sessions"][0]["models"].ToObject<List<string>>(), Is.EqualTo(new[] { "alpha", "beta" }));
            Assert.That(result["sessions"][1]["models"].ToObject<List<string>>(), Is.Empty,
                "sessions without model information must still serialize an empty list");
        }

        // ================ Golden UI payload: toolCalls wire shape (expected JSON must not change) ================

        private async Task<string> GetLastSessionJsonAsync(List<ChatMessage> messages)
        {
            _chatHistoryManagerMock
                .Setup(h => h.LoadLastSessionAsync())
                .ReturnsAsync(messages);

            return await _controller.GetLastChatSessionAsync();
        }

        private static void AssertPayload(string expectedJson, string actualJson)
        {
            var expected = JToken.Parse(expectedJson);
            var actual = JToken.Parse(actualJson);

            Assert.That(JToken.DeepEquals(expected, actual), Is.True,
                "session payload changed" + Environment.NewLine +
                "expected: " + expected.ToString() + Environment.NewLine +
                "actual:   " + actual.ToString());
        }

        [Test]
        public async Task GetLastChatSessionAsync_GoldenPayload_MessageWithoutToolCalls()
        {
            var json = await GetLastSessionJsonAsync(new List<ChatMessage>
            {
                new ChatMessage("user", "hello")
            });

            AssertPayload(@"{
                ""hasSession"": true,
                ""messages"": [
                    { ""role"": ""user"", ""content"": ""hello"", ""toolCallId"": null, ""toolCalls"": null }
                ]
            }", json);
        }

        [Test]
        public async Task GetLastChatSessionAsync_GoldenPayload_ToolCallWithoutExtraContent()
        {
            var json = await GetLastSessionJsonAsync(new List<ChatMessage>
            {
                new ChatMessage("assistant", null)
                {
                    ToolCalls = new List<ChatToolCall>
                    {
                        new ChatToolCall
                        {
                            Id = "call_1",
                            Name = "read_file",
                            Arguments = "{\"path\":\"a.cs\"}"
                        }
                    }
                }
            });

            AssertPayload(@"{
                ""hasSession"": true,
                ""messages"": [
                    {
                        ""role"": ""assistant"",
                        ""content"": null,
                        ""toolCallId"": null,
                        ""toolCalls"": [
                            {
                                ""id"": ""call_1"",
                                ""type"": ""function"",
                                ""function"": { ""name"": ""read_file"", ""arguments"": ""{\""path\"":\""a.cs\""}"" }
                            }
                        ]
                    }
                ]
            }", json);
        }

        [Test]
        public async Task GetLastChatSessionAsync_GoldenPayload_ToolCallWithExtraContent()
        {
            var json = await GetLastSessionJsonAsync(new List<ChatMessage>
            {
                new ChatMessage("assistant", null)
                {
                    ToolCalls = new List<ChatToolCall>
                    {
                        new ChatToolCall
                        {
                            Id = "call_1",
                            Name = "check_flight",
                            Arguments = "{\"flight\":\"AA100\"}",
                            ExtraContentJson = "{\"google\":{\"thought_signature\":\"sigA\"}}"
                        }
                    }
                }
            });

            AssertPayload(@"{
                ""hasSession"": true,
                ""messages"": [
                    {
                        ""role"": ""assistant"",
                        ""content"": null,
                        ""toolCallId"": null,
                        ""toolCalls"": [
                            {
                                ""id"": ""call_1"",
                                ""type"": ""function"",
                                ""function"": { ""name"": ""check_flight"", ""arguments"": ""{\""flight\"":\""AA100\""}"" },
                                ""extra_content"": { ""google"": { ""thought_signature"": ""sigA"" } }
                            }
                        ]
                    }
                ]
            }", json);
        }

        [Test]
        public async Task GetLastChatSessionAsync_GoldenPayload_TwoToolCallsKeepOrder()
        {
            var json = await GetLastSessionJsonAsync(new List<ChatMessage>
            {
                new ChatMessage("assistant", null)
                {
                    ToolCalls = new List<ChatToolCall>
                    {
                        new ChatToolCall
                        {
                            Id = "call_1",
                            Name = "read_file",
                            Arguments = "{\"path\":\"a.cs\"}"
                        },
                        new ChatToolCall
                        {
                            Id = "call_2",
                            Name = "list_dir",
                            Arguments = "{\"path\":\"src\"}"
                        }
                    }
                }
            });

            AssertPayload(@"{
                ""hasSession"": true,
                ""messages"": [
                    {
                        ""role"": ""assistant"",
                        ""content"": null,
                        ""toolCallId"": null,
                        ""toolCalls"": [
                            {
                                ""id"": ""call_1"",
                                ""type"": ""function"",
                                ""function"": { ""name"": ""read_file"", ""arguments"": ""{\""path\"":\""a.cs\""}"" }
                            },
                            {
                                ""id"": ""call_2"",
                                ""type"": ""function"",
                                ""function"": { ""name"": ""list_dir"", ""arguments"": ""{\""path\"":\""src\""}"" }
                            }
                        ]
                    }
                ]
            }", json);
        }
    }
}
