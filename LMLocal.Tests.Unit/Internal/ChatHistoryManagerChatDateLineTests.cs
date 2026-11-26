using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Chat;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi.Provider;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    /// <summary>
    /// The chat date line is the tail of the system message: the prompt text stays an unchanged prefix, and only the
    /// chat itself drops the line. The line is taken from the clock once per chat, never per request.
    /// </summary>
    [TestFixture]
    public class ChatHistoryManagerChatDateLineTests
    {
        /// <summary>Line the fake clock produces: 12:05 UTC read in the UTC+02:00 zone.</summary>
        private const string Line = "Chat started: 2026-11-21 14:05 (Saturday), time zone UTC+02:00.";

        private const string LineThreeHoursLater = "Chat started: 2026-11-21 17:05 (Saturday), time zone UTC+02:00.";

        private Mock<IChatPersistenceService> _persistence;
        private FakeTimeProvider _clock;

        [SetUp]
        public void SetUp()
        {
            _persistence = new Mock<IChatPersistenceService>();
            _persistence
                .Setup(p => p.StartSessionWithMessagesAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            _clock = new FakeTimeProvider(
                new DateTime(2026, 11, 21, 12, 5, 0, DateTimeKind.Utc),
                TimeZoneInfo.CreateCustomTimeZone("Tests/Plus2", TimeSpan.FromHours(2), "Tests/Plus2", "Tests/Plus2"));
        }

        /// <summary>
        /// A manager of the main chat; <paramref name="withClock"/> false is the history of a SubAgent run, which never
        /// gets a date line.
        /// </summary>
        private ChatHistoryManager CreateManager(string systemPrompt = "sys", bool withClock = true)
        {
            var settings = new Mock<ISettingsManager>();
            settings.Setup(s => s.SystemPrompt).Returns(systemPrompt);
            settings.Setup(s => s.Current).Returns(new AppSettings());

            return new ChatHistoryManager(settings.Object, _persistence.Object, new ProviderPolicy(), null, withClock ? _clock : null);
        }

        /// <summary>Text of the system message the model gets, i.e. the only place the line is visible.</summary>
        private static string SystemMessage(ChatHistoryManager manager)
        {
            return manager.BuildUserMessagesWithHistory()[0].Content?.ToString();
        }

        [Test]
        public void EnsureChatDateLine_AppendsTheLineToTheSystemPrompt()
        {
            var manager = CreateManager("sys");

            manager.EnsureChatDateLine();

            var messages = manager.BuildUserMessagesWithHistory();
            Assert.That(messages[0].Role, Is.EqualTo("system"));
            Assert.That(messages[0].Content, Is.EqualTo("sys\n\n" + Line));
        }

        [Test]
        public void EnsureChatDateLine_AppendsTheLineAfterTrimmingTheTrailingNewlinesOfThePrompt()
        {
            var manager = CreateManager("sys\n");

            manager.EnsureChatDateLine();

            Assert.That(SystemMessage(manager), Is.EqualTo("sys\n\n" + Line));
        }

        [Test]
        public void EnsureChatDateLine_WithoutASystemPrompt_TheLineIsTheWholeSystemMessage()
        {
            var manager = CreateManager(null);

            manager.EnsureChatDateLine();

            var messages = manager.BuildUserMessagesWithHistory();
            Assert.That(messages[0].Role, Is.EqualTo("system"));
            Assert.That(messages[0].Content, Is.EqualTo(Line));
        }

        [Test]
        public void TheSameChat_KeepsTheLine_EvenWhenTheClockMoved()
        {
            var manager = CreateManager();
            manager.EnsureChatDateLine();

            _clock.Advance(TimeSpan.FromHours(3));
            manager.EnsureChatDateLine();

            Assert.That(SystemMessage(manager), Is.EqualTo("sys\n\n" + Line),
                "A chat keeps the line it started with; changing it would invalidate the cached prompt prefix.");
        }

        [Test]
        public void BeforeTheLineIsTaken_ThePromptIsSentUnchanged()
        {
            var manager = CreateManager("sys\n");

            Assert.That(SystemMessage(manager), Is.EqualTo("sys\n"));
        }

        [Test]
        public void WithoutATimeProvider_TheLineIsNeverTaken()
        {
            var manager = CreateManager("sys", withClock: false);

            manager.EnsureChatDateLine();

            Assert.That(SystemMessage(manager), Is.EqualTo("sys"));
        }

        [Test]
        public void Clear_StartsANewChat_SoTheNextRequestTakesTheNewTime()
        {
            var manager = CreateManager();
            manager.EnsureChatDateLine();

            manager.Clear();
            _clock.Advance(TimeSpan.FromHours(3));
            manager.EnsureChatDateLine();

            Assert.That(SystemMessage(manager), Is.EqualTo("sys\n\n" + LineThreeHoursLater));
        }

        [Test]
        public async Task ClearAndSaveMessagesAsync_KeepsTheLine_SoACompactionLeavesThePromptPrefixAlone()
        {
            var manager = CreateManager();
            manager.EnsureChatDateLine();

            await manager.ClearAndSaveMessagesAsync(new List<ChatMessage> { new ChatMessage("user", "summary placeholder") });
            _clock.Advance(TimeSpan.FromHours(3));
            manager.EnsureChatDateLine();

            Assert.That(SystemMessage(manager), Is.EqualTo("sys\n\n" + Line));
        }

        [Test]
        public async Task ReplaceHistoryWithNewSessionAsync_KeepsTheLine_SoTheOverflowRecoveryLeavesThePrefixAlone()
        {
            var manager = CreateManager();
            manager.EnsureChatDateLine();

            await manager.ReplaceHistoryWithNewSessionAsync(new List<ChatMessage> { new ChatMessage("user", "summary placeholder") });
            _clock.Advance(TimeSpan.FromHours(3));
            manager.EnsureChatDateLine();

            Assert.That(SystemMessage(manager), Is.EqualTo("sys\n\n" + Line));
        }

        [Test]
        public async Task LoadSessionByIdAsync_DropsTheLine_SoTheLoadedChatGetsALineOfItsOwn()
        {
            _persistence
                .Setup(p => p.LoadSessionByIdAsync("other-chat", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ChatMessage> { new ChatMessage("user", "old prompt") });
            var manager = CreateManager();
            manager.EnsureChatDateLine();

            await manager.LoadSessionByIdAsync("other-chat");
            _clock.Advance(TimeSpan.FromHours(3));
            manager.EnsureChatDateLine();

            Assert.That(SystemMessage(manager), Is.EqualTo("sys\n\n" + LineThreeHoursLater));
        }

        [Test]
        public void ReplaceHistoryInMemory_KeepsTheLine()
        {
            var manager = CreateManager();
            manager.EnsureChatDateLine();

            manager.ReplaceHistoryInMemory(new List<ChatMessage> { new ChatMessage("user", "summary placeholder") });
            _clock.Advance(TimeSpan.FromHours(3));
            manager.EnsureChatDateLine();

            Assert.That(SystemMessage(manager), Is.EqualTo("sys\n\n" + Line));
        }
    }
}
