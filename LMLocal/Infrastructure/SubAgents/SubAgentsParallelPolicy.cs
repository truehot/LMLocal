using System;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.SubAgents;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs;

namespace LMLocal.Infrastructure.SubAgents
{
    /// <summary>
    /// Default <see cref="ISubAgentsParallelPolicy"/>.
    /// </summary>
    internal sealed class SubAgentsParallelPolicy : ISubAgentsParallelPolicy
    {
        private readonly ISubAgentsCatalog _catalog;
        private readonly ISettingsManager _settingsManager;
        private readonly IBuiltInVsToolProvider _builtInTools;

        public SubAgentsParallelPolicy(
            ISubAgentsCatalog catalog,
            ISettingsManager settingsManager,
            IBuiltInVsToolProvider builtInTools)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _builtInTools = builtInTools ?? throw new ArgumentNullException(nameof(builtInTools));
        }

        public bool IsParallelEnabled(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return false;

            var settings = _settingsManager.Current;
            if (settings == null || !settings.EnableSubAgents || !settings.EnableAiTools)
                return false;

            var agent = FindAgent(toolName);
            if (agent == null || !agent.Parallel)
                return false;

            return HasOnlyReadOnlyTools(agent);
        }

        public int ResolveMaxParallel(string toolName)
            => Math.Min(ResolveConfiguredMaxParallel(toolName), SubAgentsConfig.MaxParallelLimit);

        private int ResolveConfiguredMaxParallel(string toolName)
        {
            var agent = FindAgent(toolName);
            if (agent != null && agent.MaxParallel.HasValue && agent.MaxParallel.Value >= 1)
                return agent.MaxParallel.Value;

            var topLevel = _catalog.TryGetSnapshot()?.MaxParallel;
            if (topLevel.HasValue && topLevel.Value >= 1)
                return topLevel.Value;

            return SubAgentsConfig.DefaultMaxParallel;
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
                if (agent != null && string.Equals(agent.Id?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return agent;
            }

            return null;
        }

        private bool HasOnlyReadOnlyTools(SubAgentDefinition agent)
        {
            var allowed = agent.AllowedTools;
            if (allowed == null || allowed.Count == 0)
                return true; // reasoning-only agent: no tools -> safe.

            foreach (var name in allowed)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var trimmed = name.Trim();

                // Only built-in tools can run inside a SubAgent; unknown/MCP names are not executed there.
                if (!_builtInTools.ToolExists(trimmed))
                    continue;

                if (_builtInTools.GetToolAccessLevel(trimmed) != ToolAccessLevel.ReadOnly)
                    return false;
            }

            return true;
        }
    }
}
