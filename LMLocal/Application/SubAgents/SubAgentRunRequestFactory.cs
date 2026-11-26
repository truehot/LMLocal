using System;
using System.Collections.Generic;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;

namespace LMLocal.Application.SubAgents
{
    /// <summary>
    /// Everything a caller may override when a <see cref="SubAgentDefinition"/> becomes a <see cref="SubAgentRunRequest"/>.
    /// </summary>
    public sealed class SubAgentRunRequestOptions
    {
        /// <summary>User message of the run.</summary>
        public string Prompt { get; set; }

        /// <summary>Agent names the run must not be able to call (recursion guard for allowedTools).</summary>
        public IReadOnlyCollection<string> ExcludedAgentNames { get; set; }

        /// <summary>Progress reporter of the run.</summary>
        public IProgress<ToolActivityEvent> Progress { get; set; }

        /// <summary>Session of the main chat, when the run belongs to one.</summary>
        public string ParentSessionId { get; set; }

        /// <summary>Id of the main request, when the run belongs to one.</summary>
        public string RequestId { get; set; }

        /// <summary>One of <see cref="LMLocal.Core.Models.Usage.UsageOrigins"/>.</summary>
        public string Origin { get; set; }

        /// <summary>Model override of the caller (IPC); takes precedence over the agent value.</summary>
        public string ModelOverride { get; set; }

        /// <summary>Max rounds override of the caller (IPC); takes precedence over the agent value.</summary>
        public int? MaxRoundsOverride { get; set; }

        /// <summary>
        /// Root defaults of the config for the fields the agent leaves empty.
        /// </summary>
        public SubAgentsConfig RootDefaults { get; set; }
    }

    /// <summary>
    /// Builds a <see cref="SubAgentRunRequest"/> from a configured agent.
    /// </summary>
    public static class SubAgentRunRequestFactory
    {
        /// <summary>
        /// Resolves the settings of one agent run. Values of the agent win; the root defaults, when provided, fill
        /// what the agent leaves empty.
        /// </summary>
        public static SubAgentRunRequest Create(SubAgentDefinition agent, SubAgentRunRequestOptions options)
        {
            if (agent == null)
                throw new ArgumentNullException(nameof(agent));

            var o = options ?? new SubAgentRunRequestOptions();
            var root = o.RootDefaults;

            return new SubAgentRunRequest
            {
                AgentName = agent.Id == null ? null : agent.Id.Trim(),
                Prompt = o.Prompt,
                System = agent.System,
                AllowedTools = agent.AllowedTools,
                Model = !string.IsNullOrWhiteSpace(o.ModelOverride)
                    ? o.ModelOverride
                    : (agent.Model ?? (root == null ? null : root.Model)),
                Temperature = agent.Temperature ?? (root == null ? null : root.Temperature),
                ReasoningEffort = !string.IsNullOrWhiteSpace(agent.ReasoningEffort)
                    ? agent.ReasoningEffort
                    : (root == null ? null : root.ReasoningEffort),
                MaxTokens = agent.MaxTokens ?? (root == null ? null : root.MaxTokens),
                MaxRounds = o.MaxRoundsOverride ?? agent.MaxRounds ?? (root == null ? null : root.MaxRounds),
                TimeoutSeconds = agent.TimeoutSeconds ?? (root == null ? null : root.TimeoutSeconds),
                ProviderType = !string.IsNullOrWhiteSpace(agent.ProviderType)
                    ? agent.ProviderType
                    : (root == null ? null : root.ProviderType),
                BaseUrl = !string.IsNullOrWhiteSpace(agent.CustomBaseUrl)
                    ? agent.CustomBaseUrl
                    : (root == null ? null : root.CustomBaseUrl),
                ApiKey = !string.IsNullOrWhiteSpace(agent.CustomApiKey)
                    ? agent.CustomApiKey
                    : (root == null ? null : root.CustomApiKey),
                ExcludedAgentNames = o.ExcludedAgentNames,
                Progress = o.Progress,
                ParentSessionId = o.ParentSessionId,
                RequestId = o.RequestId,
                Origin = o.Origin
            };
        }
    }
}
