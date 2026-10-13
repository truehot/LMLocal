namespace LMLocal.Infrastructure.WebView
{
    /// <summary>
    /// Message containing tool call invocation details.
    /// </summary>
    internal class WebView2ToolCallMessage : WebView2ScriptMessage
    {
        /// <summary>
        /// Function name being invoked (e.g., "search_in_files").
        /// </summary>
        public string FunctionName { get; set; }

        /// <summary>
        /// UI-scoped unique identifier of this tool call invocation, used by the WebView to key the tool-status card across the start/step/end messages.
        /// </summary>
        public string CallId { get; set; }

        /// <summary>
        /// JSON-formatted arguments for the tool call.
        /// </summary>
        public string ArgumentsJson { get; set; }

        /// <summary>
        /// Message describing what the tool is doing (e.g., "Searching for 'X'...").
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Indicates whether tool execution failed.
        /// </summary>
        public bool IsError { get; set; }

        /// <summary>
        /// Optional 1-based step number, present on StreamToolStep messages.
        /// </summary>
        public int? Step { get; set; }
    }
}




