using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Core.Models;

namespace LMLocal.Application.ChatSession
{
    /// <summary>
    /// Mutable context for current chat session state.
    /// </summary>
    internal class SessionStateContext
    {
        /// <summary>
        /// Current state in the session lifecycle.
        /// </summary>
        public ChatSessionState CurrentState { get; set; }

        /// <summary>
        /// Current generation round number (0-based).
        /// </summary>
        public int RoundNumber { get; set; }

        /// <summary>
        /// Number of consecutive tool iterations in current cycle (resets after generation completes).
        /// </summary>
        public int ConsecutiveToolIterationCount { get; set; }

        /// <summary>
        /// Number of consecutive rounds where the model called the same tools with identical arguments.
        /// </summary>
        public int ConsecutiveDuplicateToolRounds { get; set; }

        /// <summary>
        /// Last LLM generation result.
        /// </summary>
        public StreamCompletionResult LastResult { get; set; }

        /// <summary>
        /// All generation results from all rounds.
        /// </summary>
        public List<StreamCompletionResult> AllResults { get; set; }

        /// <summary>
        /// Tool execution results to be passed to LLM in next round.
        /// </summary>
        public List<ToolResultMessage> ToolResultsForNextRound { get; set; }

        /// <summary>
        /// Exception that caused error state (if applicable).
        /// </summary>
        public Exception LastException { get; set; }

        /// <summary>
        /// Number of context-overflow recoveries performed in the current session.
        /// </summary>
        public int ContextOverflowRetryCount { get; set; }

        /// <summary>
        /// True once the history has been compacted in this session.
        /// </summary>
        public bool HistoryCompacted { get; set; }

        /// <summary>
        /// Cancellation token for the session. Used to propagate cancellation to service methods.
        /// </summary>
        public CancellationToken SessionCancellationToken { get; set; }

        /// <summary>
        /// Sink for rate-limit wait notifications. Null when the caller passed no message callback, i.e. there is
        /// nothing to show. Application-level delegate so this state never depends on a WebView delivery mechanism.
        /// </summary>
        public Func<RateLimitWaitInfo, Task> RateLimitWaitSink { get; set; }

        /// <summary>
        /// Id of the current user request. Ties the main usage record to the subagents and compactions it started.
        /// </summary>
        public string RequestId { get; set; }

        /// <summary>
        /// Session of the main chat log, read once when the request starts. A compaction inside the request starts a new
        /// log session, so a later read would spread the records of one request over two sessions.
        /// </summary>
        public string SessionId { get; set; }

        /// <summary>UTC time the request started; written into the usage record, so it lands in the day of its start.</summary>
        public DateTime StartedUtc { get; set; }

        /// <summary>
        /// First ~80 characters of the first user message of the session, written into every main record so the stats
        /// dialog never has to read the chat logs.
        /// </summary>
        public string Title { get; set; }
    }
}
