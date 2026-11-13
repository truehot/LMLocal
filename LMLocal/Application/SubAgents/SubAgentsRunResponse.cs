using System.Collections.Generic;
using LMLocal.Application.Tool;

namespace LMLocal.Application.SubAgents
{
    /// <summary>
    /// Result of a SubAgent run. At minimum exposes success/content/error.
    /// </summary>
    public class SubAgentsRunResponse : IToolExecutionOutcome
    {
        public bool Success { get; set; }
        public string Content { get; set; }
        public string Error { get; set; }
        public string Model { get; set; }
        public string RunId { get; set; }
        public long DurationMs { get; set; }
        public int? PromptTokens { get; set; }
        public int? CompletionTokens { get; set; }
        public int? TotalTokens { get; set; }
        public double TokensPerSecond { get; set; }
        public int Rounds { get; set; }
        public List<string> ToolsUsed { get; set; }

        /// <summary>
        /// Number of ref markers successfully expanded back into real file content: the ref mechanism worked.
        /// </summary>
        public int ExpandedRefCount { get; set; }

        /// <summary>
        /// Number of ref markers left as-is because their (path, range) was not in the run's read ledger (anti-hallucination: a range that was never read is never expanded).
        /// </summary>
        public int UnresolvedRefCount { get; set; }

        /// <summary>
        /// One note per automated recovery during the run (a repeated tool call, a malformed tool call aborted while streaming);
        /// null when the run never recovered.
        /// </summary>
        public List<string> Recoveries { get; set; }

        object IToolExecutionOutcome.Result => Content;
    }
}
