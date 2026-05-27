namespace LMLocal.Core.Models
{
    internal class ChatMessage
    {
        /// <summary>
        /// Role of the message sender. Common values: "user", "assistant", "system" (or developer?), "tool".
        /// </summary>
        public string Role { get; }

        /// <summary>
        /// Content of the message. Can be a string for simple text messages, or null for assistant messages with only tool calls.
        /// </summary>
        public object Content { get; }

        /// <summary>
        /// Optional ID for tool call being responded to (required when role is "tool").
        /// </summary>
        public string ToolCallId { get; set; }

        /// <summary>
        /// Tool calls initiated by assistant (only for role="assistant" when tool_calls are present).
        /// When present, this should be a collection of ToolCall objects.
        /// According to OpenAI spec: One assistant message can contain multiple tool_calls in the tool_calls array.
        /// </summary>
        public object ToolCalls { get; set; }

        public ChatMessage(string role, object content, string toolCallId = null)
        {
            Role = role;
            Content = content;
            ToolCallId = toolCallId;
        }
    }
}
