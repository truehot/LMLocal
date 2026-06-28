using System.Collections.Generic;
using System.Linq;
using LMLocal.Application.Chat;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.LlmApi.Requests;
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

        [Test]
        public void Clear_MarksNewSession()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            var mockPersistence = new Mock<IChatPersistenceService>();
            var manager = new ChatHistoryManager(mockSettings.Object, mockPersistence.Object);

            manager.Clear();

            mockPersistence.Verify(
                p => p.MarkNewSessionAsync(It.IsAny<System.Threading.CancellationToken>()),
                Times.Once);
        }

        [Test]
        public void DependencyInjection_ChatHistoryManager_CreatesSuccessfullyWithDependencies()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("Test system prompt");

            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            Assert.That(manager, Is.Not.Null);
            Assert.That(manager, Is.InstanceOf<ChatHistoryManager>());
        }

        [Test]
        public void AddAssistantMessage_WithContentAndToolCalls_AddsSingleMessage()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            var mockPersistence = new Mock<IChatPersistenceService>();

            var manager = new ChatHistoryManager(mockSettings.Object, mockPersistence.Object);

            var toolCalls = new System.Collections.Generic.List<ToolCallRecord>
            {
                new ToolCallRecord { CallId = "c1", FunctionName = "search", ArgumentsJson = "{}" },
                new ToolCallRecord { CallId = "c2", FunctionName = "read", ArgumentsJson = "{\"path\":\"/x\"}" }
            };

            manager.AddAssistantMessage("Here is the result", toolCalls);

            var history = manager.GetHistoryCopy();

            Assert.That(history.Count, Is.EqualTo(1));
            Assert.That(history[0].Role, Is.EqualTo("assistant"));
            Assert.That(history[0].Content, Is.EqualTo("Here is the result"));

            var toolCallsObj = history[0].ToolCalls as List<ToolCall>;
            Assert.That(toolCallsObj, Is.Not.Null);
            Assert.That(toolCallsObj.Count, Is.EqualTo(2));
            Assert.That(toolCallsObj[0].Id, Is.EqualTo("c1"));
            Assert.That(toolCallsObj[0].Function.Name, Is.EqualTo("search"));
            Assert.That(toolCallsObj[1].Id, Is.EqualTo("c2"));
            Assert.That(toolCallsObj[1].Function.Arguments, Is.EqualTo("{\"path\":\"/x\"}"));

            mockPersistence.Verify(
                p => p.SaveLastMessageAsync(It.IsAny<ChatMessage>(), It.IsAny<System.Threading.CancellationToken>()),
                Times.Once);
        }


        [Test]
        public void AddUserMessage_WithActiveDocumentContent_MergesContent()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("Explain this code", "public class Foo { }");

            var history = manager.GetHistoryCopy();
            Assert.That(history.Count, Is.EqualTo(1));
            Assert.That(history[0].Role, Is.EqualTo("user"));
            Assert.That(history[0].Content as string, Does.Contain("Reference code:"));
            Assert.That(history[0].Content as string, Does.Contain("public class Foo { }"));
            Assert.That(history[0].Content as string, Does.Contain("Explain this code"));
        }

        [Test]
        public void AddUserMessage_WithActiveDocumentContent_PreservesRoundTrip()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("base");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("tell a joke", "some reference code");
            manager.AddAssistantMessage("Why did the chicken...", new List<ToolCallRecord>());

            var history = manager.GetHistoryCopy();
            Assert.That(history.Count, Is.EqualTo(2));
            Assert.That(history[0].Role, Is.EqualTo("user"));
            Assert.That(history[1].Role, Is.EqualTo("assistant"));
        }

        // ---------- BuildUserMessagesWithHistory / LlamaCpp normalization ----------

        [Test]
        public void BuildMessages_NonLlamaCpp_ReturnsHistoryUnchanged()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { Provider = "openai" });
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("hello");
            manager.AddAssistantMessage("world");

            var messages = manager.BuildUserMessagesWithHistory("next");

            Assert.That(messages.Count, Is.EqualTo(4)); // system, user, assistant, user
            Assert.That(messages[0].Role, Is.EqualTo("system"));
            Assert.That(messages[1].Role, Is.EqualTo("user"));
            Assert.That(messages[2].Role, Is.EqualTo("assistant"));
            Assert.That(messages[3].Role, Is.EqualTo("user"));
            Assert.That(messages[3].Content, Is.EqualTo("next"));
        }

        [Test]
        public void BuildMessages_LlamaCpp_EmptyHistory_NoPlaceholder()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { Provider = "llamacpp" });
            mockSettings.Setup(s => s.AssistantPlaceholder).Returns("INTERRUPTED");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.EnsureHistoryNormalized();
            var messages = manager.BuildUserMessagesWithHistory("first prompt");

            Assert.That(messages.Count, Is.EqualTo(2)); // system + user, no placeholder needed
            Assert.That(messages[0].Role, Is.EqualTo("system"));
            Assert.That(messages[1].Role, Is.EqualTo("user"));
            Assert.That(messages[1].Content, Is.EqualTo("first prompt"));
        }

        [Test]
        public void BuildMessages_LlamaCpp_DanglingAssistantToolCalls_Removed()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { Provider = "llamacpp" });
            mockSettings.Setup(s => s.AssistantPlaceholder).Returns("INTERRUPTED");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("search for files");
            manager.AddAssistantMessage("ok searching", new List<ToolCallRecord>
            {
                new ToolCallRecord { CallId = "c1", FunctionName = "find", ArgumentsJson = "{}" }
            });
            manager.EnsureHistoryNormalized();
            var messages = manager.BuildUserMessagesWithHistory("retry");

            // Dangling assistant(tc) removed, user remains → placeholder after user.
            // Result: system, user, assistant(pl), user("retry")
            Assert.That(messages.Count, Is.EqualTo(4));
            Assert.That(messages[0].Role, Is.EqualTo("system"));
            Assert.That(messages[1].Role, Is.EqualTo("user"));
            Assert.That(messages[1].Content, Is.EqualTo("search for files"));
            Assert.That(messages[2].Role, Is.EqualTo("assistant"));
            Assert.That(messages[2].Content, Is.EqualTo("INTERRUPTED"));
            Assert.That(messages[2].ToolCalls, Is.Null);
            Assert.That(messages[3].Role, Is.EqualTo("user"));
            Assert.That(messages[3].Content, Is.EqualTo("retry"));
        }

        [Test]
        public void BuildMessages_LlamaCpp_ConsecutiveUsers_InsertsPlaceholder()
        {
            // Regression: consecutive user messages without assistant between them.
            // The last user in history is skipped (dangling, no response yet) —
            // it is picked up from the raw tail in BuildUserMessagesWithHistory.
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { Provider = "llamacpp" });
            mockSettings.Setup(s => s.AssistantPlaceholder).Returns("INTERRUPTED");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("first");
            manager.AddUserMessage("second");

            manager.EnsureHistoryNormalized();
            var messages = manager.BuildUserMessagesWithHistory("third");

            var roles = messages.Select(m => m.Role).ToList();
            // "first" → normalized into cache; "second" → last in history, skipped;
            // "third" → added as current prompt. No placeholders needed (no assistant responses).
            Assert.That(roles, Is.EqualTo(new List<string>
                { "system", "user", "user" }));
            Assert.That(messages[1].Content, Is.EqualTo("first"));
            Assert.That(messages[2].Content, Is.EqualTo("third"));
        }

        [Test]
        public void BuildMessages_LlamaCpp_NormalToolRound_Preserved()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { Provider = "llamacpp" });
            mockSettings.Setup(s => s.AssistantPlaceholder).Returns("INTERRUPTED");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("find files");
            manager.AddAssistantMessage("using tool", new List<ToolCallRecord>
            {
                new ToolCallRecord { CallId = "c1", FunctionName = "find", ArgumentsJson = "{}" }
            });
            manager.AddToolExecutionResultMessages(new[]
            {
                new ChatMessage("tool", "result1", "c1")
            });
            manager.AddAssistantMessage("found file.txt");
            manager.EnsureHistoryNormalized();
            var messages = manager.BuildUserMessagesWithHistory("thanks");

            var roles = messages.Select(m => m.Role).ToList();
            Assert.That(roles, Is.EqualTo(new List<string>
                { "system", "user", "assistant", "tool", "assistant", "user" }));
            Assert.That(messages[2].ToolCalls, Is.Not.Null);
        }

        [Test]
        public void BuildMessages_LlamaCpp_CacheHit_NoReNormalization()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { Provider = "llamacpp" });
            mockSettings.Setup(s => s.AssistantPlaceholder).Returns("INTERRUPTED");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("hello");
            manager.AddAssistantMessage("world");

            manager.EnsureHistoryNormalized();
            var first = manager.BuildUserMessagesWithHistory("one");
            manager.EnsureHistoryNormalized();
            var second = manager.BuildUserMessagesWithHistory("two");

            Assert.That(first.Select(m => m.Role).ToList(),
                Is.EqualTo(second.Select(m => m.Role).ToList()));
        }

        [Test]
        public void BuildMessages_LlamaCpp_CacheExtend_AfterAppend()
        {
            // Regression: incremental cache extension after new messages appended to history.
            // EnsureHistoryNormalized only processes the tail from _lastCheckedVersion.
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { Provider = "llamacpp" });
            mockSettings.Setup(s => s.AssistantPlaceholder).Returns("INTERRUPTED");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            // Populate cache
            manager.AddUserMessage("u1");
            manager.AddAssistantMessage("a1");
            manager.EnsureHistoryNormalized();
            manager.BuildUserMessagesWithHistory("u2");

            // Append: real flow is AddUserMessage → BuildMessages → AddAssistantMessage
            manager.AddUserMessage("u2");
            manager.AddAssistantMessage("a2");

            manager.EnsureHistoryNormalized();
            // This call must extend cache (not rebuild)
            var result = manager.BuildUserMessagesWithHistory("u3");

            // All 4 history messages present + system + user prompt = 6 minimum
            Assert.That(result.Count, Is.GreaterThanOrEqualTo(6));
            Assert.That(result[0].Role, Is.EqualTo("system"));
            Assert.That(result[result.Count - 1].Role, Is.EqualTo("user"));
            Assert.That(result[result.Count - 1].Content, Is.EqualTo("u3"));
        }

        [Test]
        public void BuildMessages_LlamaCpp_ToolToUser_InsertsPlaceholder()
        {
            // Regression: EnsureHistoryNormalized inserts assistant placeholder
            // when tool result is the last message (dangling tool call, no assistant response).
            // The tool message itself is dropped in this case — llama.cpp format
            // requires assistant after tool, so we close with a placeholder.
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { Provider = "llamacpp" });
            mockSettings.Setup(s => s.AssistantPlaceholder).Returns("INTERRUPTED");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("search files");
            manager.AddAssistantMessage("using tool", new List<ToolCallRecord>
            {
                new ToolCallRecord { CallId = "c1", FunctionName = "find", ArgumentsJson = "{}" }
            });
            manager.AddToolExecutionResultMessages(new[]
            {
                new ChatMessage("tool", "result1", "c1")
            });
            // No assistant response after tool — next is user directly.
            // Tool at end triggers placeholder insertion (tool dropped, placeholder closes the turn).

            manager.EnsureHistoryNormalized();
            var messages = manager.BuildUserMessagesWithHistory("next question");

            var roles = messages.Select(m => m.Role).ToList();
            Assert.That(roles, Is.EqualTo(new List<string>
                { "system", "user", "assistant", "assistant", "user" }));
            Assert.That(messages[3].Content, Is.EqualTo("INTERRUPTED"));   // placeholder
            Assert.That(messages[3].ToolCalls, Is.Null);
            Assert.That(messages[4].Content, Is.EqualTo("next question")); // current prompt
        }

        [Test]
        public void BuildMessages_LlamaCpp_InterruptedPlaceholderBetweenToolAndUser_Removed()
        {
            // Regression: when a previous generation was interrupted, the error handler
            // adds assistant("The previous response was interrupted.") between tool results
            // and a new user message. This stale placeholder must be removed so that
            // the tool→user gap handler can insert a fresh placeholder.
            // History: user → assistant(tc) → tool → assistant(placeholder) → user(new)
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { Provider = "llamacpp" });
            mockSettings.Setup(s => s.AssistantPlaceholder).Returns("INTERRUPTED");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("search files");
            manager.AddAssistantMessage("using tool", new List<ToolCallRecord>
            {
                new ToolCallRecord { CallId = "c1", FunctionName = "find", ArgumentsJson = "{}" }
            });
            manager.AddToolExecutionResultMessages(new[]
            {
                new ChatMessage("tool", "result1", "c1")
            });
            // Simulate interrupted generation: error handler added placeholder assistant
            manager.AddAssistantMessage("INTERRUPTED");
            // User sends follow-up
            manager.EnsureHistoryNormalized();
            var messages = manager.BuildUserMessagesWithHistory("continue please");

            var roles = messages.Select(m => m.Role).ToList();
            // Stale placeholder removed, fresh placeholder inserted between tool and user.
            // Expected: system, user, assistant(tc), tool, assistant(pl), user
            Assert.That(roles, Is.EqualTo(new List<string>
                { "system", "user", "assistant", "tool", "assistant", "user" }));
            Assert.That(messages[2].ToolCalls, Is.Not.Null);
            Assert.That(messages[4].Content, Is.EqualTo("INTERRUPTED"));   // fresh placeholder
            Assert.That(messages[4].ToolCalls, Is.Null);
            Assert.That(messages[5].Content, Is.EqualTo("continue please"));
        }

        [Test]
        public void BuildMessages_LlamaCpp_LegitimateAssistantAfterTool_Preserved()
        {
            // A real assistant response after tool results (content != placeholder text)
            // must pass through unchanged — only the known interruption placeholder is removed.
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { Provider = "llamacpp" });
            mockSettings.Setup(s => s.AssistantPlaceholder).Returns("INTERRUPTED");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("search files");
            manager.AddAssistantMessage("using tool", new List<ToolCallRecord>
            {
                new ToolCallRecord { CallId = "c1", FunctionName = "find", ArgumentsJson = "{}" }
            });
            manager.AddToolExecutionResultMessages(new[]
            {
                new ChatMessage("tool", "result1", "c1")
            });
            // Legitimate final response from the model (not the interruption placeholder)
            manager.AddAssistantMessage("found file.txt");
            manager.EnsureHistoryNormalized();
            var messages = manager.BuildUserMessagesWithHistory("thanks");

            var roles = messages.Select(m => m.Role).ToList();
            // Real assistant response preserved as-is; no placeholder needed.
            // Expected: system, user, assistant(tc), tool, assistant("found file.txt"), user
            Assert.That(roles, Is.EqualTo(new List<string>
                { "system", "user", "assistant", "tool", "assistant", "user" }));
            Assert.That(messages[2].ToolCalls, Is.Not.Null);
            Assert.That(messages[4].Content, Is.EqualTo("found file.txt")); // real response kept
            Assert.That(messages[4].ToolCalls, Is.Null);
            Assert.That(messages[5].Content, Is.EqualTo("thanks"));
        }
    }
}