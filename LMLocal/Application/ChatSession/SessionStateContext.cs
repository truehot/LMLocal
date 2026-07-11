using System;
using System.Collections.Generic;
using System.Threading;
using LMLocal.Core.Models;

namespace LMLocal.Application.ChatSession
{
    /// <summary>
    /// Mutable context for current chat session state.
    /// Updated by state handlers during execution.
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
        /// Used to prevent infinite tool loops within a single request.
        /// </summary>
        public int ConsecutiveToolIterationCount { get; set; }

        /// <summary>
        /// Number of consecutive rounds where the model called the same tools
        /// with identical arguments. Reset when tool set changes or session ends.
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
        /// Cancellation token for the session. Used to propagate cancellation to service methods.
        /// </summary>
        public CancellationToken SessionCancellationToken { get; set; }
    }
}
