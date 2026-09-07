namespace LMLocal.Application.Tool
{
    /// <summary>
    /// A progress step emitted while a long-running tool executes.
    /// </summary>
    public sealed class ToolActivityEvent
    {
        /// <summary>Tool/agent function name being executed. Null for status-only events (e.g. "thinking").</summary>
        public string ToolName { get; set; }

        /// <summary>Status text (e.g. "thinking", or the tool name). Falls back to <see cref="ToolName"/>.</summary>
        public string Message { get; set; }

        /// <summary>1-based step number within the current tool run.</summary>
        public int? Step { get; set; }
    }
}
