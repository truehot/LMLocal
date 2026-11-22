using System;
using System.Runtime.CompilerServices;
using LMLocal.Application.Abstractions.Ports;

namespace LMLocal.Infrastructure.Time
{
    /// <summary>
    /// The machine clock and the machine time zone.
    /// </summary>
    internal class SystemTimeProvider : ITimeProvider
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long UtcNowTicks()
        {
            return DateTime.UtcNow.Ticks;
        }

        public DateTime UtcNow
        {
            get { return DateTime.UtcNow; }
        }

        public TimeZoneInfo LocalTimeZone
        {
            get { return TimeZoneInfo.Local; }
        }
    }
}
