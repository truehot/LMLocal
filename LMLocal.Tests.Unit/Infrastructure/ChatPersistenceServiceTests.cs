using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi.Requests;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Settings;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class ChatPersistenceServiceTests
    {
        /// <summary>
        /// Creates a mock ISettingsManager with all required default properties configured.
        /// </summary>
        private Mock<ISettingsManager> CreateMockSettingsManager()
        {
            var mock = new Mock<ISettingsManager>();
            mock.Setup(s => s.LocalAppDataFolder).Returns("LMLocalChat");
            mock.Setup(s => s.ChatHistoryFolder).Returns("ChatHistory");
            mock.Setup(s => s.ChatHistoryFileLabel).Returns("chat_");
            return mock;
        }

        [Test]
        public async Task SaveLastMessageAsync_WhenFileExists_AppendsInsteadOfWrite()
        {
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = true });
            var mockFileSystem = new Mock<IFileSystem>();

            var service = new ChatPersistenceService(mockSettings.Object, mockFileSystem.Object);
            var message = new ChatMessage("user", "hello");

            mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(true);

            await service.SaveLastMessageAsync(message);

            mockFileSystem.Verify(fs => fs.AppendAllBytesAsync(
                It.Is<string>(p => p.Contains("chat_")),
                It.IsAny<byte[]>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task SaveLastMessageAsync_WhenLoggingDisabled_DoesNotWrite()
        {
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = false });
            var mockFileSystem = new Mock<IFileSystem>();

            var service = new ChatPersistenceService(mockSettings.Object, mockFileSystem.Object);
            var message = new ChatMessage("user", "test");

            await service.SaveLastMessageAsync(message);

            mockFileSystem.Verify(fs => fs.WriteAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
            mockFileSystem.Verify(fs => fs.AppendAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task SaveLastMessageAsync_WhenLoggingEnabled_WritesFile()
        {
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = true });
            var mockFileSystem = new Mock<IFileSystem>();

            var service = new ChatPersistenceService(mockSettings.Object, mockFileSystem.Object);
            var message = new ChatMessage("user", "hello");

            mockFileSystem.Setup(fs => fs.FileExists(It.IsAny<string>())).Returns(false);

            await service.SaveLastMessageAsync(message);

            mockFileSystem.Verify(fs => fs.WriteAllBytesAsync(
                It.Is<string>(p => p.Contains("chat_")), 
                It.IsAny<byte[]>(), 
                It.IsAny<CancellationToken>()), 
                Times.Once);
        }

        [Test]
        public async Task SaveLastMessageAsync_WithNullMessage_DoesNotWrite()
        {
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = true });
            var mockFileSystem = new Mock<IFileSystem>();

            var service = new ChatPersistenceService(mockSettings.Object, mockFileSystem.Object);
            ChatMessage message = null;

            await service.SaveLastMessageAsync(message);

            mockFileSystem.Verify(fs => fs.WriteAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
            mockFileSystem.Verify(fs => fs.AppendAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public void SaveChatAsync_CreatesDirectoryIfNotExists()
        {
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = true });
            var mockFileSystem = new Mock<IFileSystem>();

            new ChatPersistenceService(mockSettings.Object, mockFileSystem.Object);

            mockFileSystem.Verify(fs => fs.CreateDirectory(It.IsAny<string>()), Times.Once);
        }

        [Test]
        public async Task SaveLastMessageAsync_WithRealFileSystem_WritesValidJsonl()
        {
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = true });
            var fileSystem = new InMemoryFileSystem();

            var service = new ChatPersistenceService(mockSettings.Object, fileSystem);
            var message = new ChatMessage("user", "test message");

            await service.SaveLastMessageAsync(message);

            var files = fileSystem.GetAllFiles().ToList();
            Assert.That(files.Count, Is.GreaterThan(0));
            var content = fileSystem.ReadAllText(files.First());
            Assert.That(content, Does.Contain("test message"));
            Assert.That(content, Does.Contain("timestamp"));
        }

        /// <summary>
        /// Integration test: Verifies ChatPersistenceService can be instantiated with dependencies.
        /// Validates that ISettingsManager dependency injection works correctly.
        /// </summary>
        [Test]
        public void DependencyInjection_ChatPersistenceService_CreatesSuccessfullyWithDependencies()
        {
            // Arrange
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = false });

            // Act
            var service = new ChatPersistenceService(mockSettings.Object, new Mock<IFileSystem>().Object);

            Assert.That(service, Is.InstanceOf<ChatPersistenceService>());
        }

        // ------------------------------------------------------------------
        //  Session marker tests
        // ------------------------------------------------------------------

        [Test]
        public async Task MarkNewSessionAsync_WritesSessionStartMarker()
        {
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = true });
            var fileSystem = new InMemoryFileSystem();

            var service = new ChatPersistenceService(mockSettings.Object, fileSystem);
            await service.MarkNewSessionAsync();

            var files = fileSystem.GetAllFiles().ToList();
            Assert.That(files.Count, Is.GreaterThan(0));
            var content = fileSystem.ReadAllText(files.First());
            Assert.That(content, Does.Contain("\"type\":\"session_start\""));
            Assert.That(content, Does.Contain("\"session_id\""));
            Assert.That(content, Does.Contain("\"timestamp\""));
        }

        [Test]
        public async Task MarkNewSessionAsync_WhenLoggingDisabled_DoesNotWrite()
        {
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = false });
            var mockFileSystem = new Mock<IFileSystem>();

            var service = new ChatPersistenceService(mockSettings.Object, mockFileSystem.Object);
            await service.MarkNewSessionAsync();

            mockFileSystem.Verify(
                fs => fs.WriteAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()),
                Times.Never);
            mockFileSystem.Verify(
                fs => fs.AppendAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task SaveLastMessageAsync_AfterMarkNewSession_IncludesSessionId()
        {
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = true });
            var fileSystem = new InMemoryFileSystem();

            var service = new ChatPersistenceService(mockSettings.Object, fileSystem);
            await service.MarkNewSessionAsync();
            await service.SaveLastMessageAsync(new ChatMessage("user", "hello"));

            var files = fileSystem.GetAllFiles().ToList();
            var content = fileSystem.ReadAllText(files.First());
            Assert.That(content, Does.Contain("\"type\":\"message\""));
            Assert.That(content, Does.Contain("\"session_id\""));
            Assert.That(content, Does.Contain("\"role\":\"user\""));
            Assert.That(content, Does.Contain("\"content\":\"hello\""));
        }

        // ------------------------------------------------------------------
        //  LoadLastSession tests
        // ------------------------------------------------------------------

        [Test]
        public async Task LoadLastSessionAsync_WithNoSessions_ReturnsEmpty()
        {
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = true });
            var fileSystem = new InMemoryFileSystem();

            var service = new ChatPersistenceService(mockSettings.Object, fileSystem);
            var result = await service.LoadLastSessionAsync();

            Assert.That(result, Is.Empty);
        }

        [Test]
        public async Task LoadLastSessionAsync_WithSingleSession_ReturnsMessagesInOrder()
        {
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = true });
            var fileSystem = new InMemoryFileSystem();

            var service = new ChatPersistenceService(mockSettings.Object, fileSystem);

            // Simulate a session: start marker, user message, assistant message
            await service.MarkNewSessionAsync();
            await service.SaveLastMessageAsync(new ChatMessage("user", "question"));
            await service.SaveLastMessageAsync(new ChatMessage("assistant", "answer"));

            var result = await service.LoadLastSessionAsync();

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].Role, Is.EqualTo("user"));
            Assert.That(result[0].Content, Is.EqualTo("question"));
            Assert.That(result[1].Role, Is.EqualTo("assistant"));
            Assert.That(result[1].Content, Is.EqualTo("answer"));
        }

        [Test]
        public async Task LoadLastSessionAsync_WithMultipleSessions_ReturnsOnlyLast()
        {
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = true });
            var fileSystem = new InMemoryFileSystem();

            var service = new ChatPersistenceService(mockSettings.Object, fileSystem);

            // Session 1
            await service.MarkNewSessionAsync();
            await service.SaveLastMessageAsync(new ChatMessage("user", "old question"));
            await service.SaveLastMessageAsync(new ChatMessage("assistant", "old answer"));

            // Session 2 (last one — should be returned)
            await service.MarkNewSessionAsync();
            await service.SaveLastMessageAsync(new ChatMessage("user", "new question"));
            await service.SaveLastMessageAsync(new ChatMessage("assistant", "new answer"));
            await service.SaveLastMessageAsync(new ChatMessage("tool", "result", "call-1"));

            var result = await service.LoadLastSessionAsync();

            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0].Role, Is.EqualTo("user"));
            Assert.That(result[0].Content, Is.EqualTo("new question"));
            Assert.That(result[1].Role, Is.EqualTo("assistant"));
            Assert.That(result[1].Content, Is.EqualTo("new answer"));
            Assert.That(result[2].Role, Is.EqualTo("tool"));
            Assert.That(result[2].Content, Is.EqualTo("result"));
        }

        /// <summary>
        /// Regression: tool_calls were deserialized as JArray by ParseChatMessage,
        /// which caused the `as List&lt;ToolCall&gt;` cast in BuildRequest to return null.
        /// The API then rejected the request: "Messages with role 'tool' must be a
        /// response to a preceding message with 'tool_calls'".
        /// </summary>
        [Test]
        public async Task SaveAndLoad_ToolCalls_Roundtrip_PreservesType()
        {
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = true });
            var fileSystem = new InMemoryFileSystem();

            var service = new ChatPersistenceService(mockSettings.Object, fileSystem);

            await service.MarkNewSessionAsync();

            // Build a ChatMessage with ToolCalls — exactly how ChatHistoryManager.AddAssistantMessage does it
            var toolCalls = new List<ToolCall>
            {
                new ToolCall
                {
                    Id = "call_1",
                    Type = "function",
                    Function = new FunctionCallDetails { Name = "search", Arguments = "{\"query\":\"test\"}" }
                }
            };
            var assistantMsg = new ChatMessage("assistant", "using tools") { ToolCalls = toolCalls };
            await service.SaveLastMessageAsync(assistantMsg);

            // Load back
            var loaded = await service.LoadLastSessionAsync();

            Assert.That(loaded.Count, Is.EqualTo(1));
            Assert.That(loaded[0].Role, Is.EqualTo("assistant"));
            Assert.That(loaded[0].Content, Is.EqualTo("using tools"));

            // Critical: ToolCalls must be List<ToolCall>, not JArray
            Assert.That(loaded[0].ToolCalls, Is.InstanceOf<List<ToolCall>>());
            var loadedTc = loaded[0].ToolCalls as List<ToolCall>;
            Assert.That(loadedTc.Count, Is.EqualTo(1));
            Assert.That(loadedTc[0].Id, Is.EqualTo("call_1"));
            Assert.That(loadedTc[0].Function.Name, Is.EqualTo("search"));
        }


        [Test]
        public async Task LoadLastSessionAsync_WithSessionStartButNoMessages_ReturnsEmpty()
        {
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = true });
            var fileSystem = new InMemoryFileSystem();

            var service = new ChatPersistenceService(mockSettings.Object, fileSystem);

            // Only a session start, no messages
            await service.MarkNewSessionAsync();

            var result = await service.LoadLastSessionAsync();

            Assert.That(result, Is.Empty);
        }

        // ----------------------------------------------------------------
        //  LoadLastSession — multi-file & robustness
        // ----------------------------------------------------------------

        [Test]
        public async Task LoadLastSessionAsync_SessionSpanningTwoFiles_ReturnsAllMessages()
        {
            // The most critical scenario for the bottom-up algorithm:
            // messages of a single session are spread across two hourly files.
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = true });
            var fileSystem = new InMemoryFileSystem();

            var service = new ChatPersistenceService(mockSettings.Object, fileSystem);

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LMLocalChat",
                "ChatHistory");

            string sessionId = "session-bbb";

            // Older file (hour 13): our session starts here with 2 messages
            var olderPath = Path.Combine(dir, "20250201_13_chat_.jsonl");
            var olderContent =
                "{\"type\":\"session_start\",\"session_id\":\"" + sessionId + "\",\"timestamp\":\"2025-02-01T13:00:00Z\"}\n" +
                "{\"type\":\"message\",\"session_id\":\"" + sessionId + "\",\"role\":\"user\",\"content\":\"q1\",\"timestamp\":\"2025-02-01T13:00:01Z\"}\n" +
                "{\"type\":\"message\",\"session_id\":\"" + sessionId + "\",\"role\":\"assistant\",\"content\":\"a1\",\"timestamp\":\"2025-02-01T13:00:02Z\"}\n";
            await fileSystem.WriteAllBytesAsync(olderPath, Encoding.UTF8.GetBytes(olderContent));

            // Newer file (hour 14): session continues with 2 more messages (no session_start)
            var newerPath = Path.Combine(dir, "20250201_14_chat_.jsonl");
            var newerContent =
                "{\"type\":\"message\",\"session_id\":\"" + sessionId + "\",\"role\":\"user\",\"content\":\"q2\",\"timestamp\":\"2025-02-01T14:00:00Z\"}\n" +
                "{\"type\":\"message\",\"session_id\":\"" + sessionId + "\",\"role\":\"assistant\",\"content\":\"a2\",\"timestamp\":\"2025-02-01T14:00:01Z\"}\n";
            await fileSystem.WriteAllBytesAsync(newerPath, Encoding.UTF8.GetBytes(newerContent));

            var result = await service.LoadLastSessionAsync();

            Assert.That(result.Count, Is.EqualTo(4));
            Assert.That(result[0].Content, Is.EqualTo("q1"));
            Assert.That(result[1].Content, Is.EqualTo("a1"));
            Assert.That(result[2].Content, Is.EqualTo("q2"));
            Assert.That(result[3].Content, Is.EqualTo("a2"));
        }

        [Test]
        public async Task LoadLastSessionAsync_WithOldSessionInOlderFile_ReturnsOnlyNewSession()
        {
            // File A (newer, hour 14): session BBB (last session, 2 messages)
            // File B (older, hour 13): session AAA (old session, 2 messages)
            // Must return only BBB messages; early exit on AAA session_start.
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = true });
            var fileSystem = new InMemoryFileSystem();

            var service = new ChatPersistenceService(mockSettings.Object, fileSystem);

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LMLocalChat",
                "ChatHistory");

            string sessionOld = "session-aaa";
            string sessionNew = "session-bbb";

            // Older file (hour 13): old session AAA
            var olderPath = Path.Combine(dir, "20250201_13_chat_.jsonl");
            var olderContent =
                "{\"type\":\"session_start\",\"session_id\":\"" + sessionOld + "\",\"timestamp\":\"2025-02-01T13:00:00Z\"}\n" +
                "{\"type\":\"message\",\"session_id\":\"" + sessionOld + "\",\"role\":\"user\",\"content\":\"old\",\"timestamp\":\"2025-02-01T13:00:01Z\"}\n";
            await fileSystem.WriteAllBytesAsync(olderPath, Encoding.UTF8.GetBytes(olderContent));

            // Newer file (hour 14): new session BBB
            var newerPath = Path.Combine(dir, "20250201_14_chat_.jsonl");
            var newerContent =
                "{\"type\":\"session_start\",\"session_id\":\"" + sessionNew + "\",\"timestamp\":\"2025-02-01T14:00:00Z\"}\n" +
                "{\"type\":\"message\",\"session_id\":\"" + sessionNew + "\",\"role\":\"user\",\"content\":\"new\",\"timestamp\":\"2025-02-01T14:00:01Z\"}\n" +
                "{\"type\":\"message\",\"session_id\":\"" + sessionNew + "\",\"role\":\"assistant\",\"content\":\"answer\",\"timestamp\":\"2025-02-01T14:00:02Z\"}\n";
            await fileSystem.WriteAllBytesAsync(newerPath, Encoding.UTF8.GetBytes(newerContent));

            var result = await service.LoadLastSessionAsync();

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0].Content, Is.EqualTo("new"));
            Assert.That(result[1].Content, Is.EqualTo("answer"));
        }

        [Test]
        public async Task LoadLastSessionAsync_SkipsLinesWithoutSessionId()
        {
            // Lines without session_id (or with empty session_id) must be skipped.
            // Malformed JSON lines must also be skipped gracefully.
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = true });
            var fileSystem = new InMemoryFileSystem();

            var service = new ChatPersistenceService(mockSettings.Object, fileSystem);

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LMLocalChat",
                "ChatHistory");

            string sessionId = "session-xxx";

            var filePath = Path.Combine(dir, "20250201_14_chat_.jsonl");
            var content =
                "{\"type\":\"session_start\",\"session_id\":\"" + sessionId + "\",\"timestamp\":\"2025-02-01T14:00:00Z\"}\n" +
                "garbage not json\n" +
                "{\"type\":\"message\",\"session_id\":\"" + sessionId + "\",\"role\":\"user\",\"content\":\"valid\",\"timestamp\":\"2025-02-01T14:00:01Z\"}\n" +
                "{\"type\":\"message\",\"role\":\"user\",\"content\":\"no-session-id\",\"timestamp\":\"2025-02-01T14:00:02Z\"}\n" +
                "{\"type\":\"message\",\"session_id\":\"\",\"role\":\"user\",\"content\":\"empty-session-id\",\"timestamp\":\"2025-02-01T14:00:03Z\"}\n";
            await fileSystem.WriteAllBytesAsync(filePath, Encoding.UTF8.GetBytes(content));

            var result = await service.LoadLastSessionAsync();

            Assert.That(result[0].Content, Is.EqualTo("valid"));
        }

        /// <summary>
        /// After LoadLastSessionAsync, new messages must continue the loaded session,
        /// not start a new one. Simulates app restart by creating a second service
        /// against the same file system.
        /// </summary>
        [Test]
        public async Task LoadLastSessionAsync_NewMessagesContinueLoadedSession()
        {
            var mockSettings = CreateMockSettingsManager();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = true });
            var fileSystem = new InMemoryFileSystem();

            // First "app launch": create session and save messages
            var service1 = new ChatPersistenceService(mockSettings.Object, fileSystem);
            await service1.MarkNewSessionAsync();
            await service1.SaveLastMessageAsync(new ChatMessage("user", "q1"));
            await service1.SaveLastMessageAsync(new ChatMessage("assistant", "a1"));

            // Second "app launch" (simulated restart): load, then add new message
            var service2 = new ChatPersistenceService(mockSettings.Object, fileSystem);
            var loaded = await service2.LoadLastSessionAsync();
            Assert.That(loaded.Count, Is.EqualTo(2), "Should load 2 messages from previous session");

            // This message must belong to the SAME session, not a new one
            await service2.SaveLastMessageAsync(new ChatMessage("user", "q2"));

            // Load again — should get all 3 messages as one session
            var result = await service2.LoadLastSessionAsync();
            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0].Content, Is.EqualTo("q1"));
            Assert.That(result[1].Content, Is.EqualTo("a1"));
            Assert.That(result[2].Content, Is.EqualTo("q2"),
                "New message after reload must be part of the loaded session");
        }
    }
}

