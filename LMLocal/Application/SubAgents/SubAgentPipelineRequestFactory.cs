using System;
using System.Collections.Generic;
using System.Linq;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;

namespace LMLocal.Application.SubAgents
{
    /// <summary>
    /// Turns a configured pipeline into a runnable <see cref="SubAgentPipelineRunRequest"/>.
    /// </summary>
    public static class SubAgentPipelineRequestFactory
    {
        /// <summary>
        /// Resolves every step agent of <paramref name="pipeline"/> and builds the request.
        /// Returns an error result (never throws) when a step agent is missing, or disabled while
        /// <paramref name="requireEnabledAgents"/> is true.
        /// </summary>
        public static SubAgentPipelineRequestResult Create(
            SubAgentPipelineDefinition pipeline,
            SubAgentsConfig config,
            string task,
            IProgress<ToolActivityEvent> progress = null,
            string parentSessionId = null,
            string requestId = null,
            string origin = null,
            string modelOverride = null,
            int? maxRoundsOverride = null,
            Func<SubAgentDefinition, bool> isAgentReadOnly = null,
            bool requireEnabledAgents = true)
        {
            if (pipeline == null)
                return Error("Pipeline is required.");

            var agents = config?.Agents;
            var excluded = BuildExcludedAgentNames(agents);
            var steps = new List<SubAgentPipelineStepRun>();
            var definitions = pipeline.Steps ?? new List<SubAgentPipelineStep>();

            for (int i = 0; i < definitions.Count; i++)
            {
                var step = definitions[i];
                var number = i + 1;

                if (step == null)
                    return Error($"pipeline '{pipeline.Id}' step {number}: step is empty", number);

                var stepRun = new SubAgentPipelineStepRun
                {
                    StepId = Trim(step.Id),
                    Label = step.Label,
                    PromptTemplate = step.Prompt,
                    Retries = step.Retries,
                    MaxParallel = step.MaxParallel,
                    ContinueOnBranchError = step.ContinuesOnBranchError
                };

                if (step.IsParallelGroup)
                {
                    var branches = new List<SubAgentPipelineBranchRun>(step.Parallel.Count);

                    for (int b = 0; b < step.Parallel.Count; b++)
                    {
                        var branch = step.Parallel[b];
                        var branchNumber = b + 1;

                        if (branch == null)
                            return Error($"pipeline '{pipeline.Id}' step {number} branch {branchNumber}: branch is empty", number);

                        var agent = FindAgent(agents, branch.Agent, requireEnabledAgents);
                        if (agent == null)
                            return Error($"pipeline '{pipeline.Id}' step {number} branch {branchNumber}: agent '{branch.Agent}' {MissingAgentText(requireEnabledAgents)}", number);

                        if (isAgentReadOnly != null && !isAgentReadOnly(agent))
                        {
                            return Error(
                                $"pipeline '{pipeline.Id}' step {number} branch {branchNumber}: agent '{branch.Agent}' uses write tools "
                                + "(the branches of a parallel group run at the same time and may only use read-only agents)",
                                number);
                        }

                        branches.Add(new SubAgentPipelineBranchRun
                        {
                            BranchId = Trim(branch.Id),
                            Label = branch.Label,
                            PromptTemplate = branch.Prompt,
                            AgentDisplayName = DisplayName(agent),
                            Request = BuildRequest(agent, config, excluded, origin, modelOverride, maxRoundsOverride),
                            Retries = branch.Retries ?? step.Retries
                        });
                    }

                    stepRun.Branches = branches;
                }
                else
                {
                    var agent = FindAgent(agents, step.Agent, requireEnabledAgents);
                    if (agent == null)
                        return Error($"pipeline '{pipeline.Id}' step {number}: agent '{step.Agent}' {MissingAgentText(requireEnabledAgents)}", number);

                    stepRun.AgentDisplayName = DisplayName(agent);
                    stepRun.Request = BuildRequest(agent, config, excluded, origin, modelOverride, maxRoundsOverride);
                }

                steps.Add(stepRun);
            }

            return new SubAgentPipelineRequestResult
            {
                Request = new SubAgentPipelineRunRequest
                {
                    PipelineId = Trim(pipeline.Id),
                    DisplayName = pipeline.DisplayNameOrId,
                    Task = task,
                    Output = pipeline.OutputsAllSteps
                        ? SubAgentPipelineDefinition.OutputAll
                        : SubAgentPipelineDefinition.OutputLast,
                    MaxParallel = FirstPositive(pipeline.MaxParallel, config == null ? null : config.MaxParallel),
                    Steps = steps,
                    Progress = progress,
                    ParentSessionId = parentSessionId,
                    RequestId = requestId,
                    Origin = origin
                }
            };
        }

        private static SubAgentRunRequest BuildRequest(
            SubAgentDefinition agent,
            SubAgentsConfig config,
            IReadOnlyCollection<string> excluded,
            string origin,
            string modelOverride,
            int? maxRoundsOverride)
        {
            return SubAgentRunRequestFactory.Create(agent, new SubAgentRunRequestOptions
            {
                ExcludedAgentNames = excluded,
                Origin = origin,
                ModelOverride = modelOverride,
                MaxRoundsOverride = maxRoundsOverride,
                RootDefaults = config
            });
        }

        /// <summary>
        /// Every agent id of the snapshot: an agent may not call itself or another agent through allowedTools.
        /// </summary>
        private static IReadOnlyCollection<string> BuildExcludedAgentNames(IEnumerable<SubAgentDefinition> agents)
        {
            var names = new List<string>();
            if (agents == null)
                return names;

            foreach (var agent in agents)
            {
                if (agent == null || string.IsNullOrWhiteSpace(agent.Id))
                    continue;

                var name = agent.Id.Trim();
                if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    names.Add(name);
            }

            return names;
        }

        private static SubAgentDefinition FindAgent(IEnumerable<SubAgentDefinition> agents, string id, bool requireEnabled)
        {
            var name = Trim(id);
            if (name == null || agents == null)
                return null;

            foreach (var agent in agents)
            {
                if (agent != null
                    && (!requireEnabled || agent.Enabled)
                    && string.Equals(Trim(agent.Id), name, StringComparison.OrdinalIgnoreCase))
                {
                    return agent;
                }
            }

            return null;
        }

        private static string MissingAgentText(bool requireEnabled)
        {
            return requireEnabled ? "not found or disabled" : "not found";
        }

        private static string DisplayName(SubAgentDefinition agent)
        {
            if (agent == null)
                return string.Empty;

            return !string.IsNullOrWhiteSpace(agent.DisplayName) ? agent.DisplayName.Trim() : Trim(agent.Id) ?? string.Empty;
        }

        /// <summary>
        /// Width chain of a parallel step group: the step value wins, then the pipeline value, then the root value.
        /// Null means "not set": the runner falls back to <see cref="SubAgentsConfig.DefaultMaxParallel"/>.
        /// </summary>
        private static int? FirstPositive(int? first, int? second)
        {
            if (first.HasValue && first.Value >= 1)
                return first.Value;

            return second.HasValue && second.Value >= 1 ? second : null;
        }

        private static SubAgentPipelineRequestResult Error(string message, int? failedStep = null)
        {
            return new SubAgentPipelineRequestResult { Error = message, FailedStep = failedStep };
        }

        private static string Trim(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
