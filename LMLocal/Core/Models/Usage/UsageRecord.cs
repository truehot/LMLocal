using System;

namespace LMLocal.Core.Models.Usage
{
    /// <summary>
    /// One line of the usage log: the tokens spent by a single request (not by a single round).
    /// </summary>
    public class UsageRecord
    {
        /// <summary>
        /// UTC time the request STARTED, although the record is written at the end, so a request that crosses
        /// midnight or the end of a month stays in the day and in the file of its start.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>Session of the MAIN chat. For a subagent this is the parent session, not its own log session.</summary>
        public string SessionId { get; set; }

        /// <summary>Id that ties the main request, its subagents and its compactions together.</summary>
        public string RequestId { get; set; }

        /// <summary>Who spent the tokens.</summary>
        public UsageSource Source { get; set; }

        /// <summary>One of <see cref="UsageOrigins"/>.</summary>
        public string Origin { get; set; }

        /// <summary>First ~80 characters of the first user message of the session (main records only).</summary>
        public string Title { get; set; }

        /// <summary>Subagent name (subagent records only).</summary>
        public string Agent { get; set; }

        /// <summary>Log session of the subagent itself, so its log can be found (subagent records only).</summary>
        public string RunSessionId { get; set; }

        /// <summary>Provider id, e.g. "lmstudio".</summary>
        public string Provider { get; set; }

        /// <summary>Provider address, so two LM Studio instances on different addresses stay apart.</summary>
        public string BaseUrl { get; set; }

        /// <summary>Model id the request was sent to.</summary>
        public string Model { get; set; }

        /// <summary>Extension version, filled by the store.</summary>
        public string AppVersion { get; set; }

        /// <summary>
        /// Folder of the solution that was open when the record was written, without a trailing separator; null when no
        /// solution was open.
        /// </summary>
        public string SolutionDir { get; set; }

        /// <summary>Input tokens over all rounds; null when the provider reported none.</summary>
        public int? Prompt { get; set; }

        /// <summary>Output tokens over all rounds; null when the provider reported none.</summary>
        public int? Completion { get; set; }

        /// <summary>Cached prompt tokens over all rounds.</summary>
        public int? Cached { get; set; }

        /// <summary>Reasoning tokens over all rounds.</summary>
        public int? Reasoning { get; set; }

        /// <summary>True when the numbers of a successful round were estimated instead of reported.</summary>
        public bool Estimated { get; set; }

        /// <summary>Number of generation rounds of the request, including rounds that reported no usage.</summary>
        public int Rounds { get; set; }

        /// <summary>Number of tool calls requested by the model during the request.</summary>
        public int ToolCalls { get; set; }

        /// <summary>Output tokens of the rounds that also have a measured generation speed.</summary>
        public long GenCompletion { get; set; }

        /// <summary>Generation time in milliseconds of the same rounds as <see cref="GenCompletion"/>.</summary>
        public long GenMs { get; set; }

        /// <summary>Wall-clock time of the whole request including tool execution.</summary>
        public long DurationMs { get; set; }

        /// <summary>Context window of the model when known.</summary>
        public int? ContextMax { get; set; }

        /// <summary>Largest prompt among the rounds.</summary>
        public int? ContextPeak { get; set; }

        /// <summary>How the request ended.</summary>
        public UsageOutcome Outcome { get; set; }

        /// <summary>First line of the error for outcomes other than ok/cancelled. Analysis only, never shown.</summary>
        public string Error { get; set; }

        /// <summary>
        /// Speed of the request, or 0 when nothing was measured. Speed of a group is summed, never averaged.
        /// </summary>
        public double TokensPerSecond
        {
            get { return GenMs > 0 ? GenCompletion * 1000d / GenMs : 0d; }
        }
    }
}
