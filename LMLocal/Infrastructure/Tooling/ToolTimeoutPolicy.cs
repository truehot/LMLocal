using System;
using LMLocal.Infrastructure.Tooling.Abstractions;

namespace LMLocal.Infrastructure.Tooling
{
    /// <summary>
    /// Single place for the timeout arithmetic shared by tool sources:
    /// <c>Applied = min(Default, Available − Grace)</c> inside the tool, <c>External = Default + Grace</c> for the watchdog.
    /// </summary>
    internal static class ToolTimeoutPolicy
    {
        /// <summary>
        /// Default grace added to a tool's declared default so the tool has time to report its own timeout
        /// before the external watchdog fires.
        /// </summary>
        public const int DefaultGraceSeconds = 10;

        /// <summary>
        /// External watchdog duration: the tool's own default plus the grace it needs to answer.
        /// </summary>
        public static TimeSpan? ResolveExternal(ToolExecutionLimits limits)
        {
            if (limits == null)
                return null;

            return limits.DefaultTimeout + ResolveGrace(limits);
        }

        /// <summary>
        /// What the tool may actually run: its declared default, cut down to what the caller can spare after the grace
        /// is held back.
        /// </summary>
        public static TimeSpan? ResolveApplied(ToolExecutionLimits limits, TimeSpan? available)
        {
            if (limits == null)
                return null;

            if (!available.HasValue)
                return limits.DefaultTimeout;

            var effective = EffectiveAvailable(limits, available);
            return effective < limits.DefaultTimeout ? effective : limits.DefaultTimeout;
        }

        /// <summary>
        /// True when the caller has no time left for this tool once the grace is held back: it must not be started, and
        /// the model must be told why instead of receiving a timeout from a tool that never had a chance to finish.
        /// </summary>
        public static bool IsBudgetExhausted(ToolExecutionLimits limits, TimeSpan? available)
        {
            if (limits == null || !available.HasValue)
                return false;

            return EffectiveAvailable(limits, available) <= TimeSpan.Zero;
        }

        /// <summary>
        /// Time the tool may run once its grace is subtracted from the caller-provided budget.
        /// </summary>
        public static TimeSpan EffectiveAvailable(ToolExecutionLimits limits, TimeSpan? available)
        {
            if (limits == null || !available.HasValue)
                return available ?? TimeSpan.Zero;

            return available.Value - ResolveGrace(limits);
        }

        /// <summary>
        /// Rounds a duration to whole seconds; used for second-based waits and for the reported applied value.
        /// </summary>
        public static int ToWholeSeconds(TimeSpan value)
            => (int)Math.Round(value.TotalSeconds, MidpointRounding.AwayFromZero);

        private static TimeSpan ResolveGrace(ToolExecutionLimits limits)
            => limits.Grace ?? TimeSpan.FromSeconds(DefaultGraceSeconds);
    }
}
