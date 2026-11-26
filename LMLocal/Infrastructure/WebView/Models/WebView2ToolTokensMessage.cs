namespace LMLocal.Infrastructure.WebView
{
    /// <summary>Live token usage of a running SubAgent; never a step of the tool card.</summary>
    internal class WebView2ToolTokensMessage : WebView2ScriptMessage
    {
        /// <summary>UI call id of the tool call the SubAgent runs under.</summary>
        public string CallId { get; set; }

        /// <summary>Agent of the current pipeline step; empty for a single SubAgent.</summary>
        public string AgentName { get; set; }

        /// <summary>Tokens produced by the call so far, over all its rounds.</summary>
        public int Tokens { get; set; }

        /// <summary>Live generation speed in tokens per second; 0 when not known.</summary>
        public double TokensPerSecond { get; set; }
    }
}
