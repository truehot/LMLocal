using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.SubAgents;
using LMLocal.Application.Tool;
using LMLocal.Application.Usage;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs;

namespace LMLocal.Infrastructure.SubAgents
{
    /// <summary>
    /// Tool source for SubAgents: resolves an agent or pipeline tool name to a run of <see cref="ISubAgentsService"/>
    /// (a single agent) or of <see cref="ISubAgentPipelineRunner"/> (a fixed sequence of agents).
    /// </summary>
    public interface ISubAgentsToolSource
    {
        /// <summary>
        /// True when the name is a currently enabled SubAgent or pipeline.
        /// </summary>
        bool ToolExists(string toolName);

        /// <summary>
        /// Runs the agent or pipeline 'toolName' with a single 'task' string parameter, streaming progress steps when
        /// a reporter is supplied.
        /// </summary>
        Task<object> ExecuteAsync(
            string toolName,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken,
            IProgress<ToolActivityEvent> progress = null,
            UsageCallContext context = null);

        /// <summary>
        /// Returns the external timeout for the given agent or pipeline tool.
        /// </summary>
        TimeSpan? GetToolTimeout(string toolName);

        /// <summary>
        /// Returns the display name for the given agent or pipeline tool (falls back to the id).
        /// </summary>
        string GetDisplayName(string toolName);
    }

    internal class SubAgentsToolSource : ISubAgentsToolSource
    {
        private static readonly TimeSpan TimeoutGrace = TimeSpan.FromSeconds(20);

        private readonly ISubAgentsCatalog _catalog;
        private readonly ISettingsManager _settingsManager;
        private readonly Func<ISubAgentsService> _subAgentsServiceResolver;
        private readonly ISubAgentPipelineRunner _pipelineRunner;
        private readonly IBuiltInVsToolProvider _builtInTools;

        /// <summary>
        /// Name of the single tool argument that carries the SubAgent prompt.
        /// </summary>
        internal const string TaskParam = "task";

        public SubAgentsToolSource(
            ISubAgentsCatalog catalog,
            ISettingsManager settingsManager,
            Func<ISubAgentsService> subAgentsServiceResolver,
            ISubAgentPipelineRunner pipelineRunner,
            IBuiltInVsToolProvider builtInTools = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _subAgentsServiceResolver = subAgentsServiceResolver ?? throw new ArgumentNullException(nameof(subAgentsServiceResolver));
            _pipelineRunner = pipelineRunner ?? throw new ArgumentNullException(nameof(pipelineRunner));
            _builtInTools = builtInTools;
        }

        public bool ToolExists(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return false;

            var settings = _settingsManager.Current;
            if (settings?.EnableSubAgents != true || !settings.EnableAiTools)
                return false;

            return FindAgent(toolName) != null || FindPipeline(toolName) != null;
        }

        public async Task<object> ExecuteAsync(
            string toolName,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken,
            IProgress<ToolActivityEvent> progress = null,
            UsageCallContext context = null)
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
                if (agent != null)
                    return await ExecuteAgentAsync(agent, parameters, cancellationToken, progress, context).ConfigureAwait(false);

                var pipeline = FindPipeline(toolName);
                if (pipeline != null)
                    return await ExecutePipelineAsync(pipeline, parameters, cancellationToken, progress, context).ConfigureAwait(false);

                return new SubAgentsRunResponse
                {
                    Success = false,
                    Error = $"SubAgent '{toolName}' not found.",
                    FailureKind = SubAgentFailureKind.InvalidRequest
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"SubAgentsToolSource: failed to run SubAgent '{toolName}'", ex);
                return new SubAgentsRunResponse
                {
                    Success = false,
                    Error = ex.Message,
                    FailureKind = SubAgentFailureKind.Unexpected
                };
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
            if (agent != null)
            {
                if (agent.TimeoutSeconds.HasValue && agent.TimeoutSeconds.Value <= 0)
                    return Timeout.InfiniteTimeSpan;

                var seconds = agent.TimeoutSeconds ?? SubAgentsService.DefaultTimeoutSeconds;
                return TimeSpan.FromSeconds(seconds) + TimeoutGrace;
            }

            var pipeline = FindPipeline(toolName);
            if (pipeline == null)
                return null;

            return PipelineTimeout(pipeline);
        }

        public string GetDisplayName(string toolName)
        {
            var agent = FindAgent(toolName);
            if (agent != null)
            {
                return !string.IsNullOrWhiteSpace(agent.DisplayName)
                    ? agent.DisplayName.Trim()
                    : agent.Id.Trim();
            }

            var pipeline = FindPipeline(toolName);
            if (pipeline != null)
                return pipeline.DisplayNameOrId;

            return toolName?.Trim() ?? string.Empty;
        }

        private async Task<object> ExecuteAgentAsync(
            SubAgentDefinition agent,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken,
            IProgress<ToolActivityEvent> progress,
            UsageCallContext context)
        {
            if (!TryReadTask(parameters, out var task))
                return InvalidTask();

            var config = _catalog.TryGetSnapshot();
            var excludedAgentNames = (config?.Agents ?? new List<SubAgentDefinition>())
                .Where(a => a != null && !string.IsNullOrWhiteSpace(a.Id))
                .Select(a => a.Id.Trim())
                .ToList();

            var request = SubAgentRunRequestFactory.Create(agent, new SubAgentRunRequestOptions
            {
                Prompt = task.Trim(),
                ExcludedAgentNames = excludedAgentNames,
                Progress = progress,
                ParentSessionId = context != null ? context.SessionId : null,
                RequestId = context != null ? context.RequestId : null,
                Origin = context != null ? context.Origin : null
            });

            return await _subAgentsServiceResolver()
                .ExecutePromptAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<object> ExecutePipelineAsync(
            SubAgentPipelineDefinition pipeline,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken,
            IProgress<ToolActivityEvent> progress,
            UsageCallContext context)
        {
            if (!TryReadTask(parameters, out var task))
                return InvalidTask();

            var config = _catalog.TryGetSnapshot();

            var built = SubAgentPipelineRequestFactory.Create(
                pipeline,
                config,
                task.Trim(),
                progress,
                context != null ? context.SessionId : null,
                context != null ? context.RequestId : null,
                context != null ? context.Origin : null,
                isAgentReadOnly: agent => SubAgentToolAccess.HasOnlyReadOnlyTools(agent, _builtInTools));

            if (built.Request == null)
            {
                return new SubAgentsRunResponse
                {
                    Success = false,
                    Error = built.Error,
                    FailureKind = SubAgentFailureKind.InvalidRequest
                };
            }

            return await _pipelineRunner.RunAsync(built.Request, cancellationToken).ConfigureAwait(false);
        }

        private static bool TryReadTask(Dictionary<string, object> parameters, out string task)
        {
            task = null;

            if (parameters == null || !parameters.TryGetValue(TaskParam, out var taskObj))
                return false;

            task = taskObj as string;
            return !string.IsNullOrWhiteSpace(task);
        }

        private static SubAgentsRunResponse InvalidTask()
        {
            return new SubAgentsRunResponse
            {
                Success = false,
                Error = "Parameter 'task' is required.",
                FailureKind = SubAgentFailureKind.InvalidRequest
            };
        }

        /// <summary>
        /// Timeout of the whole pipeline: the sum of its steps, where a parallel group counts as its slowest branch.
        /// </summary>
        private TimeSpan? PipelineTimeout(SubAgentPipelineDefinition pipeline)
        {
            var total = TimeSpan.Zero;
            var steps = pipeline.Steps ?? new List<SubAgentPipelineStep>();

            foreach (var step in steps)
            {
                if (step == null)
                    continue;

                if (step.IsParallelGroup)
                {
                    var group = TimeSpan.Zero;

                    foreach (var branch in step.Parallel)
                    {
                        if (branch == null)
                            continue;

                        var branchTimeout = StepTimeout(branch, step.Retries);
                        if (!branchTimeout.HasValue)
                            return Timeout.InfiniteTimeSpan;

                        if (branchTimeout.Value > group)
                            group = branchTimeout.Value;
                    }

                    total += group;
                    continue;
                }

                var stepTimeout = StepTimeout(step, null);
                if (!stepTimeout.HasValue)
                    return Timeout.InfiniteTimeSpan;

                total += stepTimeout.Value;
            }

            return total + TimeoutGrace;
        }

        /// <summary>
        /// Timeout of one step: the agent timeout plus the pauses before its retries. The retried runs themselves are
        /// not budgeted, since a provider failure usually comes fast. Null means the step has no timeout at all.
        /// </summary>
        private TimeSpan? StepTimeout(SubAgentPipelineStep step, int? inheritedRetries)
        {
            var agent = FindAgent(step.Agent);

            var seconds = agent != null && agent.TimeoutSeconds.HasValue
                ? agent.TimeoutSeconds.Value
                : SubAgentsService.DefaultTimeoutSeconds;

            if (seconds <= 0)
                return null;

            var retries = step.Retries ?? inheritedRetries ?? 0;
            if (retries > SubAgentPipelineStep.MaxRetries)
                retries = SubAgentPipelineStep.MaxRetries;

            double pauses = 0;
            for (int i = 0; i < retries && i < SubAgentPipelineRunner.RetryDelaySeconds.Length; i++)
                pauses += SubAgentPipelineRunner.RetryDelaySeconds[i];

            return TimeSpan.FromSeconds(seconds + pauses);
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
                if (pipeline != null &&
                    pipeline.Id != null &&
                    string.Equals(pipeline.Id.Trim(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return pipeline;
                }
            }

            return null;
        }
    }
}
