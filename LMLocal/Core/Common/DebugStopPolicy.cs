using LMLocal.Core.Models;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// Decides which debugger stops are offered to the user.
    /// </summary>
    internal static class DebugStopPolicy
    {
        /// <summary>
        /// True when a stop with the given reason deserves the "Explain" banner.
        /// </summary>
        public static bool ShouldShow(DebugStopReason reason)
        {
            return GetKind(reason) != DebugStopKind.None;
        }

        /// <summary>
        /// Maps the name of an automation event reason onto the Core enum.
        /// </summary>
        public static DebugStopReason FromEventReasonName(string reasonName)
        {
            switch ((reasonName ?? string.Empty).Trim())
            {
                case "dbgEventReasonBreakpoint": return DebugStopReason.Breakpoint;
                case "dbgEventReasonExceptionThrown": return DebugStopReason.ExceptionThrown;
                case "dbgEventReasonExceptionNotHandled": return DebugStopReason.ExceptionNotHandled;
                case "dbgEventReasonUserBreak": return DebugStopReason.UserBreak;
                case "dbgEventReasonStep": return DebugStopReason.Step;
                case "dbgEventReasonGo": return DebugStopReason.Go;
                case "dbgEventReasonAttachProgram": return DebugStopReason.AttachProgram;
                case "dbgEventReasonDetachProgram": return DebugStopReason.DetachProgram;
                case "dbgEventReasonLaunchProgram": return DebugStopReason.LaunchProgram;
                case "dbgEventReasonEndProgram": return DebugStopReason.EndProgram;
                case "dbgEventReasonStopDebugging": return DebugStopReason.StopDebugging;
                case "dbgEventReasonContextSwitch": return DebugStopReason.ContextSwitch;
                default: return DebugStopReason.None;
            }
        }

        /// <summary>
        /// How the stop is presented, or <see cref="DebugStopKind.None"/> when it is not shown at all.
        /// </summary>
        public static DebugStopKind GetKind(DebugStopReason reason)
        {
            switch (reason)
            {
                case DebugStopReason.Breakpoint:
                case DebugStopReason.UserBreak:
                    return DebugStopKind.Breakpoint;

                case DebugStopReason.ExceptionThrown:
                case DebugStopReason.ExceptionNotHandled:
                    return DebugStopKind.Exception;

                default:
                    return DebugStopKind.None;
            }
        }
    }
}
