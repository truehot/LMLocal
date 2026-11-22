using System;
using System.Collections.Generic;
using System.Linq;
using LMLocal.Application.Usage;
using LMLocal.Core.Models.Usage;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.Usage
{
    /// <summary>
    /// Covers the numbers the dialog shows: which records count as requests and failures, how the speed of a group is
    /// summed, how models and sessions are grouped and how the context peak is taken.
    /// </summary>
    [TestFixture]
    public class UsageAggregatorTests
    {
        private static UsageRecord Main(long durationMs = 1000, UsageOutcome outcome = UsageOutcome.Ok, string session = "s1")
        {
            return new UsageRecord
            {
                Timestamp = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc),
                SessionId = session,
                RequestId = "r1",
                Source = UsageSource.Main,
                Origin = UsageOrigins.Chat,
                Provider = "lmstudio",
                BaseUrl = "http://localhost:1234",
                Model = "qwen3-coder-30b",
                Prompt = 1000,
                Completion = 100,
                DurationMs = durationMs,
                Outcome = outcome
            };
        }

        [Test]
        public void Aggregate_NoRecords_HasNoData()
        {
            UsageStats stats = UsageAggregator.Aggregate(new UsageRecord[0]);

            Assert.That(stats.HasData, Is.False);
            Assert.That(stats.Sources, Is.Empty);
            Assert.That(stats.Models, Is.Empty);
            Assert.That(stats.Days, Is.Empty);
        }

        [Test]
        public void Aggregate_RequestsAndTime_CountOnlyTheMainChat()
        {
            var subagent = new UsageRecord
            {
                SessionId = "s1",
                Source = UsageSource.Subagent,
                Prompt = 500,
                Completion = 50,
                DurationMs = 9000
            };

            UsageStats stats = UsageAggregator.Aggregate(new[] { Main(), subagent });

            Assert.That(stats.Totals.Requests, Is.EqualTo(1), "A subagent run is not a user request.");
            Assert.That(stats.Totals.DurationMs, Is.EqualTo(1000), "The subagent time is already inside the main request.");
            Assert.That(stats.Totals.Prompt, Is.EqualTo(1500), "Tokens are still counted for every source.");
            Assert.That(stats.Sources.Single(s => s.Source == UsageSource.Subagent).DurationMs, Is.EqualTo(9000),
                "The subagent row shows its own time, outside the totals.");
        }

        [Test]
        public void Aggregate_SubagentTime_CountsParallelRunsOnce()
        {
            var start = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

            UsageRecord Run(int startSecond, long durationMs) => new UsageRecord
            {
                SessionId = "s1",
                Source = UsageSource.Subagent,
                Timestamp = start.AddSeconds(startSecond),
                DurationMs = durationMs
            };

            // A batch of three started together (0-80s, 0-90s, 10-60s), then one more after a pause (100-120s).
            UsageStats stats = UsageAggregator.Aggregate(new[]
            {
                Main(), Run(0, 80000), Run(0, 90000), Run(10, 50000), Run(100, 20000)
            });

            Assert.That(stats.Sources.Single(s => s.Source == UsageSource.Subagent).DurationMs, Is.EqualTo(110000),
                "90s of the batch plus 20s of the last run; the pause and the overlaps are not counted.");
        }

        [Test]
        public void Aggregate_Failed_ExcludesCancelled()
        {
            var cancelled = Main(outcome: UsageOutcome.Cancelled);
            var error = Main(outcome: UsageOutcome.Error);
            var timeout = Main(outcome: UsageOutcome.Timeout);
            var incomplete = Main(outcome: UsageOutcome.Incomplete);

            UsageStats stats = UsageAggregator.Aggregate(new[] { cancelled, error, timeout, incomplete });

            Assert.That(stats.Totals.Failed, Is.EqualTo(3), "A user stop is not a failure.");
            Assert.That(stats.Sources.Single(r => r.Source == UsageSource.Main).Failed, Is.EqualTo(3));
        }

        [Test]
        public void Aggregate_EmptySourceGroupsAreNotShown()
        {
            UsageStats stats = UsageAggregator.Aggregate(new[] { Main() });

            Assert.That(stats.Sources.Count, Is.EqualTo(1));
            Assert.That(stats.Sources[0].Source, Is.EqualTo(UsageSource.Main));
        }

        [Test]
        public void Aggregate_ModelSpeed_IsSummedAndNeverAveraged()
        {
            // 100 tokens at 10 t/s spend 10 s, 100 tokens at 20 t/s spend 5 s: the group ran 200 tokens in 15 s.
            var slow = Main();
            slow.GenCompletion = 100;
            slow.GenMs = 10000;

            var fast = Main(session: "s2");
            fast.GenCompletion = 100;
            fast.GenMs = 5000;

            UsageStats stats = UsageAggregator.Aggregate(new[] { slow, fast });

            UsageModelRow row = stats.Models.Single();
            Assert.That(row.TokensPerSecond, Is.EqualTo(13.3).Within(0.1), "Averaging the two speeds would give 15.");
        }

        [Test]
        public void Aggregate_ModelsAreKeyedByProviderAddressAndModel()
        {
            var otherAddress = Main(session: "s2");
            otherAddress.BaseUrl = "http://192.168.1.5:1234";

            UsageStats stats = UsageAggregator.Aggregate(new[] { Main(), otherAddress });

            Assert.That(stats.Models.Count, Is.EqualTo(2), "Two LM Studio instances are two rows.");
        }

        [Test]
        public void Aggregate_CacheBase_CountsOnlyTheRequestsThatReportedTheCache()
        {
            var reported = Main();
            reported.Cached = 800;

            var silent = Main(session: "s2");
            silent.Cached = null;

            UsageStats stats = UsageAggregator.Aggregate(new[] { reported, silent });

            Assert.That(stats.Models.Count, Is.EqualTo(1));
            Assert.That(stats.Models[0].Prompt, Is.EqualTo(2000));
            Assert.That(stats.Models[0].Cached, Is.EqualTo(800));
            Assert.That(stats.Models[0].CacheReportedPrompt, Is.EqualTo(1000),
                "A request with no cached count must not dilute the share.");
        }

        [Test]
        public void Aggregate_PeakContext_IsTheWorstRatioOfTheKnownWindows()
        {
            var known = Main();
            known.ContextPeak = 8000;
            known.ContextMax = 32768;

            var unknown = Main(session: "s2");
            unknown.ContextPeak = 90000;

            UsageStats stats = UsageAggregator.Aggregate(new[] { known, unknown });

            Assert.That(stats.Totals.PeakContextPercent.Value, Is.EqualTo(8000d / 32768d).Within(0.001));

            UsageStats withoutWindows = UsageAggregator.Aggregate(new[] { unknown });
            Assert.That(withoutWindows.Totals.PeakContextPercent, Is.Null);
        }

        [Test]
        public void Aggregate_SessionsAreGroupedByLocalDayAndTitleComesFromTheFirstNonEmpty()
        {
            var first = Main();
            first.Title = null;
            first.Timestamp = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);

            var second = Main();
            second.Title = "тест рефакторинга stream";
            second.Timestamp = new DateTime(2026, 11, 20, 11, 0, 0, DateTimeKind.Utc);

            var otherSession = Main(session: "s2");
            otherSession.Title = "другая сессия";
            otherSession.Timestamp = new DateTime(2026, 11, 19, 11, 0, 0, DateTimeKind.Utc);

            UsageStats stats = UsageAggregator.Aggregate(new[] { first, second, otherSession });

            Assert.That(stats.Days.Count, Is.EqualTo(2));
            Assert.That(stats.Days[0].DateLocal, Is.GreaterThan(stats.Days[1].DateLocal), "The newest day comes first.");

            UsageDayGroup day = stats.Days.Single(d => d.Sessions.Any(s => s.SessionId == "s1"));
            UsageSessionRow session = day.Sessions.Single();

            Assert.That(session.Title, Is.EqualTo("тест рефакторинга stream"));
            Assert.That(session.Requests, Is.EqualTo(2));
            Assert.That(session.Sources, Is.Not.Empty, "The card carries the blocks it shows when expanded.");
            Assert.That(session.Models, Is.Not.Empty);
        }

        [Test]
        public void Aggregate_SessionCountsSubagentTokensButOnlyMainRequests()
        {
            var main = Main();
            var subagent = new UsageRecord
            {
                Timestamp = main.Timestamp.AddSeconds(5),
                SessionId = "s1",
                Source = UsageSource.Subagent,
                Prompt = 400,
                Completion = 40,
                Outcome = UsageOutcome.Timeout
            };

            UsageStats stats = UsageAggregator.Aggregate(new[] { main, subagent });

            UsageSessionRow session = stats.Days.SelectMany(d => d.Sessions).Single();
            Assert.That(session.Prompt, Is.EqualTo(1400));
            Assert.That(session.Requests, Is.EqualTo(1));
            Assert.That(session.Failed, Is.EqualTo(1));
            Assert.That(session.Sources.Count, Is.EqualTo(2));
        }
    }
}
