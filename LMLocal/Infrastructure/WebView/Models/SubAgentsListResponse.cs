using System.Collections.Generic;
using LMLocal.Core.Models;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.WebView.Models
{
    /// <summary>
    /// SubAgent summary exposed to the UI.
    /// </summary>
    public class SubAgentResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("providerType")]
        public string ProviderType { get; set; }

        [JsonProperty("customBaseUrl")]
        public string CustomBaseUrl { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("temperature")]
        public double? Temperature { get; set; }

        [JsonProperty("timeoutSeconds")]
        public int? TimeoutSeconds { get; set; }

        [JsonProperty("maxRounds")]
        public int? MaxRounds { get; set; }

        [JsonProperty("maxTokens")]
        public int? MaxTokens { get; set; }

        [JsonProperty("reasoningEffort")]
        public string ReasoningEffort { get; set; }

        [JsonProperty("allowedTools")]
        public List<string> AllowedTools { get; set; } = new List<string>();

        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        /// <summary>
        /// False when the agent is reachable only through a pipeline: the main model does not see it as a tool.
        /// </summary>
        [JsonProperty("exposed")]
        public bool Exposed { get; set; } = true;
    }

    /// <summary>
    /// Pipeline summary exposed to the UI: its steps are shown as a chain and are not editable here.
    /// </summary>
    public class SubAgentPipelineResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        /// <summary>Step labels in run order, e.g. <c>explorer → analyzer → analyzer</c>.</summary>
        [JsonProperty("steps")]
        public List<string> Steps { get; set; } = new List<string>();

        /// <summary>Why the pipeline is not offered to the main model right now; null when it is available.</summary>
        [JsonProperty("unavailableReason")]
        public string UnavailableReason { get; set; }
    }

    /// <summary>
    /// SubAgents list response wrapper.
    /// </summary>
    public class SubAgentsListResponse
    {
        [JsonProperty("agents")]
        public List<SubAgentResponse> Agents { get; set; } = new List<SubAgentResponse>();

        [JsonProperty("pipelines")]
        public List<SubAgentPipelineResponse> Pipelines { get; set; } = new List<SubAgentPipelineResponse>();

        [JsonProperty("success")]
        public bool Success { get; set; } = true;

        [JsonProperty("error")]
        public SubAgentsErrorResponse Error { get; set; }
    }

    public class SubAgentsErrorResponse
    {
        [JsonProperty("message")]
        public string Message { get; set; }
    }

    /// <summary>
    /// Update payload accepted by UpdateSubAgentsAsync.
    /// </summary>
    public class SubAgentsUpdateRequest
    {
        [JsonProperty("providerType")]
        public string ProviderType { get; set; }

        [JsonProperty("customBaseUrl")]
        public string CustomBaseUrl { get; set; }

        [JsonProperty("customApiKey")]
        public string CustomApiKey { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("agents")]
        public List<SubAgentEnabledFlag> Agents { get; set; }

        /// <summary>Enabled flags of the pipelines; null when the payload carries none.</summary>
        [JsonProperty("pipelines")]
        public List<SubAgentEnabledFlag> Pipelines { get; set; }
    }

    /// <summary>
    /// Result returned by UpdateSubAgentsAsync.
    /// </summary>
    public class SubAgentsUpdateResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; } = true;

        [JsonProperty("error")]
        public SubAgentsErrorResponse Error { get; set; }
    }
}
