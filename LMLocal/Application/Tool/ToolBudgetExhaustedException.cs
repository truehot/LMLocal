using System;

namespace LMLocal.Application.Tool
{
    /// <summary>
    /// Thrown when a long-running tool refuses to start because the caller's budget is spent (after the tool's grace is
    /// held back).
    /// </summary>
    public sealed class ToolBudgetExhaustedException : Exception
    {
        public ToolBudgetExhaustedException(string message)
            : base(message)
        {
        }
    }
}
