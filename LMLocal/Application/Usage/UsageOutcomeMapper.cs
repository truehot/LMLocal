using System;
using System.Collections.Generic;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Core.Models.Usage;

namespace LMLocal.Application.Usage
{
    /// <summary>
    /// Translates the outcomes the chat knows into <see cref="UsageOutcome"/>, plus the first line of the error text.
    /// </summary>
    internal static class UsageOutcomeMapper
    {
        /// <summary>Longest error text kept in a record; the rest is analysis detail nobody reads.</summary>
        private const int MaxErrorLength = 300;

        /// <summary>
        /// Maps the terminal state of the main chat.
        /// </summary>
        internal static void MapSessionTerminal(
            ChatSessionState state,
            IReadOnlyList<StreamCompletionResult> results,
            Exception lastException,
            string lastResultError,
            out UsageOutcome outcome,
            out string error)
        {
            error = null;

            switch (state)
            {
                case ChatSessionState.Cancelled:
                    outcome = UsageOutcome.Cancelled;
                    return;

                case ChatSessionState.Error:
                    var last = results != null && results.Count > 0 ? results[results.Count - 1] : null;
                    string finishReason = last != null ? last.FinishReason : null;

                    if (IsIncompleteFinish(finishReason))
                    {
                        outcome = UsageOutcome.Incomplete;
                        error = FirstLine(string.IsNullOrEmpty(lastResultError) && last != null
                            ? last.ErrorMessage
                            : lastResultError);
                        return;
                    }

                    outcome = UsageOutcome.Error;
                    error = FirstLine(lastException != null ? ExceptionFormatter.Format(lastException) : lastResultError);
                    return;

                default:
                    outcome = UsageOutcome.Ok;
                    return;
            }
        }

        /// <summary>
        /// Maps the outcome of one subagent round. A cancellation nobody asked for, and any error, count as failures;
        /// a timeout is decided by the caller, which knows why the token fired.
        /// </summary>
        internal static UsageOutcome MapStreamRound(StreamRoundOutcome outcome)
        {
            switch (outcome)
            {
                case StreamRoundOutcome.Completed:
                    return UsageOutcome.Ok;

                case StreamRoundOutcome.Incomplete:
                    return UsageOutcome.Incomplete;

                default:
                    // Error and an unexpected cancellation of the agent are failures of the run.
                    return UsageOutcome.Error;
            }
        }

        /// <summary>
        /// Maps the outcome of a compaction call: only an exception is an error, a user stop is a cancellation and
        /// everything else (including "nothing to compact") ends without a failure.
        /// </summary>
        internal static UsageOutcome MapCompaction(CompactionOutcome outcome)
        {
            return outcome == CompactionOutcome.Cancelled ? UsageOutcome.Cancelled : UsageOutcome.Ok;
        }

        /// <summary>True when the model stopped because it ran out of output budget or hit a safety filter.</summary>
        internal static bool IsIncompleteFinish(string finishReason)
        {
            if (string.IsNullOrWhiteSpace(finishReason))
                return false;

            string value = finishReason.Trim();

            return string.Equals(value, "length", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "content_filter", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// First non-empty line of an error text, trimmed and capped, or null when there is nothing to keep.
        /// </summary>
        internal static string FirstLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            int index = text.IndexOfAny(new[] { '\r', '\n' });
            string line = (index >= 0 ? text.Substring(0, index) : text).Trim();

            if (line.Length == 0)
                return null;

            return line.Length <= MaxErrorLength ? line : line.Substring(0, MaxErrorLength);
        }
    }
}
