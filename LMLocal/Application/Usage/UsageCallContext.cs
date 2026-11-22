using LMLocal.Core.Models.Usage;

namespace LMLocal.Application.Usage
{
    /// <summary>
    /// Identifies the chat request a piece of work belongs to: the session of the main chat, the id of the request and
    /// how the work was started.
    /// </summary>
    public sealed class UsageCallContext
    {
        /// <summary>Session of the MAIN chat; null for an IPC run, which has no chat session.</summary>
        public string SessionId { get; set; }

        /// <summary>Id of the user request that started the work.</summary>
        public string RequestId { get; set; }

        /// <summary>One of <see cref="UsageOrigins"/>; null means the work was started by a chat request.</summary>
        public string Origin { get; set; }

        /// <summary>Origin to write into a usage record, defaulting to an interactive chat request.</summary>
        public string EffectiveOrigin
        {
            get { return string.IsNullOrWhiteSpace(Origin) ? UsageOrigins.Chat : Origin; }
        }

        /// <summary>Context of the work a chat request does: subagents, compaction.</summary>
        internal static UsageCallContext ForChat(string sessionId, string requestId)
        {
            return new UsageCallContext
            {
                SessionId = sessionId,
                RequestId = requestId,
                Origin = UsageOrigins.Chat
            };
        }

        /// <summary>Context of a run started through IPC (benchmarks), which is written but never shown in the dialog.</summary>
        internal static UsageCallContext ForIpc(string requestId = null)
        {
            return new UsageCallContext
            {
                RequestId = requestId,
                Origin = UsageOrigins.Ipc
            };
        }
    }
}
