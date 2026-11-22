using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LMLocal.Application.Chat;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application
{
    /// <summary>
    /// Rules that turn parsed chat logs into the cards of the chat history dialog: prompt, activity timestamp,
    /// message count and the models that produced the answers. No file system is involved.
    /// </summary>
    [TestFixture]
    public class ChatSessionSummaryBuilderTests
    {
        private const string SessionId = "session-1";

        private static ChatLogEntry Entry(string role, string content, string timestamp, string modelId = null)
        {
            return new ChatLogEntry
            {
                Message = new ChatMessage(role, content) { ModelId = modelId },
                TimestampUtc = ParseTimestamp(timestamp)
            };
        }

        private static DateTime ParseTimestamp(string iso8601)
        {
            if (string.IsNullOrEmpty(iso8601)) return DateTime.MinValue;

            return DateTime.Parse(iso8601, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
        }

        private static ChatSessionLog Log(string sessionId, params ChatLogEntry[] entries)
        {
            return new ChatSessionLog { SessionId = sessionId, Entries = entries.ToList() };
        }

        private static ChatSessionSummary BuildSingle(params ChatLogEntry[] entries)
        {
            var summaries = ChatSessionSummaryBuilder.Build(new List<ChatSessionLog> { Log(SessionId, entries) });

            Assert.That(summaries, Has.Count.EqualTo(1));
            return summaries[0];
        }

        [Test]
        public void Build_WithoutLogs_ReturnsEmpty()
        {
            Assert.That(ChatSessionSummaryBuilder.Build(null), Is.Empty);
            Assert.That(ChatSessionSummaryBuilder.Build(new List<ChatSessionLog>()), Is.Empty);
        }

        [Test]
        public void Build_WithNonPositiveLimit_ReturnsEmpty()
        {
            var logs = new List<ChatSessionLog> { Log(SessionId, Entry("user", "q", "2025-01-01T10:00:00Z")) };

            Assert.That(ChatSessionSummaryBuilder.Build(logs, 0), Is.Empty);
            Assert.That(ChatSessionSummaryBuilder.Build(logs, -1), Is.Empty);
        }

        [Test]
        public void Build_LogWithoutEntries_IsSkipped()
        {
            var logs = new List<ChatSessionLog>
            {
                new ChatSessionLog { SessionId = "empty", Entries = new List<ChatLogEntry>() },
                new ChatSessionLog { SessionId = "null" }
            };

            Assert.That(ChatSessionSummaryBuilder.Build(logs), Is.Empty);
        }

        [Test]
        public void Build_Prompt_IsLastUserMessage()
        {
            var summary = BuildSingle(
                Entry("user", "first question", "2025-01-01T10:00:00Z"),
                Entry("assistant", "answer", "2025-01-01T10:00:01Z", "alpha"),
                Entry("user", "second question", "2025-01-01T10:00:02Z"));

            Assert.That(summary.Prompt, Is.EqualTo("second question"));
        }

        [Test]
        public void Build_Prompt_IsTheLastUserMessage_AfterCompactionPair()
        {
            // A compaction starts the session with a service pair; the question typed afterwards is the last user
            // text, so the "last one wins" rule alone already gives the card its topic.
            var summary = BuildSingle(
                Entry("user", ChatMessagePlaceholders.SummaryRequest, "2025-01-01T10:00:00Z"),
                Entry("assistant", "Summary of the earlier part of this session: ...", "2025-01-01T10:00:01Z"),
                Entry("user", "real question", "2025-01-01T10:00:02Z"));

            Assert.That(summary.Prompt, Is.EqualTo("real question"));
        }

        [TestCase(ChatMessagePlaceholders.SummaryRequest)]
        [TestCase(ChatMessagePlaceholders.ForcedSummaryUser)]
        public void Build_Prompt_SkipsTheServiceText_EvenWhenItIsTheLastUserMessage(string serviceText)
        {
            // Only an actual skip keeps the topic: here the last user line is a text the app wrote itself.
            var summary = BuildSingle(
                Entry("user", "real question", "2025-01-01T10:00:00Z"),
                Entry("assistant", "answer", "2025-01-01T10:00:01Z", "alpha"),
                Entry("user", serviceText, "2025-01-01T10:00:02Z"));

            Assert.That(summary.Prompt, Is.EqualTo("real question"));
        }

        [Test]
        public void Build_Prompt_SkipsTheServiceText_SurroundedByWhitespace()
        {
            var summary = BuildSingle(
                Entry("user", "real question", "2025-01-01T10:00:00Z"),
                Entry("user", "  " + ChatMessagePlaceholders.SummaryRequest + "\n", "2025-01-01T10:00:02Z"));

            Assert.That(summary.Prompt, Is.EqualTo("real question"));
        }

        [TestCase(ChatMessagePlaceholders.SummaryRequest)]
        [TestCase(ChatMessagePlaceholders.ForcedSummaryUser)]
        public void Build_Prompt_FallsBackToTheServiceText_WhenTheSessionHasNothingElse(string serviceText)
        {
            // A compacted session the user never wrote in: an empty title would be worse than the service text.
            var summary = BuildSingle(
                Entry("user", serviceText, "2025-01-01T10:00:00Z"),
                Entry("assistant", "summary body", "2025-01-01T10:00:01Z"));

            Assert.That(summary.Prompt, Is.EqualTo(serviceText));
        }

        [Test]
        public void Build_Prompt_IgnoresWhitespaceOnlyUserLines()
        {
            var summary = BuildSingle(
                Entry("user", "real question", "2025-01-01T10:00:00Z"),
                Entry("user", "   ", "2025-01-01T10:00:02Z"));

            Assert.That(summary.Prompt, Is.EqualTo("real question"));
        }

        [Test]
        public void Build_Prompt_IsTruncatedAboveTheLimit()
        {
            var summary = BuildSingle(Entry("user", new string('x', 201), "2025-01-01T10:00:00Z"));

            Assert.That(summary.Prompt.Length, Is.EqualTo(200 + 3), "prompt must be cut to 200 chars plus an ellipsis");
            Assert.That(summary.Prompt.EndsWith("..."), Is.True);
        }

        [TestCase(199)]
        [TestCase(200)]
        public void Build_PromptUpToTheLimit_IsNotTruncated(int length)
        {
            var summary = BuildSingle(Entry("user", new string('x', length), "2025-01-01T10:00:00Z"));

            Assert.That(summary.Prompt.Length, Is.EqualTo(length));
            Assert.That(summary.Prompt.EndsWith("..."), Is.False);
        }

        [Test]
        public void Build_SessionWithoutUserMessage_HasEmptyPrompt()
        {
            var summary = BuildSingle(Entry("assistant", "no user before", "2025-01-01T10:00:00Z", "alpha"));

            Assert.That(summary.Prompt, Is.Empty);
        }

        [Test]
        public void Build_MessageCount_CountsEveryEntry()
        {
            var summary = BuildSingle(
                Entry("user", "q", "2025-01-01T10:00:00Z"),
                Entry("assistant", null, "2025-01-01T10:00:01Z", "alpha"),
                Entry("tool", "result", "2025-01-01T10:00:02Z"),
                Entry("assistant", "a", "2025-01-01T10:00:03Z", "alpha"));

            Assert.That(summary.MessageCount, Is.EqualTo(4));
        }

        [Test]
        public void Build_Models_AreDistinctAssistantIdsInChronologicalOrder()
        {
            var summary = BuildSingle(
                Entry("user", "q", "2025-01-01T10:00:00Z"),
                Entry("assistant", null, "2025-01-01T10:00:01Z", "alpha"),
                Entry("tool", "result", "2025-01-01T10:00:02Z"),
                Entry("assistant", "a", "2025-01-01T10:00:03Z", "alpha"),
                Entry("user", "follow up", "2025-01-01T10:00:04Z"),
                Entry("assistant", "a2", "2025-01-01T10:00:05Z", "beta"));

            Assert.That(summary.Models, Is.EqualTo(new[] { "alpha", "beta" }),
                "models must be unique, in order of first use, and must not come from user/tool lines");
        }

        [Test]
        public void Build_Models_DoNotComeFromNonAssistantLines()
        {
            var summary = BuildSingle(
                Entry("user", "q", "2025-01-01T10:00:00Z", "user-model"),
                Entry("tool", "result", "2025-01-01T10:00:01Z", "tool-model"));

            Assert.That(summary.Models, Is.Empty);
        }

        [Test]
        public void Build_Models_AreEmptyForLegacyLogsWithoutModelId()
        {
            var summary = BuildSingle(
                Entry("user", "q", "2025-01-01T10:00:00Z"),
                Entry("assistant", "a", "2025-01-01T10:00:01Z"));

            Assert.That(summary.Models, Is.Empty, "legacy logs carry no model field - nothing must be invented");
        }

        [Test]
        public void Build_Models_SkipBlankIds()
        {
            var summary = BuildSingle(
                Entry("assistant", "a", "2025-01-01T10:00:00Z", "   "),
                Entry("assistant", "b", "2025-01-01T10:00:01Z", "alpha"));

            Assert.That(summary.Models, Is.EqualTo(new[] { "alpha" }));
        }

        [Test]
        public void Build_Timestamp_IsTheLastActivityInRoundTripFormat()
        {
            var summary = BuildSingle(
                Entry("user", "q", "2025-01-01T10:00:00Z"),
                Entry("assistant", "a", "2025-01-01T11:30:00Z", "alpha"));

            Assert.That(ParseTimestamp(summary.Timestamp), Is.EqualTo(new DateTime(2025, 1, 1, 11, 30, 0, DateTimeKind.Utc)));
            Assert.That(summary.Timestamp, Does.EndWith("Z"), "the frontend parses the value with new Date()");
        }

        [Test]
        public void Build_Timestamp_IsEmptyWhenTheLogCarriesNoTimestamp()
        {
            var summary = BuildSingle(Entry("user", "q", null));

            Assert.That(summary.Timestamp, Is.Empty);
        }

        [Test]
        public void Build_OrdersSessionsByLastActivity_NewestFirst()
        {
            var logs = new List<ChatSessionLog>
            {
                Log("older", Entry("user", "older question", "2025-01-01T09:00:00Z")),
                Log("newer", Entry("user", "newer question", "2025-01-02T09:00:00Z")),
                Log("middle", Entry("user", "middle question", "2025-01-01T20:00:00Z"))
            };

            var summaries = ChatSessionSummaryBuilder.Build(logs);

            Assert.That(summaries.Select(summary => summary.SessionId), Is.EqualTo(new[] { "newer", "middle", "older" }));
        }

        [Test]
        public void Build_WithLimit_KeepsTheMostRecentlyActiveSessions()
        {
            var logs = new List<ChatSessionLog>
            {
                Log("s1", Entry("user", "prompt 0", "2025-01-01T10:00:00Z")),
                Log("s2", Entry("user", "prompt 1", "2025-01-01T10:00:01Z")),
                Log("s3", Entry("user", "prompt 2", "2025-01-01T10:00:02Z"))
            };

            var summaries = ChatSessionSummaryBuilder.Build(logs, 2);

            Assert.That(summaries.Select(summary => summary.SessionId), Is.EqualTo(new[] { "s3", "s2" }));
        }
    }
}
