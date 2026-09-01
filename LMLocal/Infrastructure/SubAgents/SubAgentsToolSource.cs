using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.SubAgents;
using LMLocal.Core.Common;
using LMLocal.Core.Models;

namespace LMLocal.Infrastructure.SubAgents
{
    /// <summary>
    /// Tool source for SubAgents: resolves an agent tool name to a run of <see cref="ISubAgentsService"/>.
    /// </summary>
    public interface ISubAgentsToolSource
    {
        /// <summary>
        /// True when the name is a currently enabled SubAgent.
        /// </summary>
        bool ToolExists(string toolName);

        /// <summary>
        /// Runs the agent 'toolName' with a single 'task' string parameter.
        /// </summary>
        Task<object> ExecuteAsync(
            string toolName,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken);

        /// <summary>
        /// Returns the external timeout for the given agent tool, or null when the agent's timeout is off (0) or the agent is not enabled.
        /// </summary>
        TimeSpan? GetToolTimeout(string toolName);

        /// <summary>
        /// Returns the display name for the given agent tool (falls back to the agent id).
        /// </summary>
        string GetDisplayName(string toolName);
    }

    internal class SubAgentsToolSource : ISubAgentsToolSource
    {
        private static readonly TimeSpan TimeoutGrace = TimeSpan.FromSeconds(5);

        private readonly ISubAgentsCatalog _catalog;
        private readonly ISettingsManager _settingsManager;
        private readonly Func<ISubAgentsService> _subAgentsServiceResolver;

        private const string TaskParam = "task";

        public SubAgentsToolSource(
            ISubAgentsCatalog catalog,
            ISettingsManager settingsManager,
            Func<ISubAgentsService> subAgentsServiceResolver)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _subAgentsServiceResolver = subAgentsServiceResolver ?? throw new ArgumentNullException(nameof(subAgentsServiceResolver));
        }

        public bool ToolExists(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return false;

            var settings = _settingsManager.Current;
            if (settings == null || !settings.EnableSubAgents || !settings.EnableAiTools)
                return false;

            return FindAgent(toolName) != null;
        }

        public async Task<object> ExecuteAsync(
            string toolName,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            try
            {
                var settings = _settingsManager.Current;
                if (settings == null || !settings.EnableSubAgents)
                {
                    return new SubAgentsRunResponse { Success = false, Error = "SubAgents are disabled." };
                }
                if (!settings.EnableAiTools)
                {
                    return new SubAgentsRunResponse { Success = false, Error = "SubAgents are unavailable because AI tools are disabled." };
                }

                var agent = FindAgent(toolName);
                if (agent == null)
                    return new SubAgentsRunResponse { Success = false, Error = $"SubAgent '{toolName}' not found." };

                if (parameters == null || !parameters.TryGetValue(TaskParam, out var taskObj) ||
                    !(taskObj is string task) || string.IsNullOrWhiteSpace(task))
                {
                    return new SubAgentsRunResponse { Success = false, Error = "Parameter 'task' is required." };
                }

                var config = _catalog.TryGetSnapshot();
                var excludedAgentNames = (config?.Agents ?? new List<SubAgentDefinition>())
                    .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Id))
                    .Select(a => a.Id.Trim())
                    .ToList();

                var request = new SubAgentRunRequest
                {
                    AgentName = agent.Id.Trim(),
                    Prompt = task.Trim(),
                    ProviderType = agent.ProviderType,
                    BaseUrl = agent.CustomBaseUrl,
                    ApiKey = agent.CustomApiKey,
                    Model = agent.Model,
                    System = agent.System,
                    Temperature = agent.Temperature,
                    MaxTokens = agent.MaxTokens,
                    TimeoutSeconds = agent.TimeoutSeconds,
                    MaxRounds = agent.MaxRounds,
                    AllowedTools = agent.AllowedTools,
                    ExcludedAgentNames = excludedAgentNames
                };

                return await _subAgentsServiceResolver()
                    .ExecutePromptAsync(request, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"SubAgentsToolSource: failed to run SubAgent '{toolName}'", ex);
                return new SubAgentsRunResponse { Success = false, Error = ex.Message };
            }
        }

        public TimeSpan? GetToolTimeout(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return null;

            var settings = _settingsManager.Current;
            if (settings == null || !settings.EnableSubAgents || !settings.EnableAiTools)
                return null;

            var agent = FindAgent(toolName);
            if (agent == null)
                return null;

            if (!agent.TimeoutSeconds.HasValue || agent.TimeoutSeconds.Value <= 0)
                return null;

            return TimeSpan.FromSeconds(agent.TimeoutSeconds.Value) + TimeoutGrace;
        }

        public string GetDisplayName(string toolName)
        {
            var agent = FindAgent(toolName);
            if (agent == null)
                return toolName?.Trim() ?? string.Empty;

            return !string.IsNullOrWhiteSpace(agent.DisplayName)
                ? agent.DisplayName.Trim()
                : agent.Id.Trim();
        }

        private SubAgentDefinition FindAgent(string toolName)
        {
            var name = toolName?.Trim();
            if (string.IsNullOrEmpty(name))
                return null;

            var agents = _catalog.GetEnabledAgents();
            if (agents == null)
                return null;

            foreach (var agent in agents)
            {
                if (agent != null &&
                    string.Equals(agent.Id.Trim(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return agent;
                }
            }

            return null;
        }
    }
}
