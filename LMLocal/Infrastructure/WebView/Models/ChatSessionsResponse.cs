using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.WebView.Models
{
    /// <summary>
    /// Response for GetChatSessionsAsync — returns a list of chat session summaries.
    /// </summary>
    public class ChatSessionsResponse
    {
        [JsonProperty("sessions")]
        public List<ChatSessionSummaryResponse> Sessions { get; set; } = new List<ChatSessionSummaryResponse>();
    }

    /// <summary>
    /// A single session summary formatted for the frontend with camelCase property names.
    /// </summary>
    public class ChatSessionSummaryResponse
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; }

        [JsonProperty("prompt")]
        public string Prompt { get; set; }

        [JsonProperty("timestamp")]
        public string Timestamp { get; set; }

        [JsonProperty("messageCount")]
        public int MessageCount { get; set; }
    }
}
