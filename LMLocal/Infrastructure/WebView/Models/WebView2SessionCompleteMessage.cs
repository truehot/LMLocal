namespace LMLocal.Infrastructure.WebView
{
    /// <summary>
    /// Message containing session completion metadata.
    /// Type = ChatSessionComplete
    /// Sent at the end of generation with aggregated statistics.
    /// </summary>
    internal class WebView2SessionCompleteMessage : WebView2ScriptMessage
    {
        /// <summary>
        /// Reason why generation stopped: "stop", "length", "tool_calls", "content_filter".
        /// </summary>
        public string FinishReason { get; set; }

        /// <summary>
        /// Total tokens used: prompt + completion.
        /// Null if not provided by server.
        /// </summary>
        public int? TotalTokens { get; set; }

        /// <summary>
        /// Number of tokens in the input prompt.
        /// Null if not provided by server.
        /// </summary>
        public int? PromptTokens { get; set; }

        /// <summary>
        /// Number of tokens generated in completion.
        /// Null if not provided by server.
        /// </summary>
        public int? CompletionTokens { get; set; }

        /// <summary>
        /// Number of tokens spent on reasoning (for reasoning-capable models).
        /// Null if model doesn't support reasoning or none was used.
        /// </summary>
        public int? ReasoningTokens { get; set; }
    }
}


