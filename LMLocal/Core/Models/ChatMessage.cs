using System;

namespace LMLocal.Core.Models
{
    public class ChatMessage
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
        /// </summary>
        public object ToolCalls { get; set; }

        /// <summary>
        /// Id of the model that produced this assistant message.
        /// </summary>
        public string ModelId { get; set; }

        /// <summary>
        /// Time the message was created, in UTC. Persisted as created_at so it survives a rewrite of the history;
        /// UI only, it is never sent to the model.
        /// </summary>
        public DateTime? Timestamp { get; set; }

        public ChatMessage(string role, object content, string toolCallId = null)
        {
            Role = role;
            Content = content;
            ToolCallId = toolCallId;
        }
    }
}
