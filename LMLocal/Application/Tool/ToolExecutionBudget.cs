using System;

namespace LMLocal.Application.Tool
{
    /// <summary>
    /// How much time the caller can give a tool execution.
    /// </summary>
    public sealed class ToolExecutionBudget
    {
        public ToolExecutionBudget(TimeSpan? available)
        {
            Available = available;
        }

        /// <summary>Time the tool may take, already reduced by the caller's reserve. Null = unlimited.</summary>
        public TimeSpan? Available { get; }
    }
}
