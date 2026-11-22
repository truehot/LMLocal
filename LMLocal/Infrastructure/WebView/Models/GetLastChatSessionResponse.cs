using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.WebView.Models
{
    /// <summary>
    /// Response for GetLastChatSessionAsync — returns the last persisted chat session.
    /// </summary>
    public class GetLastChatSessionResponse
    {
        [JsonProperty("hasSession")]
        public bool HasSession { get; set; }

        [JsonProperty("messages")]
        public List<ChatMessageResponse> Messages { get; set; } = new List<ChatMessageResponse>();
    }

    /// <summary>
    /// A chat message formatted for the frontend with camelCase property names.
    /// </summary>
    public class ChatMessageResponse
    {
        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("content")]
        public object Content { get; set; }

        [JsonProperty("toolCallId")]
        public string ToolCallId { get; set; }

        [JsonProperty("toolCalls")]
        public object ToolCalls { get; set; }

        /// <summary>
        /// Id of the model that produced this message; omitted for roles that carry no model (user, tool)
        /// and for messages restored from logs written before the model id was persisted.
        /// </summary>
        [JsonProperty("modelId", NullValueHandling = NullValueHandling.Ignore)]
        public string ModelId { get; set; }

        /// <summary>
        /// Time the message was created (UI only); omitted for messages restored from logs written before the time was
        /// persisted.
        /// </summary>
        [JsonProperty("timestamp", NullValueHandling = NullValueHandling.Ignore)]
        public DateTime? Timestamp { get; set; }
    }
}
