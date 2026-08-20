namespace LMLocal.Services.Tool
{
    /// <summary>
    /// Result of tool execution.
    /// </summary>
    internal class ToolExecutionResult
    {
        /// <summary>
        /// Unique tool call ID (correlates with tool invocation).
        /// </summary>
        public string ToolId { get; set; }

        /// <summary>
        /// Tool function name.
        /// </summary>
        public string ToolName { get; set; }

        /// <summary>
        /// Execution result (can be any object - string, JSON, etc).
        /// Null if execution failed.
        /// </summary>
        public object Result { get; set; }

        /// <summary>
        /// Error message if execution failed (full details sent to the model and persisted).
        /// Null if successful.
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Short, user-facing text shown in the UI on success.
        /// Not sent to the model and not persisted.
        /// </summary>
        public string CompletionMessage { get; set; }

        /// <summary>
        /// Short, user-facing text shown in the UI on error.
        /// Not sent to the model and not persisted.
        /// </summary>
        public string UserMessage { get; set; }

        /// <summary>
        /// True if tool executed successfully.
        /// Computed property based on Error and Result.
        /// </summary>
        public bool IsSuccess => string.IsNullOrEmpty(Error) && Result != null;
    }
}
