using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Usage;
using LMLocal.Core.Models.Usage;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.Usage
{
    /// <summary>
    /// Covers what the service adds on top of the aggregator: the start of a period in the local zone of the time
    /// provider, the filter by that start and the exclusion of IPC runs. The clock and the zone are fixed, so the
    /// result never depends on the machine.
    /// </summary>
    [TestFixture]
    public class UsageStatsServiceTests
    {
        /// <summary>UTC+3 without daylight time: local midnight is 21:00 UTC of the previous day.</summary>
        private static readonly TimeZoneInfo Plus3 =
            TimeZoneInfo.CreateCustomTimeZone("Test+3", TimeSpan.FromHours(3), "Test+3", "Test+3");

        /// <summary>
        /// Central European pattern: +1 all year, +2 from the last Sunday of March to the last Sunday of October. Used to
        /// check that a period built from local midnights lasts 23 or 25 hours on the days the clock changes.
        /// </summary>
        private static TimeZoneInfo ZoneWithDaylightSaving()
        {
            TimeZoneInfo.AdjustmentRule rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date,
                DateTime.MaxValue.Date,
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday));

            return TimeZoneInfo.CreateCustomTimeZone("Test+Dst", TimeSpan.FromHours(1), "Test+Dst", "Test+Dst", "Test+Dst", new[] { rule });
        }

        /// <summary>2026-11-20 04:00 local (UTC+3).</summary>
        private static readonly DateTime NowUtc = new DateTime(2026, 11, 20, 1, 0, 0, DateTimeKind.Utc);

        private sealed class FakeReader : IUsageReader
        {
            public List<UsageRecord> Records = new List<UsageRecord>();
            public DateTime? LastFromUtc;
            public string LastSessionId;

            public Task<IReadOnlyList<UsageRecord>> ReadAsync(DateTime fromUtc, CancellationToken cancellationToken = default)
            {
                LastFromUtc = fromUtc;
                // Like the store: whole monthly files come back, the service must filter by the start itself.
                return Task.FromResult<IReadOnlyList<UsageRecord>>(Records);
            }

            public Task<IReadOnlyList<UsageRecord>> ReadSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            {
                LastSessionId = sessionId;
                return Task.FromResult<IReadOnlyList<UsageRecord>>(Records.Where(r => r.SessionId == sessionId).ToList());
            }
        }

        private FakeReader _reader;
        private UsageStatsService _service;

        [SetUp]
        public void SetUp()
        {
            _reader = new FakeReader();
            _service = new UsageStatsService(_reader, new FakeTimeProvider(NowUtc, Plus3));
        }

        private static UsageRecord Main(DateTime utc, string session = "s1", string origin = UsageOrigins.Chat, int prompt = 100)
        {
            return new UsageRecord
            {
                Timestamp = utc,
                SessionId = session,
                Source = UsageSource.Main,
                Origin = origin,
                Model = "m",
                Prompt = prompt,
                Completion = 10,
                Outcome = UsageOutcome.Ok
            };
        }

        [Test]
        public async Task Today_StartsAtLocalMidnight_OfTheProviderZone()
        {
            DateTime localMidnightUtc = new DateTime(2026, 11, 19, 21, 0, 0, DateTimeKind.Utc);
            _reader.Records.Add(Main(localMidnightUtc.AddMinutes(-1), prompt: 1));
            _reader.Records.Add(Main(localMidnightUtc, prompt: 10));
            _reader.Records.Add(Main(NowUtc, prompt: 100));

            UsageStats stats = await _service.GetPeriodStatsAsync(UsagePeriod.Today);

            Assert.That(_reader.LastFromUtc, Is.EqualTo(localMidnightUtc), "Today begins at 00:00 local, i.e. 21:00 UTC the day before.");
            Assert.That(stats.Totals.Prompt, Is.EqualTo(110), "A record one minute before local midnight belongs to yesterday.");
            Assert.That(stats.Totals.Requests, Is.EqualTo(2));
        }

        [Test]
        public async Task SevenDays_StartSixLocalDaysBeforeToday()
        {
            await _service.GetPeriodStatsAsync(UsagePeriod.Days7);

            Assert.That(_reader.LastFromUtc, Is.EqualTo(new DateTime(2026, 11, 13, 21, 0, 0, DateTimeKind.Utc)),
                "7d = today and the six local days before it.");
        }

        [Test]
        public async Task Yesterday_CoversTheWholeLocalDayBeforeToday()
        {
            DateTime todayMidnightUtc = new DateTime(2026, 11, 19, 21, 0, 0, DateTimeKind.Utc);
            DateTime yesterdayMidnightUtc = todayMidnightUtc.AddDays(-1);

            _reader.Records.Add(Main(yesterdayMidnightUtc.AddMinutes(-1), prompt: 1));
            _reader.Records.Add(Main(yesterdayMidnightUtc, prompt: 10));
            _reader.Records.Add(Main(todayMidnightUtc.AddMinutes(-1), prompt: 100));
            _reader.Records.Add(Main(todayMidnightUtc, prompt: 1000));

            UsageStats stats = await _service.GetPeriodStatsAsync(UsagePeriod.Yesterday);

            Assert.That(_reader.LastFromUtc, Is.EqualTo(yesterdayMidnightUtc));
            Assert.That(stats.Totals.Prompt, Is.EqualTo(110),
                "Yesterday is [00:00 yesterday, 00:00 today) local: 23:59 belongs to it, 00:00 of today does not.");
            Assert.That(stats.Totals.Requests, Is.EqualTo(2));
            Assert.That(stats.Days, Has.Count.EqualTo(1));
            Assert.That(stats.Days[0].DateLocal, Is.EqualTo(new DateTime(2026, 11, 19)));
        }

        [Test]
        public async Task ThreeDays_StartTwoLocalDaysBeforeToday()
        {
            await _service.GetPeriodStatsAsync(UsagePeriod.Days3);

            Assert.That(_reader.LastFromUtc, Is.EqualTo(new DateTime(2026, 11, 17, 21, 0, 0, DateTimeKind.Utc)),
                "3d = today and the two local days before it.");
        }

        [Test]
        public async Task Yesterday_OnTheDayTheClockGoesForward_LastsTwentyThreeHours()
        {
            TimeZoneInfo zone = ZoneWithDaylightSaving();

            // 10:00 local on 2026-03-30, the day after the clock jumped forward: 2026-03-29 lasted 23 hours.
            var service = new UsageStatsService(
                _reader,
                new FakeTimeProvider(new DateTime(2026, 3, 30, 8, 0, 0, DateTimeKind.Utc), zone));

            DateTime startUtc = new DateTime(2026, 3, 28, 23, 0, 0, DateTimeKind.Utc);
            DateTime endUtc = new DateTime(2026, 3, 29, 22, 0, 0, DateTimeKind.Utc);

            _reader.Records.Add(Main(startUtc.AddMinutes(-1), prompt: 1));
            _reader.Records.Add(Main(startUtc, prompt: 10));
            _reader.Records.Add(Main(endUtc.AddMinutes(-1), prompt: 100));
            _reader.Records.Add(Main(endUtc, prompt: 1000));

            UsageStats stats = await service.GetPeriodStatsAsync(UsagePeriod.Yesterday);

            Assert.That(_reader.LastFromUtc, Is.EqualTo(startUtc), "00:00 of 29 March is still standard time, +1.");
            Assert.That(stats.Totals.Prompt, Is.EqualTo(110),
                "The day ends at 00:00 of 30 March, already summer time, +2: 23 hours instead of 24.");
        }

        [Test]
        public async Task Days_AreGroupedInTheProviderZone()
        {
            // 22:00 UTC on the 19th is already the 20th in UTC+3.
            _reader.Records.Add(Main(new DateTime(2026, 11, 19, 22, 0, 0, DateTimeKind.Utc)));

            UsageStats stats = await _service.GetPeriodStatsAsync(UsagePeriod.Today);

            Assert.That(stats.Days, Has.Count.EqualTo(1));
            Assert.That(stats.Days[0].DateLocal, Is.EqualTo(new DateTime(2026, 11, 20)));
        }

        [Test]
        public async Task Period_LeavesOutIpcRuns()
        {
            _reader.Records.Add(Main(NowUtc, prompt: 100));
            _reader.Records.Add(Main(NowUtc, origin: UsageOrigins.Ipc, prompt: 5000));

            UsageStats stats = await _service.GetPeriodStatsAsync(UsagePeriod.Today);

            Assert.That(stats.Totals.Prompt, Is.EqualTo(100), "IPC benchmark runs are written but never shown.");
        }

        [Test]
        public async Task Period_WithOnlyIpcRuns_HasNoData()
        {
            _reader.Records.Add(Main(NowUtc, origin: UsageOrigins.Ipc));

            UsageStats stats = await _service.GetPeriodStatsAsync(UsagePeriod.Today);

            Assert.That(stats.HasData, Is.False);
        }

        [Test]
        public async Task Session_ReadsThatSession_AndLeavesOutIpcRuns()
        {
            _reader.Records.Add(Main(NowUtc, session: "s1", prompt: 100));
            _reader.Records.Add(Main(NowUtc, session: "s1", origin: UsageOrigins.Ipc, prompt: 5000));
            _reader.Records.Add(Main(NowUtc, session: "s2", prompt: 7));

            UsageStats stats = await _service.GetSessionStatsAsync("s1");

            Assert.That(_reader.LastSessionId, Is.EqualTo("s1"));
            Assert.That(stats.Totals.Prompt, Is.EqualTo(100));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        public async Task Session_WithoutId_ReadsNothing(string sessionId)
        {
            UsageStats stats = await _service.GetSessionStatsAsync(sessionId);

            Assert.That(stats.HasData, Is.False);
            Assert.That(_reader.LastSessionId, Is.Null, "There is nothing to look for without a session id.");
        }
    }
}
