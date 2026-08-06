namespace LMLocal.Application.Chat
{
    /// <summary>
    /// Lightweight summary of a persisted chat session — used to populate the chat history dialog.
    /// </summary>
    public class ChatSessionSummary
    {
        /// <summary>Session identifier (GUID).</summary>
        public string SessionId { get; set; }

        /// <summary>First user message, truncated for display.</summary>
        public string Prompt { get; set; }

        /// <summary>ISO 8601 timestamp of the most recent message in this session.</summary>
        public string Timestamp { get; set; }

        /// <summary>Total number of messages in the session.</summary>
        public int MessageCount { get; set; }
    }
}
