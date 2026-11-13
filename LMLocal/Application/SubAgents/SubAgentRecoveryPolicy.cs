using System;
using System.Collections.Generic;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;

namespace LMLocal.Application.SubAgents
{
    internal enum RecoveryAction
    {
        /// <summary>Execute the round's tool calls as usual.</summary>
        Proceed,

        /// <summary>
        /// Continue the run with <see cref="RecoveryDecision.Nudge"/> as the next user turn; after a tool round the run
        /// closes the turn first, a final answer is already a closed turn.
        /// </summary>
        Recover,

        /// <summary>Stop the run with <see cref="RecoveryDecision.Error"/>.</summary>
        Fail
    }

    /// <summary>
    /// What a SubAgent run does with a round: one that ended in tool calls, or a final answer.
    /// </summary>
    internal sealed class RecoveryDecision
    {
        public static readonly RecoveryDecision Proceed = new RecoveryDecision(RecoveryAction.Proceed, null, null, null);

        private RecoveryDecision(RecoveryAction action, string nudge, string note, string error)
        {
            Action = action;
            Nudge = nudge;
            Note = note;
            Error = error;
        }

        public RecoveryAction Action { get; }

        /// <summary>Recover: the prompt of the next user turn.</summary>
        public string Nudge { get; }

        /// <summary>Recover: the note added to the run's recoveries ("SubAgent recovered from … (attempt N of M).").</summary>
        public string Note { get; }

        /// <summary>Fail: the error of the run.</summary>
        public string Error { get; }

        public static RecoveryDecision Recover(string nudge, string note) => new RecoveryDecision(RecoveryAction.Recover, nudge, note, null);

        public static RecoveryDecision Fail(string error) => new RecoveryDecision(RecoveryAction.Fail, null, null, error);
    }

    /// <summary>
    /// Decides whether a SubAgent run recovers from a malformed (aborted) tool call, a repeated tool call loop, or a
    /// final answer with ref markers the runner could not expand.
    /// </summary>
    internal sealed class SubAgentRecoveryPolicy
    {
        /// <summary>
        /// User-turn instruction injected after a repeated tool call.
        /// </summary>
        internal const string LoopRecoveryNudge = @"[AUTOMATED GUARD: DUPLICATE TOOL CALL BLOCKED]
Your last call repeated an earlier one; its result is above.
Answer the task now, or make one call with different arguments.
Answer directly.";

        /// <summary>
        /// User-turn instruction injected after a tool call was aborted while streaming because its arguments were malformed.
        /// </summary>
        internal const string MalformedToolCallNudge = @"[AUTOMATED GUARD: MALFORMED TOOL CALL ABORTED]
The previous tool call was malformed and aborted.

Retry it once using the correct tool name and argument format. Keep arguments simple; avoid unnecessary quotes, backticks, and escaping.

Output only the corrected tool call. If unable, stop.";

        /// <summary>
        /// User-turn instruction injected after a final answer with ref markers the runner could not expand.
        /// </summary>
        internal const string UnresolvedRefsNudge = @"[AUTOMATED GUARD: UNRESOLVED REF MARKERS]
These markers in your answer point to lines that no tool returned in this run, so they cannot be expanded:
{0}
For each marker, call a tool that returns those lines (read_file_lines for a source range), or remove the @ and write the location as plain text.
Then give the complete final answer again.";

        /// <summary>How many unresolved markers the nudge lists; the rest are only counted.</summary>
        internal const int UnresolvedRefsListLimit = 10;

        /// <summary>How many times a single run may send its final answer back because of unresolved ref markers.</summary>
        private const int UnresolvedRefsMaxAttempts = 1;

        /// <summary>Consecutive identical rounds that count as a loop.</summary>
        private const int MaxDuplicateToolRounds = 2;

        /// <summary>How many times a single run may recover from a repeated tool call before it fails.</summary>
        private const int LoopRecoveryMaxAttempts = 2;

        /// <summary>How many times a single run may recover from a tool call aborted while streaming before it fails.</summary>
        private const int MalformedRecoveryMaxAttempts = 2;

        private readonly IToolCallLoopDetector _loopDetector;
        private readonly bool _loopRecoveryEnabled;
        private readonly int _maxRounds;

        private IReadOnlyList<ToolCallRecord> _previousToolCalls;
        private int _consecutiveDuplicateRounds;
        private int _loopRecoveryAttempts;
        private int _malformedRecoveryAttempts;
        private int _unresolvedRefsAttempts;

        public SubAgentRecoveryPolicy(IToolCallLoopDetector loopDetector, bool loopRecoveryEnabled, int maxRounds)
        {
            _loopDetector = loopDetector ?? throw new ArgumentNullException(nameof(loopDetector));
            _loopRecoveryEnabled = loopRecoveryEnabled;
            _maxRounds = maxRounds;
        }

        /// <summary>
        /// Decides what to do with a round that ended in tool calls.
        /// </summary>
        public RecoveryDecision Evaluate(StreamCompletionResult round, int roundIndex)
        {
            if (round == null)
                throw new ArgumentNullException(nameof(round));

            if (round.StopReason == StreamStopReason.ToolCallAborted)
            {
                if (!CanRecover(_malformedRecoveryAttempts, MalformedRecoveryMaxAttempts, roundIndex))
                    return RecoveryDecision.Fail($"SubAgent stopped: the model produced malformed tool calls {_malformedRecoveryAttempts + 1} times.");

                _malformedRecoveryAttempts++;
                return StartRecovery(
                    MalformedToolCallNudge,
                    $"SubAgent recovered from a malformed tool call (attempt {_malformedRecoveryAttempts} of {MalformedRecoveryMaxAttempts}).");
            }

            if (_previousToolCalls != null && _loopDetector.AreSameToolCalls(round.ToolCalls, _previousToolCalls))
            {
                _consecutiveDuplicateRounds++;
                if (_consecutiveDuplicateRounds >= MaxDuplicateToolRounds)
                {
                    if (!_loopRecoveryEnabled || !CanRecover(_loopRecoveryAttempts, LoopRecoveryMaxAttempts, roundIndex))
                        return RecoveryDecision.Fail("SubAgent detected a repeated tool call loop and stopped.");

                    _loopRecoveryAttempts++;
                    return StartRecovery(
                        LoopRecoveryNudge,
                        $"SubAgent recovered from a repeated tool call loop (attempt {_loopRecoveryAttempts} of {LoopRecoveryMaxAttempts}).");
                }
            }
            else
            {
                _consecutiveDuplicateRounds = 0;
            }

            _previousToolCalls = round.ToolCalls;
            return RecoveryDecision.Proceed;
        }

        /// <summary>
        /// Decides what to do with a final answer: Recover when it has ref markers the runner could not expand and the
        /// attempt is not used yet; otherwise Proceed, and the answer is returned as it is.
        /// </summary>
        public RecoveryDecision EvaluateAnswer(IReadOnlyList<string> unresolvedRefs, int roundIndex)
        {
            if (unresolvedRefs == null || unresolvedRefs.Count == 0)
                return RecoveryDecision.Proceed;

            if (!CanRecover(_unresolvedRefsAttempts, UnresolvedRefsMaxAttempts, roundIndex))
                return RecoveryDecision.Proceed;

            _unresolvedRefsAttempts++;
            return RecoveryDecision.Recover(
                BuildUnresolvedRefsNudge(unresolvedRefs),
                $"SubAgent recovered from unresolved ref markers (attempt {_unresolvedRefsAttempts} of {UnresolvedRefsMaxAttempts}).");
        }

        /// <summary>
        /// Fills <see cref="UnresolvedRefsNudge"/> with the markers: at most <see cref="UnresolvedRefsListLimit"/>, one per line,
        /// then "(and N more)" when there are more.
        /// </summary>
        internal static string BuildUnresolvedRefsNudge(IReadOnlyList<string> unresolvedRefs)
        {
            int listed = Math.Min(unresolvedRefs.Count, UnresolvedRefsListLimit);
            var lines = new List<string>(listed + 1);
            for (int i = 0; i < listed; i++)
                lines.Add(unresolvedRefs[i]);
            if (unresolvedRefs.Count > listed)
                lines.Add($"(and {unresolvedRefs.Count - listed} more)");

            return UnresolvedRefsNudge.Replace("\r\n", "\n").Replace("{0}", string.Join("\n", lines));
        }

        private RecoveryDecision StartRecovery(string nudge, string note)
        {
            _previousToolCalls = null;
            _consecutiveDuplicateRounds = 0;
            return RecoveryDecision.Recover(nudge, note);
        }

        /// <summary>
        /// The run may recover (one more user turn) instead of failing: attempts are left and a round is left for the nudge.
        /// </summary>
        private bool CanRecover(int attempts, int maxAttempts, int roundIndex)
            => attempts < maxAttempts
            && roundIndex + 1 < _maxRounds;
    }
}
