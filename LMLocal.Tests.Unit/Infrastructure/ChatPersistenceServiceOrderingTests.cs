using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Tests.Unit.Infrastructure;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    /// <summary>
    /// Characterization tests that document the write-ordering contract of <see cref="ChatPersistenceService"/>.
    ///
    /// Both <c>MarkNewSessionAsync</c> and <c>SaveMessagesAsync</c> write through the same
    /// <c>SemaphoreSlim</c> (<c>_writeSemaphore</c>). As a result, an *un-awaited* (fire-and-forget) session
    /// marker is still guaranteed to be persisted before the messages that follow it, and the new session
    /// can always be read back with <c>LoadLastSessionAsync</c>.
    ///
    /// This is why the historical <c>_ = _persistence?.MarkNewSessionAsync();</c> pattern in
    /// <c>ChatHistoryManager.Clear()</c> did NOT produce an "empty chat" race on the persistence layer.
    /// <see cref="CancelableFileSystem"/> is used so every write has a deliberate delay, widening any
    /// timing window between the two writes.
    /// </summary>
    [TestFixture]
    public class ChatPersistenceServiceOrderingTests
    {
        private const string Dir = "C:/tests/chat";

        private static Mock<ISettingsManager> Settings()
        {
            var settings = new Mock<ISettingsManager>();
            settings.Setup(x => x.Current).Returns(new AppSettings { EnableChatLogging = true });
            settings.Setup(x => x.ChatHistoryFileLabel).Returns("test");
            return settings;
        }

        [Test]
        public async Task FireAndForgetMarker_ThenSave_MarkerIsWrittenFirst_AndSessionLoads()
        {
            var fs = new CancelableFileSystem();
            var svc = new ChatPersistenceService(Settings().Object, fs, Dir);

            var msgs = new List<ChatMessage>
            {
                new ChatMessage("user", "last"),
                new ChatMessage("assistant", "resp")
            };

            // Deliberately NOT awaited, mimicking the historical Clear() behavior.
            _ = svc.MarkNewSessionAsync();
            await svc.SaveMessagesAsync(msgs);

            var file = fs.GetFiles(Dir, "*.jsonl").Single();
            var lines = fs.ReadAllText(file)
                          .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            Assert.That(lines[0], Does.Contain("session_start"), "Marker must be the first line");
            Assert.That(lines[1], Does.Contain("\"role\":\"user\""));
            Assert.That(lines[2], Does.Contain("\"role\":\"assistant\""));

            var loaded = await svc.LoadLastSessionAsync();
            Assert.That(loaded.Count, Is.EqualTo(2));
            Assert.That((string)loaded[0].Content, Is.EqualTo("last"));
            Assert.That((string)loaded[1].Content, Is.EqualTo("resp"));
        }

        [Test]
        public async Task AwaitedMarker_ThenSave_ProducesSameOrdering()
        {
            var fs = new CancelableFileSystem();
            var svc = new ChatPersistenceService(Settings().Object, fs, Dir);

            var msgs = new List<ChatMessage>
            {
                new ChatMessage("user", "last"),
                new ChatMessage("assistant", "resp")
            };

            await svc.MarkNewSessionAsync();
            await svc.SaveMessagesAsync(msgs);

            var file = fs.GetFiles(Dir, "*.jsonl").Single();
            var lines = fs.ReadAllText(file)
                          .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            Assert.That(lines[0], Does.Contain("session_start"));
            Assert.That(lines[1], Does.Contain("\"role\":\"user\""));
            Assert.That(lines[2], Does.Contain("\"role\":\"assistant\""));

            var loaded = await svc.LoadLastSessionAsync();
            Assert.That(loaded.Count, Is.EqualTo(2));
        }
    }
}
