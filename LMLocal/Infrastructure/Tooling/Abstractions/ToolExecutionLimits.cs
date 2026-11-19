using System;

namespace LMLocal.Infrastructure.Tooling.Abstractions
{
    /// <summary>
    /// Timeout budget declared by a built-in tool: the single source of truth for how long the tool normally runs and
    /// how much extra time the external watchdog grants it.
    /// </summary>
    internal sealed class ToolExecutionLimits
    {
        public ToolExecutionLimits(TimeSpan defaultTimeout, TimeSpan? grace = null)
        {
            DefaultTimeout = defaultTimeout;
            Grace = grace;
        }

        /// <summary>
        /// Applied when the caller supplies no smaller budget (main chat, IPC).
        /// </summary>
        public TimeSpan DefaultTimeout { get; }

        /// <summary>
        /// Extra time the external watchdog grants the tool to report its own timeout. Null = use the policy default.
        /// </summary>
        public TimeSpan? Grace { get; }
    }
}
