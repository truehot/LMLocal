namespace LMLocal.Application.Tool
{
    /// <summary>Progress event of a long-running tool: a progress step, or the live token usage of a SubAgent.</summary>
    public sealed class ToolActivityEvent
    {
        /// <summary>Tool/agent function name being executed. Null for status-only events (e.g. "thinking").</summary>
        public string ToolName { get; set; }

        /// <summary>Status text (e.g. "thinking", or the tool name). Falls back to <see cref="ToolName"/>.</summary>
        public string Message { get; set; }

        /// <summary>1-based step number within the current tool run.</summary>
        public int? Step { get; set; }

        /// <summary>Tokens produced by the run so far; set only on a token event, which is never a step.</summary>
        public int? Tokens { get; set; }

        /// <summary>Live generation speed of the run in tokens per second; null when not known.</summary>
        public double? TokensPerSecond { get; set; }

        /// <summary>Agent of the current pipeline step; empty for a single SubAgent run.</summary>
        public string AgentName { get; set; }

        /// <summary>True when the event reports token usage instead of a progress step.</summary>
        public bool IsTokenEvent
        {
            get { return Tokens.HasValue; }
        }
    }
}
