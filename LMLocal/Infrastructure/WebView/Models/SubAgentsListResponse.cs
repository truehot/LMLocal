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

        [JsonProperty("allowedTools")]
        public List<string> AllowedTools { get; set; } = new List<string>();

        [JsonProperty("enabled")]
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// SubAgents list response wrapper.
    /// </summary>
    public class SubAgentsListResponse
    {
        [JsonProperty("agents")]
        public List<SubAgentResponse> Agents { get; set; } = new List<SubAgentResponse>();

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
    /// Update payload accepted by UpdateSubAgentsAsync: only the enabled flags are edited from the dialog; the rest of the config is preserved.
    /// </summary>
    public class SubAgentsUpdateRequest
    {
        [JsonProperty("agents")]
        public List<SubAgentEnabledFlag> Agents { get; set; }
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
