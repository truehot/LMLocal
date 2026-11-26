using System;
using LMLocal.Application.Chat;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.Chat
{
    /// <summary>
    /// Covers the line the chat history puts into the system message: the local date and time, the day name in
    /// English and an offset that always carries a sign and minutes.
    /// </summary>
    [TestFixture]
    public class CurrentDateLineTests
    {
        private static TimeZoneInfo Zone(string id, TimeSpan offset)
        {
            return TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id);
        }

        [Test]
        public void Format_ReadsInTheLocalZone_WithADayNameAndASignedOffset()
        {
            string line = CurrentDateLine.Format(
                new DateTime(2026, 11, 21, 12, 5, 0, DateTimeKind.Utc),
                Zone("Tests/Plus2", TimeSpan.FromHours(2)));

            Assert.That(line, Is.EqualTo("Chat started: 2026-11-21 14:05 (Saturday), time zone UTC+02:00."));
        }

        [Test]
        public void Format_LateEveningUtc_ShowsTheNextLocalDay()
        {
            string line = CurrentDateLine.Format(
                new DateTime(2026, 11, 21, 23, 30, 0, DateTimeKind.Utc),
                Zone("Tests/Plus2", TimeSpan.FromHours(2)));

            Assert.That(line, Is.EqualTo("Chat started: 2026-11-22 01:30 (Sunday), time zone UTC+02:00."));
        }

        [Test]
        public void Format_EarlyMorningUtc_ShowsThePreviousLocalDay()
        {
            string line = CurrentDateLine.Format(
                new DateTime(2026, 11, 21, 2, 0, 0, DateTimeKind.Utc),
                Zone("Tests/Minus5", TimeSpan.FromHours(-5)));

            Assert.That(line, Is.EqualTo("Chat started: 2026-11-20 21:00 (Friday), time zone UTC-05:00."));
        }

        [TestCase(0, 0, "UTC+00:00")]
        [TestCase(-5, 0, "UTC-05:00")]
        [TestCase(5, 30, "UTC+05:30")]
        public void Format_AlwaysWritesTheSignAndTheMinutes(int offsetHours, int offsetMinutes, string expectedOffset)
        {
            var offset = new TimeSpan(offsetHours, offsetMinutes, 0);

            string line = CurrentDateLine.Format(
                new DateTime(2026, 11, 21, 12, 5, 0, DateTimeKind.Utc),
                Zone("Tests/Offset" + expectedOffset, offset));

            Assert.That(line, Does.EndWith("time zone " + expectedOffset + "."));
        }
    }
}
