using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    /// <summary>
    /// Covers the session-boundary contract of <see cref="ChatPersistenceService"/>:
    /// a new session is written as one append (marker + messages), the write reports success, and every
    /// write carries the session id that was current when it was *called*, not when it reached the file.
    /// </summary>
    [TestFixture]
    public class ChatPersistenceSessionTests
    {
        private const string Dir = "C:/tests/chat";

        private static Mock<ISettingsManager> CreateSettingsManager(bool loggingEnabled = true)
        {
            var mock = new Mock<ISettingsManager>();
            mock.Setup(s => s.LocalAppDataFolder).Returns("LMLocalChat");
            mock.Setup(s => s.ChatHistoryFolder).Returns("ChatHistory");
            mock.Setup(s => s.ChatHistoryFileLabel).Returns("chat_");
            mock.Setup(s => s.Current).Returns(new AppSettings { EnableChatLogging = loggingEnabled });
            return mock;
        }

        /// <summary>
        /// File system mock that records every append payload and optionally blocks the n-th append.
        /// </summary>
        private static Mock<IFileSystem> CreateAppendingFileSystem(List<string> writes, Func<int, Task> gate = null)
        {
            int appendCount = 0;

            var fs = new Mock<IFileSystem>();
            fs.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);
            fs.Setup(f => f.AppendAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
              .Returns((string path, byte[] data, CancellationToken ct) =>
              {
                  lock (writes)
                      writes.Add(Encoding.UTF8.GetString(data));

                  int n = Interlocked.Increment(ref appendCount);
                  return gate != null ? gate(n) : Task.CompletedTask;
              });

            return fs;
        }

        private static List<JObject> ParseLines(string payload)
        {
            var result = new List<JObject>();
            foreach (var line in payload.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                result.Add(JObject.Parse(line));
            return result;
        }

        private static List<JObject> ParseWrites(List<string> writes)
        {
            List<JObject> all;
            lock (writes)
                all = writes.SelectMany(ParseLines).ToList();
            return all;
        }

        [Test]
        public async Task StartSessionWithMessagesAsync_WritesMarkerAndMessagesInOneAppend()
        {
            var writes = new List<string>();
            var fs = CreateAppendingFileSystem(writes);
            var service = new ChatPersistenceService(CreateSettingsManager().Object, fs.Object, Dir);

            bool result = await service.StartSessionWithMessagesAsync(new List<ChatMessage>
            {
                new ChatMessage("user", "summary prompt"),
                new ChatMessage("assistant", "summary")
            });

            Assert.That(result, Is.True);

            // A single append: the marker cannot be persisted without its messages (and vice versa).
            fs.Verify(f => f.AppendAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(writes.Count, Is.EqualTo(1));

            var lines = ParseLines(writes[0]);
            Assert.That(lines.Count, Is.EqualTo(3));
            Assert.That(lines[0].Value<string>("type"), Is.EqualTo("session_start"));
            Assert.That(lines[1].Value<string>("role"), Is.EqualTo("user"));
            Assert.That(lines[2].Value<string>("role"), Is.EqualTo("assistant"));

            string sessionId = lines[0].Value<string>("session_id");
            Assert.That(lines[1].Value<string>("session_id"), Is.EqualTo(sessionId));
            Assert.That(lines[2].Value<string>("session_id"), Is.EqualTo(sessionId));
            Assert.That(service.CurrentSessionId.Value.ToString(), Is.EqualTo(sessionId));
        }

        [Test]
        public async Task StartSessionWithMessagesAsync_WithNoMessages_WritesMarkerOnly()
        {
            var writes = new List<string>();
            var fs = CreateAppendingFileSystem(writes);
            var service = new ChatPersistenceService(CreateSettingsManager().Object, fs.Object, Dir);

            bool result = await service.StartSessionWithMessagesAsync(new List<ChatMessage>());

            Assert.That(result, Is.True);
            Assert.That(writes.Count, Is.EqualTo(1));

            var lines = ParseLines(writes[0]);
            Assert.That(lines.Count, Is.EqualTo(1));
            Assert.That(lines[0].Value<string>("type"), Is.EqualTo("session_start"));
        }

        [Test]
        public async Task StartSessionWithMessagesAsync_WhenLoggingDisabled_ReturnsFalseAndWritesNothing()
        {
            var writes = new List<string>();
            var fs = CreateAppendingFileSystem(writes);
            var service = new ChatPersistenceService(CreateSettingsManager(loggingEnabled: false).Object, fs.Object, Dir);

            Guid before = service.CurrentSessionId.Value;

            bool result = await service.StartSessionWithMessagesAsync(new List<ChatMessage>
            {
                new ChatMessage("user", "x")
            });

            Assert.That(result, Is.False);
            Assert.That(writes, Is.Empty);
            fs.Verify(f => f.AppendAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
            fs.Verify(f => f.WriteAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(service.CurrentSessionId.Value, Is.EqualTo(before),
                "A disabled log must not rotate the session id either");
        }

        [Test]
        public async Task StartSessionWithMessagesAsync_WhenWriteFails_ReturnsFalseAndKeepsSessionId()
        {
            var fs = new Mock<IFileSystem>();
            fs.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);
            fs.Setup(f => f.AppendAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new IOException("disk full"));

            var service = new ChatPersistenceService(CreateSettingsManager().Object, fs.Object, Dir);
            Guid before = service.CurrentSessionId.Value;

            bool result = await service.StartSessionWithMessagesAsync(new List<ChatMessage>
            {
                new ChatMessage("user", "x")
            });

            Assert.That(result, Is.False, "The caller must be able to see that nothing was persisted");
            Assert.That(service.CurrentSessionId.Value, Is.EqualTo(before),
                "A rolled-back write must leave later appends in the previous session");
        }

        [Test]
        public async Task MarkNewSessionAsync_ThenSave_WritesMessageUnderNewSessionId()
        {
            var writes = new List<string>();
            var fs = CreateAppendingFileSystem(writes);
            var service = new ChatPersistenceService(CreateSettingsManager().Object, fs.Object, Dir);

            Guid original = service.CurrentSessionId.Value;

            await service.MarkNewSessionAsync();
            await service.SaveLastMessageAsync(new ChatMessage("user", "after marker"));

            var lines = ParseWrites(writes);
            Assert.That(lines.Count, Is.EqualTo(2));

            string markerSessionId = lines[0].Value<string>("session_id");
            string messageSessionId = lines[1].Value<string>("session_id");

            Assert.That(markerSessionId, Is.Not.EqualTo(original.ToString()));
            Assert.That(messageSessionId, Is.EqualTo(markerSessionId));
        }

        [Test]
        public async Task MarkNewSessionAsync_WhenWriteFails_StillRotatesAtCallTime()
        {
            var fs = new Mock<IFileSystem>();
            fs.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);
            fs.Setup(f => f.AppendAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new IOException("disk full"));

            var service = new ChatPersistenceService(CreateSettingsManager().Object, fs.Object, Dir);
            Guid before = service.CurrentSessionId.Value;

            await service.MarkNewSessionAsync();

            Assert.That(service.CurrentSessionId.Value, Is.Not.EqualTo(before),
                "The id is rotated at call time and is never rolled back, so writes that already captured it stay in one session");
        }

        [Test]
        public async Task MarkNewSessionAsync_WhileSemaphoreBusy_RotatesImmediately_SoFollowingSaveUsesNewId()
        {
            // Regression: Clear() fires MarkNewSessionAsync and does not await it. If the rotation were
            // deferred until the semaphore is acquired, the very next Add* (e.g. the first message of a new
            // chat) would capture the OLD id and be written after the new marker under the old session —
            // leaving the new session empty and attaching the new chat to the previous one.
            var writes = new List<string>();
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var fs = CreateAppendingFileSystem(writes, n => n == 1 ? gate.Task : Task.CompletedTask);
            var service = new ChatPersistenceService(CreateSettingsManager().Object, fs.Object, Dir);

            string originalSessionId = service.CurrentSessionId.Value.ToString();

            var inFlight = service.SaveLastMessageAsync(new ChatMessage("user", "last answer being written"));
            var newSession = service.MarkNewSessionAsync();                                   // queued behind it
            var firstMessage = service.SaveLastMessageAsync(new ChatMessage("user", "first message of the new chat"));

            gate.SetResult(true);
            await Task.WhenAll(inFlight, newSession, firstMessage);

            var lines = ParseWrites(writes);
            string markerSessionId = lines.Single(o => o.Value<string>("type") == "session_start").Value<string>("session_id");

            var newChatMessage = lines.Last();
            Assert.That(markerSessionId, Is.Not.EqualTo(originalSessionId));
            Assert.That(newChatMessage.Value<string>("role"), Is.EqualTo("user"));
            Assert.That(newChatMessage.Value<string>("session_id"), Is.EqualTo(markerSessionId),
                "The first message of the new chat must belong to the new session, not to the previous one");
        }

        [Test]
        public async Task SaveLastMessageAsync_QueuedBehindNewSession_KeepsCallTimeSessionId()
        {
            var writes = new List<string>();
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // The first append blocks while holding the write semaphore, so the second save and the session
            // marker are both still queued when the semaphore is released — the window the race needed.
            var fs = CreateAppendingFileSystem(writes, n => n == 1 ? gate.Task : Task.CompletedTask);
            var service = new ChatPersistenceService(CreateSettingsManager().Object, fs.Object, Dir);

            string originalSessionId = service.CurrentSessionId.Value.ToString();

            var firstWrite = service.SaveLastMessageAsync(new ChatMessage("user", "first"));
            var queuedWrite = service.SaveLastMessageAsync(new ChatMessage("user", "queued"));
            var marker = service.MarkNewSessionAsync();

            gate.SetResult(true);
            await Task.WhenAll(firstWrite, queuedWrite, marker);

            var messageSessionIds = ParseWrites(writes)
                .Where(o => o.Value<string>("type") != "session_start")
                .Select(o => o.Value<string>("session_id"))
                .ToList();

            Assert.That(messageSessionIds.Count, Is.EqualTo(2));
            Assert.That(messageSessionIds, Is.All.EqualTo(originalSessionId),
                "A save must carry the session id captured when it was called, not the one current when the semaphore is acquired");
        }
    }
}
