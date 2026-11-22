using System;
using System.Collections.Generic;
using LMLocal.Application.Tool;

namespace LMLocal.Application.SubAgents
{
    /// <summary>
    /// All SubAgent settings for one run, resolved by the caller so the service itself does not re-read json.
    /// </summary>
    public class SubAgentRunRequest
    {
        public string AgentName { get; set; }
        public string Prompt { get; set; }
        public string ProviderType { get; set; }
        public string BaseUrl { get; set; }
        public string ApiKey { get; set; }
        public string Model { get; set; }
        public string System { get; set; }
        public double? Temperature { get; set; }
        public string ReasoningEffort { get; set; }
        public int? MaxTokens { get; set; }
        public int? TimeoutSeconds { get; set; }
        public int? MaxRounds { get; set; }
        public List<string> AllowedTools { get; set; }
        public IReadOnlyCollection<string> ExcludedAgentNames { get; set; }
        public IProgress<ToolActivityEvent> Progress { get; set; }

        /// <summary>
        /// Session of the MAIN chat this run belongs to. The service logs runs to its own session, so the parent id is
        /// passed in; null for a run started outside a chat (IPC).
        /// </summary>
        public string ParentSessionId { get; set; }

        /// <summary>Id of the user request that started the run; ties the run to the main record and its compactions.</summary>
        public string RequestId { get; set; }

        /// <summary>One of <see cref="LMLocal.Core.Models.Usage.UsageOrigins"/>; null means a chat request.</summary>
        public string Origin { get; set; }
    }
}
