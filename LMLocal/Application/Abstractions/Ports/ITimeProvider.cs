using System;

namespace LMLocal.Application.Abstractions.Ports
{
    /// <summary>
    /// Source of the current time and of the local time zone, so code that depends on "now" or on local days can be
    /// tested with a fixed clock and a fixed zone instead of the machine's.
    /// </summary>
    internal interface ITimeProvider
    {
        /// <summary>Current UTC time in ticks; the cheap form for hot paths such as the token speed meter.</summary>
        long UtcNowTicks();

        /// <summary>Current UTC time.</summary>
        DateTime UtcNow { get; }

        /// <summary>Zone that local days and "today" are computed in.</summary>
        TimeZoneInfo LocalTimeZone { get; }
    }
}
