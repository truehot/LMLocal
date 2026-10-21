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
        /// </summary>
        public int? TotalTokens { get; set; }

        /// <summary>
        /// Number of tokens in the input prompt.
        /// </summary>
        public int? PromptTokens { get; set; }

        /// <summary>
        /// Number of tokens generated in completion.
        /// </summary>
        public int? CompletionTokens { get; set; }

        /// <summary>
        /// Sum of prompt tokens across ALL rounds of this request.
        /// </summary>
        public int? RequestPromptTokens { get; set; }

        /// <summary>
        /// Sum of completion tokens across ALL rounds of this request.
        /// </summary>
        public int? RequestCompletionTokens { get; set; }

        /// <summary>
        /// Sum of cached prompt tokens across ALL rounds of this request.
        /// </summary>
        public int? RequestCachedTokens { get; set; }

        /// <summary>
        /// Number of tokens spent on reasoning (for reasoning-capable models).
        /// </summary>
        public int? ReasoningTokens { get; set; }

        /// <summary>
        /// Number of prompt tokens served from the provider's cache (cache hit).
        /// </summary>
        public int? CachedTokens { get; set; }

        /// <summary>
        /// Average tokens per second during the generation session.
        /// </summary>
        public double TokensPerSecond { get; set; }
    }
}


