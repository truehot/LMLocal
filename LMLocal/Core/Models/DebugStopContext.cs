using System.Collections.Generic;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Everything read for one debugger stop: the cheap record plus the exception chain, the top
    /// stack frames and the local variables of the current frame.
    /// </summary>
    public sealed class DebugStopContext
    {
        /// <summary>The stop this context belongs to.</summary>
        public DebugStopInfo Info { get; set; }

        /// <summary>
        /// Exception chain, outermost first.
        /// </summary>
        public IReadOnlyList<DebugExceptionInfo> Exceptions { get; set; } = new List<DebugExceptionInfo>();

        /// <summary>Top stack frames, innermost first. A frame without a PDB has no <see cref="DebugStackFrameInfo.File"/>.</summary>
        public IReadOnlyList<DebugStackFrameInfo> Frames { get; set; } = new List<DebugStackFrameInfo>();

        /// <summary>Local variables of the current frame; sensitive names carry <see cref="DebugLocalVariable.HiddenValue"/>.</summary>
        public IReadOnlyList<DebugLocalVariable> Locals { get; set; } = new List<DebugLocalVariable>();
    }
}
