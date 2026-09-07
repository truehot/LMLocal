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
        object IToolExecutionOutcome.Result => Content;
    }
}
