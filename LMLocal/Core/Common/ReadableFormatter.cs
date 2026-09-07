using System;
using System.Globalization;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// Formats token counts, durations and throughput into compact human-readable strings for status messages.
    /// </summary>
    internal static class ReadableFormatter
    {
        /// <summary>
        /// Formats a token count as a short human-readable value (e.g. "950", "12.5k", "3.2M").
        /// </summary>
        public static string FormatTokens(int? tokens)
        {
            if (!tokens.HasValue)
                return "0";

            var value = tokens.Value;
            if (value < 1000)
                return value.ToString(CultureInfo.InvariantCulture);

            if (value < 1_000_000)
            {
                var k = value / 1000.0;
                var format = value % 1000 == 0 ? "0" : "0.0";
                return $"{k.ToString(format, CultureInfo.InvariantCulture)}k";
            }

            return $"{(value / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture)}M";
        }

        /// <summary>
        /// Formats a duration in milliseconds as a human-readable string (e.g. "4.2s", "1m 30s", "2h 15m").
        /// </summary>
        public static string FormatDuration(long ms)
        {
            if (ms < 0)
                ms = 0;

            var totalSeconds = ms / 1000.0;
            if (totalSeconds < 60)
                return $"{totalSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s";

            var roundedTotalSeconds = (int)Math.Round(totalSeconds, MidpointRounding.AwayFromZero);
            var minutes = roundedTotalSeconds / 60;
            var seconds = roundedTotalSeconds % 60;

            if (minutes < 60)
                return $"{minutes}m {seconds}s";

            var hours = minutes / 60;
            var remMinutes = minutes % 60;
            return $"{hours}h {remMinutes}m";
        }

        /// <summary>
        /// Formats a measured token throughput (tokens per second) with one decimal.
        /// </summary>
        public static string FormatTokensPerSecond(double tokensPerSecond)
        {
            if (tokensPerSecond <= 0 || double.IsNaN(tokensPerSecond) || double.IsInfinity(tokensPerSecond))
                return null;

            return tokensPerSecond.ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Truncates a string to a maximum length, appending an ellipsis and line break when it was cut.
        /// </summary>
        public static string Truncate(string value)
        {
            const int max = 120;
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value;
            return value.Substring(0, max) + "...\r\n";
        }
    }
}
