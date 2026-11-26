using System;
using System.Collections.Generic;
using LMLocal.Application.Tool;

namespace LMLocal.Application.SubAgents
{
    /// <summary>
    /// One pipeline run: the task received from the main model, the fully resolved steps and the context of the chat.
    /// </summary>
    public class SubAgentPipelineRunRequest
    {
        /// <summary>Pipeline id; null-safe, used in logs, errors and usage.</summary>
        public string PipelineId { get; set; }

        /// <summary>Display name of the pipeline, for logs.</summary>
        public string DisplayName { get; set; }

        /// <summary>The task the main model passed; substituted into <c>{{task}}</c>.</summary>
        public string Task { get; set; }

        /// <summary><see cref="LMLocal.Core.Models.SubAgentPipelineDefinition.OutputAll"/> or "last".</summary>
        public string Output { get; set; }

        /// <summary>Width of a parallel step group: the step value wins, then this value; null means the default.</summary>
        public int? MaxParallel { get; set; }

        /// <summary>Steps in run order.</summary>
        public IReadOnlyList<SubAgentPipelineStepRun> Steps { get; set; }

        /// <summary>Reporter of the main chat tool card.</summary>
        public IProgress<ToolActivityEvent> Progress { get; set; }

        /// <summary>Session of the main chat, when the run belongs to one.</summary>
        public string ParentSessionId { get; set; }

        /// <summary>Id of the main request, when the run belongs to one.</summary>
        public string RequestId { get; set; }

        /// <summary>One of <see cref="LMLocal.Core.Models.Usage.UsageOrigins"/>.</summary>
        public string Origin { get; set; }
    }

    /// <summary>
    /// One step of a pipeline run: either a single run (<see cref="Request"/>) or a group of branches
    /// that execute at the same time (<see cref="Branches"/>).
    /// </summary>
    public class SubAgentPipelineStepRun
    {
        /// <summary>Step id from the config, when it declares one.</summary>
        public string StepId { get; set; }

        /// <summary>Id when set, else the agent: used in progress, logs and headings.</summary>
        public string Label { get; set; }

        /// <summary>Prompt template with the placeholders still unresolved.</summary>
        public string PromptTemplate { get; set; }

        /// <summary>Prepared run of the single step; null for a group.</summary>
        public SubAgentRunRequest Request { get; set; }

        /// <summary>Agent display name, used in the progress prefix.</summary>
        public string AgentDisplayName { get; set; }

        /// <summary>Retries of this step (already bounded by the config validation).</summary>
        public int? Retries { get; set; }

        /// <summary>Branches of the parallel group; null or empty for a sequential step.</summary>
        public IReadOnlyList<SubAgentPipelineBranchRun> Branches { get; set; }

        /// <summary>Width of this group: the group value wins, then the pipeline value, then the root value.</summary>
        public int? MaxParallel { get; set; }

        /// <summary>True when a final branch error must not cancel the other branches.</summary>
        public bool ContinueOnBranchError { get; set; }

        /// <summary>True when the step runs several branches at the same time.</summary>
        public bool IsGroup
        {
            get { return Branches != null && Branches.Count > 0; }
        }
    }

    /// <summary>
    /// One branch of a parallel pipeline step.
    /// </summary>
    public class SubAgentPipelineBranchRun
    {
        /// <summary>Branch id from the config, when it declares one.</summary>
        public string BranchId { get; set; }

        /// <summary>Id when set, else the agent: used in progress and headings.</summary>
        public string Label { get; set; }

        /// <summary>Prompt template with the placeholders still unresolved.</summary>
        public string PromptTemplate { get; set; }

        /// <summary>Prepared run of the branch.</summary>
        public SubAgentRunRequest Request { get; set; }

        /// <summary>Agent display name, used in the progress prefix.</summary>
        public string AgentDisplayName { get; set; }

        /// <summary>Retries of the branch; the group value is already applied by the request factory.</summary>
        public int? Retries { get; set; }
    }

    /// <summary>
    /// Result of building a pipeline run: either the request or the reason why it cannot be built.
    /// </summary>
    public sealed class SubAgentPipelineRequestResult
    {
        /// <summary>The ready request; null when <see cref="Error"/> is set.</summary>
        public SubAgentPipelineRunRequest Request { get; set; }

        /// <summary>Human-readable reason why the pipeline cannot run; null on success.</summary>
        public string Error { get; set; }

        /// <summary>1-based number of the step that could not be resolved, when the error is a step error.</summary>
        public int? FailedStep { get; set; }
    }
}
