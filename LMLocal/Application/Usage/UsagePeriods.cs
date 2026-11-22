using System;

namespace LMLocal.Application.Usage
{
    /// <summary>
    /// Periods the stats dialog can show.
    /// </summary>
    internal enum UsagePeriod
    {
        /// <summary>The session that is open in the chat right now.</summary>
        Current,

        /// <summary>Local midnight of today until now.</summary>
        Today,

        /// <summary>The whole local day before today.</summary>
        Yesterday,

        /// <summary>Today and the two days before it.</summary>
        Days3,

        /// <summary>Today and the six days before it.</summary>
        Days7,

        /// <summary>Today and the twenty-nine days before it.</summary>
        Days30
    }

    /// <summary>
    /// Wire names and local boundaries of the periods.
    /// </summary>
    internal static class UsagePeriods
    {
        internal const string CurrentWireName = "current";
        internal const string TodayWireName = "today";
        internal const string YesterdayWireName = "yesterday";
        internal const string Days3WireName = "3d";
        internal const string Days7WireName = "7d";
        internal const string Days30WireName = "30d";

        /// <summary>
        /// Parses the value the dialog sends; an unknown value falls back to <see cref="UsagePeriod.Current"/>.
        /// </summary>
        internal static UsagePeriod Parse(string value)
        {
            string text = (value ?? string.Empty).Trim().ToLowerInvariant();

            switch (text)
            {
                case TodayWireName:
                    return UsagePeriod.Today;

                case YesterdayWireName:
                    return UsagePeriod.Yesterday;

                case Days3WireName:
                    return UsagePeriod.Days3;

                case Days7WireName:
                    return UsagePeriod.Days7;

                case Days30WireName:
                    return UsagePeriod.Days30;

                default:
                    return UsagePeriod.Current;
            }
        }

        internal static string ToWireName(UsagePeriod period)
        {
            switch (period)
            {
                case UsagePeriod.Today:
                    return TodayWireName;

                case UsagePeriod.Yesterday:
                    return YesterdayWireName;

                case UsagePeriod.Days3:
                    return Days3WireName;

                case UsagePeriod.Days7:
                    return Days7WireName;

                case UsagePeriod.Days30:
                    return Days30WireName;

                default:
                    return CurrentWireName;
            }
        }

        /// <summary>
        /// Local start of the period, or null for <see cref="UsagePeriod.Current"/>, which is addressed by session id.
        /// </summary>
        internal static DateTime? StartLocal(UsagePeriod period, DateTime nowLocal)
        {
            switch (period)
            {
                case UsagePeriod.Today:
                    return nowLocal.Date;

                case UsagePeriod.Yesterday:
                    return nowLocal.Date.AddDays(-1);

                case UsagePeriod.Days3:
                    return nowLocal.Date.AddDays(-2);

                case UsagePeriod.Days7:
                    return nowLocal.Date.AddDays(-6);

                case UsagePeriod.Days30:
                    return nowLocal.Date.AddDays(-29);

                default:
                    return null;
            }
        }

        /// <summary>
        /// Local end of the period, exclusive, or null when the period runs until now.
        /// </summary>
        internal static DateTime? EndLocal(UsagePeriod period, DateTime nowLocal)
        {
            return period == UsagePeriod.Yesterday ? nowLocal.Date : (DateTime?)null;
        }
    }
}
