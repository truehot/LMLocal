namespace LMLocal.Infrastructure.WebView
{
    /// <summary>
    /// Message containing tool call invocation details.
    /// Type = StreamToolCall or StreamToolEnd
    /// For StreamToolCall: Tool is detected during generation, will be executed by backend.
    /// For StreamToolEnd: Tool execution has completed with result information.
    /// </summary>
    internal class WebView2ToolCallMessage : WebView2ScriptMessage
    {
        /// <summary>
        /// Function name being invoked (e.g., "search_in_files").
        /// </summary>
        public string FunctionName { get; set; }

        /// <summary>
        /// Unique identifier for this tool call invocation (e.g., "call_abc123").
        /// Used to correlate results back to this specific call.
        /// </summary>
        public string CallId { get; set; }

        /// <summary>
        /// JSON-formatted arguments for the tool call.
        /// Null for StreamToolEnd messages.
        /// </summary>
        public string ArgumentsJson { get; set; }

        /// <summary>
        /// For StreamToolCall: Processing message describing what the tool is doing (e.g., "Searching for 'X'...").
        /// For StreamToolEnd: single user-facing text — result summary on success (e.g., "Found 5 matches"),
        /// or a short error message when IsError is true. The full details go to the model, not here.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Indicates whether tool execution failed.
        /// For StreamToolEnd messages: true if an error occurred, false if successful.
        /// </summary>
        public bool IsError { get; set; }
    }
}




