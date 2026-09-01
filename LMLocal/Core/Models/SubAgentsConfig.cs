using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Runtime configuration for all SubAgents, stored in json.
    /// </summary>
    public class SubAgentsConfig
    {
        /// <summary>
        /// All defined SubAgents in file order.
        /// </summary>
        [JsonProperty("agents")]
        public List<SubAgentDefinition> Agents { get; set; } = new List<SubAgentDefinition>();

        /// <summary>
        /// Default provider type applied to agents that don't specify their own. Optional.
        /// </summary>
        [JsonProperty("providerType")]
        public string ProviderType { get; set; }

        /// <summary>
        /// Default base URL applied to agents that don't specify their own. Optional.
        /// </summary>
        [JsonProperty("customBaseUrl")]
        public string CustomBaseUrl { get; set; }

        /// <summary>
        /// Default API key applied to agents that don't specify their own. Optional.
        /// </summary>
        [JsonProperty("customApiKey")]
        public string CustomApiKey { get; set; }

        /// <summary>
        /// Validation errors of the most recent parse. 
        /// </summary>
        [JsonIgnore]
        public IReadOnlyList<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// Validates all agents and returns a list of human-readable errors.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < Agents.Count; i++)
            {
                var agent = Agents[i];
                foreach (var e in agent.Validate())
                {
                    errors.Add($"agent[{i}] {e}");
                }

                if (!string.IsNullOrWhiteSpace(agent.Id))
                {
                    if (!names.Add(agent.Id.Trim()))
                    {
                        errors.Add($"duplicate agent id '{agent.Id.Trim()}'");
                    }
                }
            }

            return errors;
        }

        /// <summary>
        /// Fills agent-level provider settings from the top-level defaults when an agent has no value of its own.
        /// </summary>
        public void ApplyDefaults()
        {
            if (Agents == null)
                return;

            foreach (var agent in Agents)
            {
                if (agent == null)
                    continue;

                if (string.IsNullOrWhiteSpace(agent.ProviderType))
                    agent.ProviderType = string.IsNullOrWhiteSpace(ProviderType) ? "lmstudio" : ProviderType;

                if (string.IsNullOrWhiteSpace(agent.CustomBaseUrl))
                    agent.CustomBaseUrl = CustomBaseUrl;

                if (string.IsNullOrWhiteSpace(agent.CustomApiKey))
                    agent.CustomApiKey = CustomApiKey;
            }
        }

    }

    /// <summary>
    /// Configuration for a single SubAgent, one item of the subagents array in json .
    /// <summary>
    public class SubAgentEnabledFlag
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("index")]
        public int? Index { get; set; }

        [JsonProperty("enabled")]
        public bool Enabled { get; set; }
    }

    /// </summary>
    public class SubAgentDefinition
    {
        /// <summary>
        /// Agent id. Required (unique, case-insensitive). Used as the tool name.
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// Optional human-readable display name shown in the UI/chat. Falls back to <see cref="Id"/>.
        /// </summary>
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        /// <summary>
        /// Human-readable agent description. Required. 
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>Provider type key: "lmstudio", "ollama", "openai", ... Falls back to "lmstudio" when neither the agent nor the top-level defaults specify one.</summary>
        [JsonProperty("providerType")]
        public string ProviderType { get; set; }

        /// <summary>Base URL for the SubAgent's dedicated provider endpoint. Required.</summary>
        [JsonProperty("customBaseUrl")]
        public string CustomBaseUrl { get; set; }

        /// <summary>API key for the SubAgent's dedicated provider endpoint.</summary>
        [JsonProperty("customApiKey")]
        public string CustomApiKey { get; set; }

        /// <summary>ModelId the SubAgent must use. Required; comes from subagents.json.</summary>
        [JsonProperty("model")]
        public string Model { get; set; }

        /// <summary>System prompt for the SubAgent. Empty => fall back to the main chat system prompt.</summary>
        [JsonProperty("system")]
        public string System { get; set; }

        /// <summary>Sampling temperature (0..2).</summary>
        [JsonProperty("temperature")]
        public double? Temperature { get; set; }

        /// <summary>Overall watchdog timeout in seconds. 0 = disabled.</summary>
        [JsonProperty("timeoutSeconds")]
        public int? TimeoutSeconds { get; set; }

        /// <summary>Max tool-call rounds before the SubAgent stops.</summary>
        [JsonProperty("maxRounds")]
        public int? MaxRounds { get; set; }

        /// <summary>Max output tokens for a SubAgent reply.</summary>
        [JsonProperty("maxTokens")]
        public int? MaxTokens { get; set; }

        /// <summary>
        /// Whether the agent is enabled. Default: true.
        /// </summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Allowed built-in tool names. Empty list => no tools at all.
        /// </summary>
        [JsonProperty("allowedTools")]
        public List<string> AllowedTools { get; set; } = new List<string>();

        /// <summary>
        /// Validates all agents and returns a list of human-readable errors.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();

            if (!string.IsNullOrWhiteSpace(Id) && !IsValidToolIdentifier(Id))
                errors.Add($"'id' '{Id.Trim()}' is not a valid tool identifier (allowed: letters, digits, '_', '-', 1..64 chars)");

            if (string.IsNullOrWhiteSpace(Id))
                errors.Add("'id' is required");

            if (string.IsNullOrWhiteSpace(Description))
                errors.Add("'description' is required");

            if (string.IsNullOrWhiteSpace(Model))
                errors.Add("'model' is required");

            if (string.IsNullOrWhiteSpace(CustomBaseUrl))
                errors.Add("'customBaseUrl' is required");
            else if (!Uri.TryCreate(CustomBaseUrl, UriKind.Absolute, out _))
                errors.Add($"'customBaseUrl' is not a valid URL: '{CustomBaseUrl}'");

            if (Temperature.HasValue && (Temperature.Value < 0 || Temperature.Value > 2))
                errors.Add("'temperature' must be between 0 and 2");

            if (TimeoutSeconds.HasValue && TimeoutSeconds.Value < 0)
                errors.Add("'timeoutSeconds' must be >= 0 (0 disables the timeout)");

            if (MaxRounds.HasValue && MaxRounds.Value < 1)
                errors.Add("'maxRounds' must be >= 1");

            if (MaxTokens.HasValue && MaxTokens.Value < 1)
                errors.Add("'maxTokens' must be >= 1");

            return errors;
        }

        /// <summary>
        /// Validates the agent name as a tool identifier: letters, digits, '_', '-', 1..64 chars.
        /// </summary>
        private static bool IsValidToolIdentifier(string name)
        {
            var value = name.Trim();
            if (string.IsNullOrEmpty(value) || value.Length > 64)
                return false;

            foreach (var c in value)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                    return false;
            }

            return true;
        }
    }
}













