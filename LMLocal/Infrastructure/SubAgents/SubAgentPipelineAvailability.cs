using System;
using System.Collections.Generic;
using System.Linq;
using LMLocal.Core.Models;

namespace LMLocal.Infrastructure.SubAgents
{
    /// <summary>
    /// Answers why a pipeline cannot run right now. The same reasons are needed twice: by the tool queue policy
    /// (to decide whether the pipeline becomes a tool of the main model) and by the dialog (to show the reason),
    /// and they must never disagree.
    /// </summary>
    internal static class SubAgentPipelineAvailability
    {
        /// <summary>
        /// Null when the pipeline is structurally correct and every agent of its steps exists and is enabled.
        /// Otherwise the first reason, ready to show in the dialog and in the log.
        /// </summary>
        internal static string GetUnavailableReason(
            SubAgentPipelineDefinition pipeline,
            SubAgentsConfig config,
            IReadOnlyCollection<string> builtInToolNames,
            Func<SubAgentDefinition, bool> isAgentUsable = null,
            Func<SubAgentDefinition, bool> isAgentReadOnly = null)
        {
            if (pipeline == null)
                return "pipeline is not defined";

            var id = Trim(pipeline.Id);
            if (id == null)
                return "pipeline id is missing";

            var agents = config == null ? null : config.Agents;

            if (agents?.Any(a => a != null && string.Equals(Trim(a.Id), id, StringComparison.OrdinalIgnoreCase)) == true)
                return $"id collides with agent '{id}'";

            if (builtInToolNames != null
                && builtInToolNames.Any(name => string.Equals(name == null ? null : name.Trim(), id, StringComparison.OrdinalIgnoreCase)))
            {
                return $"id collides with built-in tool '{id}'";
            }

            var structural = pipeline.Validate();
            if (structural.Count > 0)
                return structural[0];

            foreach (var step in pipeline.Steps ?? new List<SubAgentPipelineStep>())
            {
                if (step == null)
                    continue;

                var isBranch = step.IsParallelGroup;

                if (!isBranch)
                {
                    var single = StepAgentReason(step.Agent, step, agents, isAgentUsable, isAgentReadOnly, false);
                    if (single != null)
                        return single;

                    continue;
                }

                foreach (var branch in step.Parallel)
                {
                    if (branch == null)
                        continue;

                    var reason = StepAgentReason(branch.Agent, branch, agents, isAgentUsable, isAgentReadOnly, true);
                    if (reason != null)
                        return reason;
                }
            }

            return null;
        }

        /// <summary>
        /// Null when the agent of one step (or branch) exists, is enabled and passes the optional checks; otherwise the
        /// first reason.
        /// </summary>
        private static string StepAgentReason(
            string rawAgentName,
            SubAgentPipelineStep step,
            IEnumerable<SubAgentDefinition> agents,
            Func<SubAgentDefinition, bool> isAgentUsable,
            Func<SubAgentDefinition, bool> isAgentReadOnly,
            bool isBranch)
        {
            var agentName = Trim(rawAgentName);
            if (agentName == null)
                return null;

            var agent = FindAgent(agents, agentName);
            if (agent == null)
                return $"agent '{agentName}' not found";

            if (!agent.Enabled)
                return $"agent '{agentName}' is disabled";

            if (isAgentUsable != null && !isAgentUsable(agent))
                return $"agent '{agentName}' needs tools that are unavailable in the current mode";

            if (isBranch && isAgentReadOnly != null && !isAgentReadOnly(agent))
            {
                return $"branch agent '{agentName}' uses write tools "
                    + "(the branches of a parallel group run at the same time and may only use read-only agents)";
            }

            return null;
        }

        /// <summary>
        /// Enabled agent with this id from the config, or null.
        /// </summary>
        internal static SubAgentDefinition FindEnabledAgent(SubAgentsConfig config, string id)
        {
            var name = Trim(id);
            if (name == null)
                return null;

            var agent = FindAgent(config == null ? null : config.Agents, name);
            return agent != null && agent.Enabled ? agent : null;
        }

        private static SubAgentDefinition FindAgent(IEnumerable<SubAgentDefinition> agents, string id)
        {
            if (agents == null)
                return null;

            var name = Trim(id);
            if (name == null)
                return null;

            foreach (var agent in agents)
            {
                if (agent != null && string.Equals(Trim(agent.Id), name, StringComparison.OrdinalIgnoreCase))
                    return agent;
            }

            return null;
        }

        private static string Trim(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
