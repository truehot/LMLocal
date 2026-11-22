using System.Collections.Generic;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Lightweight summary of a persisted chat session — used to populate the chat history dialog.
    /// </summary>
    public class ChatSessionSummary
    {
        /// <summary>Session identifier (GUID).</summary>
        public string SessionId { get; set; }

        /// <summary>
        /// Prompt shown on the session card: the last user message of the session, truncated for display.
        /// </summary>
        public string Prompt { get; set; }

        /// <summary>ISO 8601 (round-trip) timestamp of the most recent activity in this session; empty when the log carries no timestamp.</summary>
        public string Timestamp { get; set; }

        /// <summary>Total number of logged lines in the session (user, assistant and tool messages).</summary>
        public int MessageCount { get; set; }

        /// <summary>
        /// Ids of the models that produced the assistant messages of this session, in order of first use.
        /// </summary>
        public List<string> Models { get; set; } = new List<string>();
    }
}
