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
    }
}
