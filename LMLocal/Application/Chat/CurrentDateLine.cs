using System;
using System.Globalization;

namespace LMLocal.Application.Chat
{
    /// <summary>
    /// Builds the single line with the date and time the chat started, which <see cref="ChatHistoryManager"/> puts at
    /// the end of the main chat's system message once per chat.
    /// </summary>
    internal static class CurrentDateLine
    {
        /// <summary>
        /// Formats <paramref name="utcNow"/> as it reads in <paramref name="zone"/>, e.g.
        /// "Chat started: 2026-11-21 14:05 (Saturday), time zone UTC+02:00." The offset always carries a sign and
        /// hours with minutes, so the line reads the same on every zone.
        /// </summary>
        public static string Format(DateTime utcNow, TimeZoneInfo zone)
        {
            if (zone == null)
                throw new ArgumentNullException(nameof(zone));

            DateTime utc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
            DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
            TimeSpan offset = zone.GetUtcOffset(utc);
            string sign = offset < TimeSpan.Zero ? "-" : "+";

            return string.Concat(
                "Chat started: ",
                local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                " (",
                local.ToString("dddd", CultureInfo.InvariantCulture),
                "), time zone UTC",
                sign,
                offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                ".");
        }
    }
}
