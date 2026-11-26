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

            var name = toolName.Trim();

            var agent = FindAgent(name);
            if (agent != null)
                return agent.Parallel && HasOnlyReadOnlyTools(agent);

            var pipeline = FindPipeline(name);
            return pipeline != null && IsPipelineParallel(pipeline);
        }

        public int ResolveMaxParallel(string toolName)
            => Math.Min(ResolveConfiguredMaxParallel(toolName), SubAgentsConfig.MaxParallelLimit);

        private int ResolveConfiguredMaxParallel(string toolName)
        {
            var name = toolName == null ? null : toolName.Trim();

            var agent = FindAgent(name);
            if (agent?.MaxParallel.HasValue == true && agent.MaxParallel.Value >= 1)
                return agent.MaxParallel.Value;

            var pipeline = FindPipeline(name);
            if (pipeline?.MaxParallel.HasValue == true && pipeline.MaxParallel.Value >= 1)
                return pipeline.MaxParallel.Value;

            var topLevel = _catalog.TryGetSnapshot()?.MaxParallel;
            if (topLevel.HasValue && topLevel.Value >= 1)
                return topLevel.Value;

            return SubAgentsConfig.DefaultMaxParallel;
        }

        /// <summary>
        /// A pipeline may join a parallel group of the main round only when it opted in, when every agent of its steps
        /// is read-only, and when it has no parallel step of its own: a group inside a group would multiply the
        /// concurrent runs by the two widths.
        /// </summary>
        private bool IsPipelineParallel(SubAgentPipelineDefinition pipeline)
        {
            var root = _catalog.TryGetSnapshot();
            var optedIn = pipeline.Parallel.HasValue
                ? pipeline.Parallel.Value
                : (root != null && root.Parallel);

            if (!optedIn)
                return false;

            if (pipeline.HasParallelSteps)
                return false;

            foreach (var agentName in pipeline.GetAgentNames())
            {
                var agent = FindAgent(agentName);
                if (agent == null || !HasOnlyReadOnlyTools(agent))
                    return false;
            }

            return true;
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

        private SubAgentPipelineDefinition FindPipeline(string toolName)
        {
            var name = toolName?.Trim();
            if (string.IsNullOrEmpty(name))
                return null;

            var pipelines = _catalog.GetEnabledPipelines();
            if (pipelines == null)
                return null;

            foreach (var pipeline in pipelines)
            {
                if (pipeline != null && string.Equals(pipeline.Id?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return pipeline;
            }

            return null;
        }

        private bool HasOnlyReadOnlyTools(SubAgentDefinition agent)
        {
            return SubAgentToolAccess.HasOnlyReadOnlyTools(agent, _builtInTools);
        }
    }
}
