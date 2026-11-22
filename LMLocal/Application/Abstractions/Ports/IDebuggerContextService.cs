using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Models;

namespace LMLocal.Application.Abstractions.Ports
{
    /// <summary>
    /// Port for the current Visual Studio debugger stop.
    /// </summary>
    internal interface IDebuggerContextService
    {
        /// <summary>
        /// The stop the "Explain" banner is currently offered for, or null when the banner is hidden
        /// (not debugging, running, stepping, stopped, or the feature is disabled).
        /// </summary>
        DebugStopInfo Current { get; }

        /// <summary>
        /// Raised on the UI thread whenever <see cref="Current"/> changes.
        /// </summary>
        event Action StopChanged;

        /// <summary>
        /// Reads the full context of the current stop: exception chain, stack frames and locals.
        /// </summary>
        Task<DebugStopContext> CollectContextAsync(CancellationToken cancellationToken = default);
    }
}
