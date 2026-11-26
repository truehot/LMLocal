using System;
using System.Collections.Generic;
using LMLocal.Application.Tool;

namespace LMLocal.Application.SubAgents
{
    /// <summary>
    /// All SubAgent settings for one run, resolved by the caller so the service itself does not re-read json.
    /// </summary>
    public class SubAgentRunRequest
    {
        public string AgentName { get; set; }
        public string Prompt { get; set; }
        public string ProviderType { get; set; }
        public string BaseUrl { get; set; }
        public string ApiKey { get; set; }
        public string Model { get; set; }
        public string System { get; set; }
        public double? Temperature { get; set; }
        public string ReasoningEffort { get; set; }
        public int? MaxTokens { get; set; }
        public int? TimeoutSeconds { get; set; }
        public int? MaxRounds { get; set; }
        public List<string> AllowedTools { get; set; }
        public IReadOnlyCollection<string> ExcludedAgentNames { get; set; }
        public IProgress<ToolActivityEvent> Progress { get; set; }

        /// <summary>
        /// Session of the MAIN chat this run belongs to. The service logs runs to its own session, so the parent id is
        /// passed in; null for a run started outside a chat (IPC).
        /// </summary>
        public string ParentSessionId { get; set; }

        /// <summary>Id of the user request that started the run; ties the run to the main record and its compactions.</summary>
        public string RequestId { get; set; }

        /// <summary>One of <see cref="LMLocal.Core.Models.Usage.UsageOrigins"/>; null means a chat request.</summary>
        public string Origin { get; set; }

        /// <summary>Pipeline id when the run is a step of a pipeline; null for a plain agent run.</summary>
        public string PipelineId { get; set; }

        /// <summary>One id per pipeline run, shared by all its steps; null for a plain agent run.</summary>
        public string PipelineRunId { get; set; }

        /// <summary>1-based number of the step inside the pipeline; null for a plain agent run.</summary>
        public int? PipelineStep { get; set; }

        /// <summary>Step id from the config, when the step declares one.</summary>
        public string PipelineStepId { get; set; }

        /// <summary>1-based attempt of the step; null on the first attempt.</summary>
        public int? PipelineAttempt { get; set; }

        /// <summary>1-based number of the branch inside a parallel step; null for a sequential step.</summary>
        public int? PipelineBranch { get; set; }

        /// <summary>
        /// Set by the owner of the cancellation token so a run cancelled on purpose (a sibling branch of a pipeline
        /// group failed) is not reported as a user stop. Null for a plain agent run.
        /// </summary>
        public SubAgentRunCancellationOwner Cancellation { get; set; }

        /// <summary>
        /// Copy of the request, so the caller can run the same settings again (a retry) or with another prompt
        /// without touching the request stored in the pipeline run.
        /// </summary>
        public SubAgentRunRequest Copy()
        {
            return new SubAgentRunRequest
            {
                AgentName = AgentName,
                Prompt = Prompt,
                ProviderType = ProviderType,
                BaseUrl = BaseUrl,
                ApiKey = ApiKey,
                Model = Model,
                System = System,
                Temperature = Temperature,
                ReasoningEffort = ReasoningEffort,
                MaxTokens = MaxTokens,
                TimeoutSeconds = TimeoutSeconds,
                MaxRounds = MaxRounds,
                AllowedTools = AllowedTools,
                ExcludedAgentNames = ExcludedAgentNames,
                Progress = Progress,
                ParentSessionId = ParentSessionId,
                RequestId = RequestId,
                Origin = Origin,
                PipelineId = PipelineId,
                PipelineRunId = PipelineRunId,
                PipelineStep = PipelineStep,
                PipelineStepId = PipelineStepId,
                PipelineAttempt = PipelineAttempt,
                PipelineBranch = PipelineBranch,
                Cancellation = Cancellation
            };
        }
    }
}
