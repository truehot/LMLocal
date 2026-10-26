using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.SubAgents;
using LMLocal.Core.Models;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.SubAgents
{
    /// <summary>
    /// Debug-only sink for SubAgent failures.
    /// </summary>
    internal interface ISubAgentFailureLogger
    {
        /// <summary>
        /// Appends a pre-built record as one JSON line.
        /// </summary>
        Task LogAsync(SubAgentFailureRecord record, CancellationToken cancellationToken = default);

        /// <summary>
        /// Appends a failure for a run request (prompt + all run parameters). Secrets are never copied.
        /// </summary>
        Task LogFromRequestAsync(
            SubAgentRunRequest request,
            string stage,
            string error,
            string runId = null,
            int? round = null,
            Guid? chatSessionId = null,
            Exception exception = null,
            IReadOnlyList<SubAgentRecentCall> recentCalls = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Appends a failure captured where only the raw tool call is available; the prompt is read from the call arguments (the "task" parameter).
        /// </summary>
        Task LogFromToolCallAsync(
            ToolCallRecord call,
            string stage,
            string error,
            Exception exception = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One SubAgent failure. Field names serialize to camelCase so the line can be deserialized straight back into <see cref="SubAgentRunRequest"/> for a replay.
    /// </summary>
    internal sealed class SubAgentFailureRecord
    {
        public DateTime TimestampUtc { get; set; }
        public string Stage { get; set; }
        public string RunId { get; set; }
        public string CallId { get; set; }
        public string AgentName { get; set; }
        public string Tool { get; set; }
        public int? Round { get; set; }
        public Guid? ChatSessionId { get; set; }
        public string Model { get; set; }
        public string ProviderType { get; set; }
        public string BaseUrl { get; set; }
        public double? Temperature { get; set; }
        public string ReasoningEffort { get; set; }
        public int? MaxTokens { get; set; }
        public int? MaxRounds { get; set; }
        public int? TimeoutSeconds { get; set; }
        public IReadOnlyList<string> AllowedTools { get; set; }
        public IReadOnlyCollection<string> ExcludedAgentNames { get; set; }
        public string Error { get; set; }
        public string Prompt { get; set; }
        public string System { get; set; }
        public string RawArgumentsJson { get; set; }
        public int PromptChars { get; set; }
        public bool PromptTruncated { get; set; }

        /// <summary>
        /// Last tool-call attempts before the failure (name, arguments, truncated result).
        /// </summary>
        public IReadOnlyList<SubAgentRecentCall> RecentCalls { get; set; }

        /// <summary>
        /// Present only when the failure came from an exception; never populated in Release.
        /// </summary>
        [JsonProperty("exception")]
        public SubAgentFailureExceptionInfo ExceptionInfo { get; set; }
    }

    /// <summary>
    /// Minimal exception snapshot (type + full <c>ToString()</c> including inner exceptions and stack).
    /// </summary>
    internal sealed class SubAgentFailureExceptionInfo
    {
        public string Type { get; set; }
        public string StackTrace { get; set; }

        internal static SubAgentFailureExceptionInfo From(Exception exception)
        {
            if (exception == null)
                return null;

            return new SubAgentFailureExceptionInfo
            {
                Type = exception.GetType().FullName,
                StackTrace = exception.ToString()
            };
        }
    }

    /// <summary>
    /// One tool-call attempt made by a SubAgent before it failed (name, arguments, truncated result).
    /// Only the last few attempts are kept: the line answers "what was the agent doing right before it died".
    /// </summary>
    internal sealed class SubAgentRecentCall
    {
        public string ToolName { get; set; }
        public string CallId { get; set; }
        public int? Round { get; set; }
        public int? Step { get; set; }
        public string ArgumentsJson { get; set; }
        public int? ArgumentsChars { get; set; }
        public bool? ArgumentsTruncated { get; set; }
        public string ResultText { get; set; }
        public int? ResultChars { get; set; }
        public bool? ResultTruncated { get; set; }
        public string Error { get; set; }
    }
}
