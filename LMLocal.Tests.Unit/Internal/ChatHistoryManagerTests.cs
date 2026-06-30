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
            manager.AddUserMessage("next");

            var messages = manager.BuildUserMessagesWithHistory();

            Assert.That(messages.Count, Is.EqualTo(4)); // system, user, assistant, user
            Assert.That(messages[0].Role, Is.EqualTo("system"));
            Assert.That(messages[1].Role, Is.EqualTo("user"));
            Assert.That(messages[1].Content, Is.EqualTo("hello"));
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
            manager.AddUserMessage("first prompt");
            var messages = manager.BuildUserMessagesWithHistory();

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
            manager.AddUserMessage("retry");
            var messages = manager.BuildUserMessagesWithHistory();

            // Dangling assistant(tc) → placeholder after user.
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
            // EnsureHistoryNormalized removes the first lone user (no assistant follows)
            // and processes the second through the isInsidePrompt path.
            // The last user added after normalization appears via the raw tail.
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { Provider = "llamacpp" });
            mockSettings.Setup(s => s.AssistantPlaceholder).Returns("INTERRUPTED");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("first");
            manager.AddUserMessage("second");
            manager.EnsureHistoryNormalized();
            manager.AddUserMessage("third");
            var messages = manager.BuildUserMessagesWithHistory();

            var roles = messages.Select(m => m.Role).ToList();
            // "first" removed (no assistant follows), "second" consumed by isInsidePrompt,
            // "third" added via AddUserMessage after normalization → appears in raw tail.
            Assert.That(roles, Is.EqualTo(new List<string>
                { "system", "user" }));
            Assert.That(messages[1].Content, Is.EqualTo("third"));
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
            manager.AddUserMessage("thanks");
            var messages = manager.BuildUserMessagesWithHistory();

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
            manager.AddUserMessage("one");
            var first = manager.BuildUserMessagesWithHistory();
            manager.EnsureHistoryNormalized();
            manager.AddUserMessage("two");
            var second = manager.BuildUserMessagesWithHistory();

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
            manager.AddUserMessage("u2");
            manager.BuildUserMessagesWithHistory();

            // Append: real flow is AddUserMessage → BuildMessages → AddAssistantMessage
            manager.AddAssistantMessage("a2");
            manager.EnsureHistoryNormalized();
            manager.AddUserMessage("u3");
            var result = manager.BuildUserMessagesWithHistory();

            // All 4 history messages present + system = 5 minimum
            Assert.That(result.Count, Is.GreaterThanOrEqualTo(5));
            Assert.That(result[0].Role, Is.EqualTo("system"));
            Assert.That(result[result.Count - 1].Role, Is.EqualTo("user"));
            Assert.That(result[result.Count - 1].Content, Is.EqualTo("u3"));
        }

        [Test]
        public void BuildMessages_LlamaCpp_ToolToUser_InsertsPlaceholder()
        {
            // Regression: EnsureHistoryNormalized inserts assistant placeholder
            // when tool result is the last message (dangling tool call, no assistant response).
            // The tool message is preserved, placeholder closes the turn.
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
            manager.EnsureHistoryNormalized();
            manager.AddUserMessage("next question");
            var messages = manager.BuildUserMessagesWithHistory();

            var roles = messages.Select(m => m.Role).ToList();
            Assert.That(roles, Is.EqualTo(new List<string>
                { "system", "user", "assistant", "tool", "assistant", "user" }));
            Assert.That(messages[2].ToolCalls, Is.Not.Null);
            Assert.That(messages[4].Content, Is.EqualTo("INTERRUPTED"));   // placeholder
            Assert.That(messages[4].ToolCalls, Is.Null);
            Assert.That(messages[5].Content, Is.EqualTo("next question"));
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
            manager.EnsureHistoryNormalized();
            manager.AddUserMessage("continue please");
            var messages = manager.BuildUserMessagesWithHistory();

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
            manager.AddUserMessage("thanks");
            var messages = manager.BuildUserMessagesWithHistory();

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


        // ---------- EnsureHistoryNormalized edge cases ----------

        [Test]
        public void EnsureHistoryNormalized_SingleUserMessage_SkippedFromCachePresentInTail()
        {
            // Single user message with no assistant response: normalization skips it
            // from cache (it's the last message), and it's also past _lastCheckedVersion
            // so it won't appear in the raw tail — effectively lost.
            // Only the user added _after_ normalization survives.
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { Provider = "llamacpp" });
            mockSettings.Setup(s => s.AssistantPlaceholder).Returns("INTERRUPTED");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("lone message");
            manager.EnsureHistoryNormalized();
            manager.AddUserMessage("next");
            var messages = manager.BuildUserMessagesWithHistory();

            var roles = messages.Select(m => m.Role).ToList();
            // Lone user skipped from cache (isLast in snapshot), also past _lastCheckedVersion.
            // Only the post-normalization user appears.
            Assert.That(roles, Is.EqualTo(new List<string>
                { "system", "user" }));
            Assert.That(messages[1].Content, Is.EqualTo("next"));
        }

        [Test]
        public void EnsureHistoryNormalized_MultipleToolRounds_Preserved()
        {
            // Multiple sequential assistant(tc)→tool rounds within a single user turn.
            // user → assistant(tc1) → tool1 → assistant(tc2) → tool2 → assistant(final) → user
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { Provider = "llamacpp" });
            mockSettings.Setup(s => s.AssistantPlaceholder).Returns("INTERRUPTED");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("complex task");
            manager.AddAssistantMessage("step 1", new List<ToolCallRecord>
            {
                new ToolCallRecord { CallId = "c1", FunctionName = "search", ArgumentsJson = "{}" }
            });
            manager.AddToolExecutionResultMessages(new[]
            {
                new ChatMessage("tool", "result1", "c1")
            });
            manager.AddAssistantMessage("step 2", new List<ToolCallRecord>
            {
                new ToolCallRecord { CallId = "c2", FunctionName = "read", ArgumentsJson = "{\"path\":\"/f\"}" }
            });
            manager.AddToolExecutionResultMessages(new[]
            {
                new ChatMessage("tool", "result2", "c2")
            });
            manager.AddAssistantMessage("all done");
            manager.EnsureHistoryNormalized();
            manager.AddUserMessage("thanks");
            var messages = manager.BuildUserMessagesWithHistory();

            var roles = messages.Select(m => m.Role).ToList();
            // Expected: system, user, assistant(tc1), tool, assistant(tc2), tool, assistant(final), user
            Assert.That(roles, Is.EqualTo(new List<string>
                { "system", "user", "assistant", "tool", "assistant", "tool", "assistant", "user" }));
            Assert.That(messages[2].ToolCalls, Is.Not.Null);
            Assert.That(((IReadOnlyList<ToolCall>)messages[2].ToolCalls).Count, Is.EqualTo(1));
            Assert.That(messages[4].ToolCalls, Is.Not.Null);
            Assert.That(((IReadOnlyList<ToolCall>)messages[4].ToolCalls).Count, Is.EqualTo(1));
            Assert.That(messages[6].Content, Is.EqualTo("all done"));
            Assert.That(messages[6].ToolCalls, Is.Null);
            Assert.That(messages[7].Content, Is.EqualTo("thanks"));
        }

        [Test]
        public void EnsureHistoryNormalized_DanglingToolResults_PlaceholderAdded()
        {
            // assistant(tc) → tool (last in history, no assistant after).
            // Normalization adds placeholder to close the tool→assistant gap.
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
            // No assistant response — tool is last.
            manager.EnsureHistoryNormalized();
            manager.AddUserMessage("next");
            var messages = manager.BuildUserMessagesWithHistory();

            var roles = messages.Select(m => m.Role).ToList();
            Assert.That(roles, Is.EqualTo(new List<string>
                { "system", "user", "assistant", "tool", "assistant", "user" }));
            Assert.That(messages[4].Content, Is.EqualTo("INTERRUPTED"));
            Assert.That(messages[4].ToolCalls, Is.Null);
        }

        [Test]
        public void EnsureHistoryNormalized_NonLlamaCpp_NoOp()
        {
            // EnsureHistoryNormalized is a no-op for non-LlamaCpp providers.
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { Provider = "openai" });
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("hello");
            manager.AddUserMessage("world"); // consecutive users — would be removed by LlamaCpp normalization
            manager.EnsureHistoryNormalized();
            manager.AddUserMessage("again");
            var messages = manager.BuildUserMessagesWithHistory();

            // Non-LlamaCpp: all messages pass through as-is, no normalization.
            var roles = messages.Select(m => m.Role).ToList();
            Assert.That(roles, Is.EqualTo(new List<string>
                { "system", "user", "user", "user" }));
            Assert.That(messages[1].Content, Is.EqualTo("hello"));
            Assert.That(messages[2].Content, Is.EqualTo("world"));
            Assert.That(messages[3].Content, Is.EqualTo("again"));
        }

        // ---------- SetPendingAssistant ----------

        [Test]
        public void SetPendingAssistant_FlushedByAddToolExecutionResultMessages()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("find files");
            manager.SetPendingAssistant("using tool", new List<ToolCallRecord>
            {
                new ToolCallRecord { CallId = "c1", FunctionName = "find", ArgumentsJson = "{}" }
            });

            // Before flush — history has only user
            var before = manager.GetHistoryCopy();
            Assert.That(before.Count, Is.EqualTo(1));
            Assert.That(before[0].Role, Is.EqualTo("user"));

            // Flush via AddToolExecutionResultMessages
            manager.AddToolExecutionResultMessages(new[]
            {
                new ChatMessage("tool", "result1", "c1")
            });

            // After flush — assistant+tc, then tool results
            var after = manager.GetHistoryCopy();
            Assert.That(after.Count, Is.EqualTo(3));
            Assert.That(after[0].Role, Is.EqualTo("user"));
            Assert.That(after[1].Role, Is.EqualTo("assistant"));
            Assert.That(after[1].ToolCalls, Is.Not.Null);
            Assert.That(after[2].Role, Is.EqualTo("tool"));
            Assert.That(after[2].ToolCallId, Is.EqualTo("c1"));
        }

        [Test]
        public void SetPendingAssistant_ClearedOnClear()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.SetPendingAssistant("using tool", new List<ToolCallRecord>
            {
                new ToolCallRecord { CallId = "c1", FunctionName = "find", ArgumentsJson = "{}" }
            });
            manager.Clear();

            // Clear discards pending — AddToolExecutionResultMessages is a no-op for assistant
            manager.AddToolExecutionResultMessages(new[]
            {
                new ChatMessage("tool", "result1", "c1")
            });

            var history = manager.GetHistoryCopy();
            Assert.That(history.Count, Is.EqualTo(1)); // only tool result, no assistant
            Assert.That(history[0].Role, Is.EqualTo("tool"));
        }

        [Test]
        public void SetPendingAssistant_NotInHistoryUntilFlushed()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            var manager = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            manager.AddUserMessage("hello");
            manager.SetPendingAssistant("response", new List<ToolCallRecord>());

            // Pending should not appear in history copy
            var history = manager.GetHistoryCopy();
            Assert.That(history.Count, Is.EqualTo(1));
            Assert.That(history[0].Role, Is.EqualTo("user"));
        }
    }
}