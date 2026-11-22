using System;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Why Visual Studio entered Break Mode, reduced to the values the chat UI distinguishes.
    /// </summary>
    public enum DebugStopReason
    {
        /// <summary>No stop: the debugger is running, in design mode, or not debugging at all.</summary>
        None = 0,

        /// <summary>A breakpoint was hit.</summary>
        Breakpoint,

        /// <summary>An exception crossed the first-chance handler and the debugger stopped.</summary>
        ExceptionThrown,

        /// <summary>An exception was not handled and the debugger stopped.</summary>
        ExceptionNotHandled,

        /// <summary>The user pressed Pause (Break All).</summary>
        UserBreak,

        /// <summary>A step (F10/F11) ended. Never shown in the UI.</summary>
        Step,

        /// <summary>Execution continued. Hides the banner.</summary>
        Go,

        /// <summary>The debugger attached to a process. Hides the banner.</summary>
        AttachProgram,

        /// <summary>The debugger detached from a process. Hides the banner.</summary>
        DetachProgram,

        /// <summary>A debugged process was launched. Hides the banner.</summary>
        LaunchProgram,

        /// <summary>The debugged program ended. Hides the banner.</summary>
        EndProgram,

        /// <summary>Debugging was stopped. Hides the banner.</summary>
        StopDebugging,

        /// <summary>The debugger switched to another thread or frame. Hides the banner.</summary>
        ContextSwitch
    }

    /// <summary>
    /// What a stop is about: drives the accent of the banner and the instruction of the prompt.
    /// </summary>
    public enum DebugStopKind
    {
        /// <summary>The stop is not offered to the user.</summary>
        None = 0,

        /// <summary>Breakpoint or user break.</summary>
        Breakpoint,

        /// <summary>Exception, thrown or not handled.</summary>
        Exception
    }

    /// <summary>
    /// The cheap part of a debugger stop: read on every debugger event, without stack frames and
    /// without variable values, because evaluating them at the moment of the stop can fight with the
    /// Exception Helper.
    /// </summary>
    public sealed class DebugStopInfo : IEquatable<DebugStopInfo>
    {
        /// <summary>Why the debugger stopped.</summary>
        public DebugStopReason Reason { get; set; }

        /// <summary>How the stop is presented; <see cref="DebugStopKind.None"/> when it is not shown at all.</summary>
        public DebugStopKind Kind { get; set; }

        /// <summary>Full path of the source file of the stop. Null when no file is known (no PDB, native code).</summary>
        public string File { get; set; }

        /// <summary>One-based line of the stop, 0 when unknown.</summary>
        public int Line { get; set; }

        /// <summary>
        /// Identity of the stop, used to drop repeated events so that the banner does not flicker.
        /// </summary>
        public bool Equals(DebugStopInfo other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;

            return Reason == other.Reason
                && Line == other.Line
                && string.Equals(File, other.File, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj) => Equals(obj as DebugStopInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + Reason.GetHashCode();
                hash = hash * 23 + Line.GetHashCode();
                hash = hash * 23 + (File != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(File) : 0);
                return hash;
            }
        }
    }
}
