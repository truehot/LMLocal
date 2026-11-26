using LMLocal.Core.Models;

namespace LMLocal.Application.Tool
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
        /// Data for the WebView only, e.g. the task list of todo_write.
        /// Not sent to the model and not persisted.
        /// </summary>
        public object UiData { get; set; }

        /// <summary>
        /// True if tool executed successfully.
        /// Computed property based on Error and Result.
        /// </summary>
        public bool IsSuccess => string.IsNullOrEmpty(Error) && Result != null;

        /// <summary>
        /// Result to send to the model for <paramref name="call"/>: the error text takes the place of the result of a
        /// failed call, so the model reads what went wrong instead of an empty answer.
        /// </summary>
        public ToolResultMessage ToMessage(ToolCallRecord call)
        {
            return new ToolResultMessage
            {
                ToolCallId = call.CallId,
                ToolName = call.FunctionName,
                Result = string.IsNullOrEmpty(Error) ? Result : Error,
                Error = Error
            };
        }
    }
}
