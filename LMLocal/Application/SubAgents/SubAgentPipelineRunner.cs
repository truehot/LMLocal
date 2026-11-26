using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;

namespace LMLocal.Application.SubAgents
{
    /// <summary>
    /// Runs the steps of a pipeline one after another (and the branches of a parallel step at the same time) on top
    /// of <see cref="ISubAgentsService"/>.
    /// </summary>
    internal sealed class SubAgentPipelineRunner : ISubAgentPipelineRunner
    {
        /// <summary>Pause before the second, third and fourth attempt after a transient provider failure.</summary>
        internal static readonly int[] RetryDelaySeconds = { 3, 5, 8 };

        private readonly ISubAgentsService _subAgentsService;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        public SubAgentPipelineRunner(ISubAgentsService subAgentsService, Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            _subAgentsService = subAgentsService ?? throw new ArgumentNullException(nameof(subAgentsService));
            _delay = delay ?? Task.Delay;
        }

        public async Task<SubAgentsRunResponse> RunAsync(SubAgentPipelineRunRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var pipelineRunId = Guid.NewGuid().ToString("N");
            var pipelineId = Trim(request.PipelineId) ?? string.Empty;
            var steps = request.Steps ?? new List<SubAgentPipelineStepRun>();
            var total = steps.Count;

            var context = new RunContext(pipelineId, pipelineRunId);
            var progress = new SubAgentPipelineProgress(request.Progress);
            var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var answers = new List<AnsweredStep>();

            InternalLogger.Info($"SubAgentPipelineRunner: pipeline '{pipelineId}' run {pipelineRunId}: start, {total} steps");

            if (total == 0)
                return Fail(context, "pipeline '" + pipelineId + "' has no steps", SubAgentFailureKind.InvalidRequest);

            string prev = null;

            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var step = steps[i];
                var number = i + 1;
                var label = number + "/" + total + " " + (Trim(step == null ? null : step.Label) ?? "step");

                if (step == null)
                    return Fail(context, "pipeline '" + pipelineId + "' step " + label + ": step is empty", SubAgentFailureKind.InvalidRequest);

                if (!step.IsGroup && step.Request == null)
                    return Fail(context, "pipeline '" + pipelineId + "' step " + label + ": agent is not resolved", SubAgentFailureKind.InvalidRequest);

                StepOutcome outcome;

                if (step.IsGroup)
                {
                    outcome = await RunGroupAsync(request, step, number, total, label, prev, outputs, progress, context, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    var target = ToTarget(step);
                    var stepProgress = progress.BeginStep(label, target.AgentDisplayName);
                    outcome = await RunTargetAsync(
                            request, target, number, label, prev, outputs, stepProgress, context, null, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (!outcome.Success)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var error = outcome.HasPosition
                        ? outcome.Error
                        : "pipeline '" + pipelineId + "' step " + label + ": " + outcome.Error;

                    return Fail(context, error, outcome.FailureKind);
                }

                prev = outcome.Content;
                answers.Add(new AnsweredStep
                {
                    Number = number,
                    Total = total,
                    Label = Trim(step.Label) ?? "step",
                    Content = outcome.Content
                });

                if (!string.IsNullOrWhiteSpace(step.StepId))
                    outputs[step.StepId.Trim()] = outcome.Content;

                if (outcome.Outputs != null)
                {
                    foreach (var pair in outcome.Outputs)
                        outputs[pair.Key] = pair.Value;
                }
            }

            var response = context.Aggregate;
            response.Success = true;
            response.FailureKind = SubAgentFailureKind.None;
            response.Content = BuildFinalContent(request, answers);
            context.Complete();

            InternalLogger.Info(
                $"SubAgentPipelineRunner: pipeline '{pipelineId}' run {pipelineRunId}: completed, "
                + $"{total} steps, {response.Rounds} rounds, {response.DurationMs} ms");

            return response;
        }

        /// <summary>
        /// Runs a group of branches at the same time.
        /// </summary>
        private async Task<StepOutcome> RunGroupAsync(
            SubAgentPipelineRunRequest request,
            SubAgentPipelineStepRun step,
            int stepNumber,
            int total,
            string stepLabel,
            string prev,
            IReadOnlyDictionary<string, string> outputs,
            SubAgentPipelineProgress progress,
            RunContext context,
            CancellationToken cancellationToken)
        {
            var branches = step.Branches;
            var count = branches.Count;
            var width = ResolveWidth(step.MaxParallel.HasValue ? step.MaxParallel : request.MaxParallel);
            var groupProgress = progress.BeginGroup(stepLabel, count);
            var outcomes = new StepOutcome[count];
            var owner = new SubAgentRunCancellationOwner();

            int stopCause = -1;

            using (var groupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var gate = new SemaphoreSlim(width, width))
            {
                var tasks = new List<Task>(count);

                void StopGroup(int index)
                {
                    Interlocked.CompareExchange(ref stopCause, index, -1);

                    owner.MarkStoppedByCaller();
                    groupCts.Cancel();
                }

                string CancelledBySibling()
                {
                    var cause = Volatile.Read(ref stopCause);
                    return cause >= 0
                        ? "cancelled: branch " + (cause + 1) + "/" + count + " failed"
                        : "cancelled: the run was cancelled by its caller";
                }

                for (int b = 0; b < count; b++)
                {
                    var index = b;

                    tasks.Add(Task.Run(async () =>
                    {
                        var branch = branches[index];

                        if (branch == null || branch.Request == null)
                        {
                            outcomes[index] = StepOutcome.Fail(SubAgentFailureKind.InvalidRequest, "agent is not resolved");
                            groupProgress.MarkCompleted();
                            if (!step.ContinueOnBranchError)
                                StopGroup(index);
                            return;
                        }

                        var branchProgress = groupProgress.CreateBranchProgress(branch.AgentDisplayName);
                        var entered = false;

                        try
                        {
                            try
                            {
                                await gate.WaitAsync(groupCts.Token).ConfigureAwait(false);
                                entered = true;
                            }
                            catch (OperationCanceledException)
                            {
                                outcomes[index] = StepOutcome.Fail(
                                    SubAgentFailureKind.Cancelled,
                                    CancelledBySibling(),
                                    cancelledByCaller: true);
                            }

                            if (entered)
                            {
                                try
                                {
                                    var result = await RunTargetAsync(
                                            request,
                                            ToTarget(branch, index + 1),
                                            stepNumber,
                                            stepLabel,
                                            prev,
                                            outputs,
                                            branchProgress,
                                            context,
                                            owner,
                                            groupCts.Token)
                                        .ConfigureAwait(false);

                                    if (result?.Success == false
                                        && result.FailureKind == SubAgentFailureKind.Cancelled
                                        && owner.StoppedByCaller
                                        && !cancellationToken.IsCancellationRequested)
                                    {
                                        result = StepOutcome.Fail(
                                            SubAgentFailureKind.Cancelled,
                                            CancelledBySibling(),
                                            cancelledByCaller: true);
                                    }

                                    outcomes[index] = result;
                                }
                                finally
                                {
                                    gate.Release();
                                }
                            }
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            outcomes[index] = StepOutcome.Fail(
                                SubAgentFailureKind.Cancelled,
                                CancelledBySibling(),
                                cancelledByCaller: true);
                        }
                        finally
                        {
                            groupProgress.MarkCompleted();
                        }

                        if (outcomes[index] != null && !outcomes[index].Success && !step.ContinueOnBranchError)
                            StopGroup(index);
                    }, CancellationToken.None));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var firstFailure = -1;
            var firstCancelled = -1;
            var succeeded = 0;

            for (int b = 0; b < count; b++)
            {
                if (outcomes[b] == null)
                    outcomes[b] = StepOutcome.Fail(SubAgentFailureKind.Unexpected, "branch produced no result");

                if (outcomes[b].Success)
                {
                    succeeded++;
                    continue;
                }

                if (outcomes[b].CancelledByCaller)
                {
                    if (firstCancelled < 0)
                        firstCancelled = b;

                    InternalLogger.Info(
                        "SubAgentPipelineRunner: pipeline '" + context.PipelineId + "' step " + stepLabel
                        + " branch " + (b + 1) + "/" + count + " (" + BranchLabel(branches, b) + "): "
                        + outcomes[b].Error);
                    continue;
                }

                if (firstFailure < 0)
                    firstFailure = b;
            }

            var stopIndex = Volatile.Read(ref stopCause);
            var failure = -1;

            if (stopIndex >= 0 && stopIndex < count && outcomes[stopIndex] != null
                && !outcomes[stopIndex].Success && !outcomes[stopIndex].CancelledByCaller)
            {
                failure = stopIndex;
            }
            else if (firstFailure >= 0)
            {
                failure = firstFailure;
            }
            else if (firstCancelled >= 0)
            {
                failure = firstCancelled;
            }

            if (failure >= 0 && (!step.ContinueOnBranchError || succeeded == 0))
            {
                var failed = outcomes[failure];
                var position = "pipeline '" + context.PipelineId + "' step " + stepLabel
                    + " branch " + (failure + 1) + "/" + count
                    + " (" + BranchLabel(branches, failure) + ")";

                return StepOutcome.Fail(failed.FailureKind, position + ": " + failed.Error, true);
            }

            var builder = new StringBuilder();
            var branchOutputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int b = 0; b < count; b++)
            {
                var branch = branches[b];
                var outcome = outcomes[b];

                if (b > 0)
                    builder.AppendLine();

                var heading = SubAgentPipelinePrompt.BuildHeading(b + 1, count, Trim(branch.Label) ?? "branch");

                if (outcome.Success)
                {
                    builder.AppendLine(heading);
                    builder.Append((outcome.Content ?? string.Empty).TrimEnd());
                }
                else
                {
                    builder.AppendLine(heading + " — ERROR: " + outcome.Error);
                }

                if (!string.IsNullOrWhiteSpace(branch.BranchId))
                    branchOutputs[branch.BranchId.Trim()] = outcome.Success ? outcome.Content : "ERROR: " + outcome.Error;
            }

            return StepOutcome.Ok(builder.ToString().TrimEnd(), branchOutputs);
        }

        /// <summary>
        /// Runs one step or one branch, repeating it after a pause on a transient provider failure while attempts are left.
        /// </summary>
        private async Task<StepOutcome> RunTargetAsync(
            SubAgentPipelineRunRequest request,
            RunTarget target,
            int stepNumber,
            string stepLabel,
            string prev,
            IReadOnlyDictionary<string, string> outputs,
            SubAgentStepProgress progress,
            RunContext context,
            SubAgentRunCancellationOwner owner,
            CancellationToken cancellationToken)
        {
            var retries = target.Retries ?? 0;
            if (retries < 0)
                retries = 0;
            if (retries > SubAgentPipelineStep.MaxRetries)
                retries = SubAgentPipelineStep.MaxRetries;

            var maxAttempts = retries + 1;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var prompt = SubAgentPipelinePrompt.Render(target.PromptTemplate, request.Task, prev, outputs);

                var runRequest = target.Request.Copy();
                runRequest.Prompt = prompt;
                runRequest.Progress = progress;
                runRequest.PipelineId = context.PipelineId;
                runRequest.PipelineRunId = context.PipelineRunId;
                runRequest.PipelineStep = stepNumber;
                runRequest.PipelineStepId = target.StepId;
                runRequest.PipelineBranch = target.Branch;
                runRequest.PipelineAttempt = attempt > 1 ? (int?)attempt : null;
                runRequest.Cancellation = owner;

                if (string.IsNullOrWhiteSpace(runRequest.ParentSessionId))
                    runRequest.ParentSessionId = request.ParentSessionId;
                if (string.IsNullOrWhiteSpace(runRequest.RequestId))
                    runRequest.RequestId = request.RequestId;
                if (string.IsNullOrWhiteSpace(runRequest.Origin))
                    runRequest.Origin = request.Origin;

                progress.ReportStart(attempt > 1 ? "retry " + attempt + "/" + maxAttempts : null);

                var attemptResponse = await _subAgentsService.ExecutePromptAsync(runRequest, cancellationToken).ConfigureAwait(false);

                context.AddMetrics(attemptResponse, stepLabel);

                var succeeded = attemptResponse != null
                    && attemptResponse.Success
                    && !string.IsNullOrWhiteSpace(attemptResponse.Content);

                if (succeeded)
                {
                    InternalLogger.Info(
                        "SubAgentPipelineRunner: pipeline '" + context.PipelineId + "' step " + stepLabel
                        + " '" + target.Label + "' run " + attemptResponse.RunId + ": ok, " + attemptResponse.DurationMs + " ms, "
                        + DescribeTokens(attemptResponse) + (attempt > 1 ? ", attempt " + attempt + "/" + maxAttempts : string.Empty));
                    return StepOutcome.Ok(attemptResponse.Content);
                }

                var kind = attemptResponse == null ? SubAgentFailureKind.Unexpected : attemptResponse.FailureKind;
                var error = attemptResponse == null || string.IsNullOrWhiteSpace(attemptResponse.Error)
                    ? "empty answer"
                    : attemptResponse.Error;

                if (kind == SubAgentFailureKind.None)
                    kind = SubAgentFailureKind.Agent;

                var retry = attempt < maxAttempts && kind == SubAgentFailureKind.Provider;
                var pause = retry ? TimeSpan.FromSeconds(RetryDelaySeconds[Math.Min(attempt, RetryDelaySeconds.Length) - 1]) : TimeSpan.Zero;

                InternalLogger.Warn(
                    "SubAgentPipelineRunner: pipeline '" + context.PipelineId + "' step " + stepLabel
                    + " '" + target.Label + "' attempt " + attempt + "/" + maxAttempts + ": " + kind + ": " + error
                    + (retry ? ", waiting " + pause.TotalSeconds + "s" : string.Empty));

                if (!retry)
                    return StepOutcome.Fail(kind, error);

                await _delay(pause, cancellationToken).ConfigureAwait(false);
            }

            return StepOutcome.Fail(SubAgentFailureKind.Unexpected, "no attempt was made");
        }

        /// <summary>
        /// Final text of the pipeline: the last answer, or every answer with its heading.
        /// </summary>
        private static string BuildFinalContent(SubAgentPipelineRunRequest request, IReadOnlyList<AnsweredStep> answers)
        {
            if (answers == null || answers.Count == 0)
                return null;

            var all = string.Equals(Trim(request.Output), SubAgentPipelineDefinition.OutputAll, StringComparison.OrdinalIgnoreCase);
            if (!all)
                return answers[answers.Count - 1].Content;

            var builder = new StringBuilder();
            foreach (var answer in answers)
            {
                if (builder.Length > 0)
                    builder.AppendLine().AppendLine();

                builder.AppendLine(SubAgentPipelinePrompt.BuildHeading(answer.Number, answer.Total, answer.Label));
                builder.Append(answer.Content ?? string.Empty);
            }

            return builder.ToString().TrimEnd();
        }

        private static RunTarget ToTarget(SubAgentPipelineStepRun step)
        {
            return new RunTarget
            {
                Label = Trim(step.Label) ?? "step",
                StepId = Trim(step.StepId),
                PromptTemplate = step.PromptTemplate,
                Request = step.Request,
                Retries = step.Retries,
                AgentDisplayName = step.AgentDisplayName,
                Branch = null
            };
        }

        private static RunTarget ToTarget(SubAgentPipelineBranchRun branch, int branchNumber)
        {
            return new RunTarget
            {
                Label = Trim(branch.Label) ?? "branch",
                StepId = Trim(branch.BranchId),
                PromptTemplate = branch.PromptTemplate,
                Request = branch.Request,
                Retries = branch.Retries,
                AgentDisplayName = branch.AgentDisplayName,
                Branch = branchNumber > 0 ? (int?)branchNumber : null
            };
        }

        /// <summary>Label of a branch for a message; null-safe because a broken config may hold an empty branch.</summary>
        private static string BranchLabel(IReadOnlyList<SubAgentPipelineBranchRun> branches, int index)
        {
            if (branches == null || index < 0 || index >= branches.Count || branches[index] == null)
                return "branch";

            return Trim(branches[index].Label) ?? "branch";
        }


        private static int ResolveWidth(int? configured)
        {
            var width = configured.HasValue && configured.Value >= 1
                ? configured.Value
                : SubAgentsConfig.DefaultMaxParallel;

            return Math.Min(width, SubAgentsConfig.MaxParallelLimit);
        }

        private static string DescribeTokens(SubAgentsRunResponse response)
        {
            if (response == null || !response.TotalTokens.HasValue)
                return "no tokens reported";

            return response.TotalTokens.Value + " tokens";
        }

        private static SubAgentsRunResponse Fail(RunContext context, string error, SubAgentFailureKind kind)
        {
            var response = context.Aggregate;
            response.Success = false;
            response.Error = error;
            response.FailureKind = kind;
            context.Complete();

            InternalLogger.Warn(
                $"SubAgentPipelineRunner: pipeline '{context.PipelineId}' run {context.PipelineRunId}: failed: {error}");

            return response;
        }

        private static int? Add(int? left, int? right)
        {
            if (!left.HasValue && !right.HasValue)
                return null;

            return (left ?? 0) + (right ?? 0);
        }

        private static string Trim(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Aggregate response of the run and the accumulated metrics of its steps.
        /// </summary>
        private sealed class RunContext
        {
            private readonly object _sync = new object();
            private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
            private long _generatedTokens;
            private long _generationMs;

            internal RunContext(string pipelineId, string pipelineRunId)
            {
                PipelineId = pipelineId ?? string.Empty;
                PipelineRunId = pipelineRunId ?? string.Empty;
                Aggregate = new SubAgentsRunResponse { RunId = pipelineRunId };
            }

            internal string PipelineId { get; private set; }

            internal string PipelineRunId { get; private set; }

            internal SubAgentsRunResponse Aggregate { get; private set; }

            /// <summary>
            /// Writes the values that cannot be summed: the duration is the wall clock of the whole run (a sum would
            /// count parallel branches and failed attempts twice) and the speed is the total tokens over the total
            /// generation time (a sum of rates would add them up).
            /// </summary>
            internal void Complete()
            {
                lock (_sync)
                {
                    Aggregate.DurationMs = _stopwatch.ElapsedMilliseconds;
                    Aggregate.TokensPerSecond = _generationMs > 0
                        ? _generatedTokens * 1000d / _generationMs
                        : 0d;
                }
            }

            /// <summary>
            /// Adds the metrics of one step (or attempt) to the aggregate. Called from concurrent branches.
            /// </summary>
            internal void AddMetrics(SubAgentsRunResponse source, string stepLabel)
            {
                if (source == null)
                    return;

                lock (_sync)
                {
                    var target = Aggregate;

                    target.PromptTokens = Add(target.PromptTokens, source.PromptTokens);
                    target.CompletionTokens = Add(target.CompletionTokens, source.CompletionTokens);
                    target.TotalTokens = Add(target.TotalTokens, source.TotalTokens);
                    target.Rounds += source.Rounds;
                    target.ExpandedRefCount += source.ExpandedRefCount;
                    target.UnresolvedRefCount += source.UnresolvedRefCount;

                    if (source.CompletionTokens.HasValue && source.CompletionTokens.Value > 0 && source.TokensPerSecond > 0)
                    {
                        _generatedTokens += source.CompletionTokens.Value;
                        _generationMs += (long)Math.Round(source.CompletionTokens.Value * 1000d / source.TokensPerSecond);
                    }

                    if (source.ToolsUsed != null)
                    {
                        foreach (var tool in source.ToolsUsed)
                        {
                            if (string.IsNullOrWhiteSpace(tool))
                                continue;

                            if (target.ToolsUsed == null)
                                target.ToolsUsed = new List<string>();

                            if (!target.ToolsUsed.Any(t => string.Equals(t, tool, StringComparison.OrdinalIgnoreCase)))
                                target.ToolsUsed.Add(tool);
                        }
                    }

                    if (source.Recoveries != null)
                    {
                        foreach (var note in source.Recoveries)
                        {
                            if (string.IsNullOrWhiteSpace(note))
                                continue;

                            if (target.Recoveries == null)
                                target.Recoveries = new List<string>();

                            target.Recoveries.Add(stepLabel + ": " + note);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(source.Model))
                        target.Model = source.Model;

                    if (!string.IsNullOrWhiteSpace(source.RunId))
                        target.RunId = source.RunId;
                }
            }
        }

        /// <summary>
        /// One step (or branch) reduced to what the runner needs: the template and the prepared request.
        /// </summary>
        private sealed class RunTarget
        {
            internal string Label { get; set; }

            internal string StepId { get; set; }

            internal string PromptTemplate { get; set; }

            internal SubAgentRunRequest Request { get; set; }

            internal int? Retries { get; set; }

            internal string AgentDisplayName { get; set; }

            /// <summary>1-based number of the branch inside a parallel step; null for a sequential step.</summary>
            internal int? Branch { get; set; }
        }

        /// <summary>
        /// Result of one step or branch.
        /// </summary>
        private sealed class StepOutcome
        {
            internal bool Success { get; private set; }

            internal string Content { get; private set; }

            internal string Error { get; private set; }

            internal SubAgentFailureKind FailureKind { get; private set; }

            /// <summary>True when the error text already carries the step and branch position.</summary>
            internal bool HasPosition { get; private set; }

            /// <summary>
            /// True when the branch was cancelled by the runner itself (a sibling failed), not by the user and not by
            /// its own agent.
            /// </summary>
            internal bool CancelledByCaller { get; private set; }

            /// <summary>Named outputs the step publishes for <c>{{steps.&lt;id&gt;}}</c>.</summary>
            internal Dictionary<string, string> Outputs { get; private set; }

            internal static StepOutcome Ok(string content, Dictionary<string, string> outputs = null)
            {
                return new StepOutcome
                {
                    Success = true,
                    Content = content,
                    FailureKind = SubAgentFailureKind.None,
                    Outputs = outputs
                };
            }

            internal static StepOutcome Fail(
                SubAgentFailureKind kind,
                string error,
                bool hasPosition = false,
                bool cancelledByCaller = false)
            {
                return new StepOutcome
                {
                    Success = false,
                    Error = error,
                    FailureKind = kind,
                    HasPosition = hasPosition,
                    CancelledByCaller = cancelledByCaller
                };
            }
        }

        /// <summary>
        /// Answer of a finished step, used by the "all" output mode.
        /// </summary>
        private sealed class AnsweredStep
        {
            internal int Number { get; set; }

            internal int Total { get; set; }

            internal string Label { get; set; }

            internal string Content { get; set; }
        }
    }
}
