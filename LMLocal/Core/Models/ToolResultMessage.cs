namespace LMLocal.Core.Models
{
    /// <summary>
    /// Represents a tool execution result to be sent to the model.
    /// </summary>
    public class ToolResultMessage
    {
        /// <summary>
        /// The unique identifier of the tool call being responded to.
        /// </summary>
        public string ToolCallId { get; set; }

        /// <summary>
        /// The name of the tool that was executed.
        /// </summary>
        public string ToolName { get; set; }

        /// <summary>
        /// The result of the tool execution (success case).
        /// Can be any object - string, JSON, etc.
        /// </summary>
        public object Result { get; set; }

        /// <summary>
        /// Error message if tool execution failed.
        /// Null if execution was successful.
        /// </summary>
        public string Error { get; set; }
    }
}
