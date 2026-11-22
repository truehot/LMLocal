using System;

namespace LMLocal.Core.Models.Usage
{
    /// <summary>
    /// Accepted values of <see cref="UsageRecord.Origin"/>.
    /// </summary>
    public static class UsageOrigins
    {
        /// <summary>Normal interactive work; shown in the dialog.</summary>
        public const string Chat = "chat";

        /// <summary>A run started through <c>IpcCommandHandler</c> (benchmarks); written but never shown.</summary>
        public const string Ipc = "ipc";

        /// <summary>
        /// True when the record came from an IPC run and must be left out of the dialog.
        /// </summary>
        public static bool IsIpc(string origin)
        {
            return string.Equals(origin, Ipc, StringComparison.OrdinalIgnoreCase);
        }
    }
}
