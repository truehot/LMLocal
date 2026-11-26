using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.SubAgents.ReadRefs;
using LMLocal.Application.Tool;
using LMLocal.Application.Usage;
using LMLocal.Core.Common;
using LMLocal.Core.Exceptions;
using LMLocal.Core.Models;
using LMLocal.Core.Models.Usage;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.Persistence;

namespace LMLocal.Application.SubAgents
{
    /// <summary>
    /// Runs isolated "SubAgent" prompts with nested agent loop: request -> tool_calls -> execute (allowed tools only) -> repeat, up to maxRounds.
    /// </summary>
    internal class SubAgentsService : ISubAgentsService
    {
        private const string LogsFolderName = "SubAgentsLogs";
        private const int DefaultMaxRounds = 10;
        internal const int DefaultTimeoutSeconds = 120;

        /// <summary>
        /// Time kept back for the final answer round.
        /// </summary>
        internal const int BudgetReserveSeconds = 25;

        // Sent in the final-answer round after the round limit: an empty list, so the request carries no tools at all.
        private static readonly IReadOnlyList<ToolDefinition> NoTools = new ToolDefinition[0];

        /// <summary>
        /// Enables loop recovery: on an identical repeated tool call the run closes the turn and continues as a new user turn on the same history instead of failing.
        /// </summary>
        internal const bool DefaultLoopRecoveryEnabled = true;

        private readonly ISettingsManager _settingsManager;
        private readonly IFileSystem _fileSystem;
        private readonly IStreamingRoundService _roundService;
        private readonly Func<IToolExecutionManager> _toolExecutionManagerResolver;
        private readonly IToolQueueProvider _queueProvider;
        private readonly IChatPersistenceFactory _chatPersistenceFactory;
        private readonly IProviderPolicy _providerPolicy;
        private readonly IUsageRecorder _usageRecorder;
        private readonly ITimeProvider _timeProvider;
        private readonly string _logsFolder;
        private readonly bool _loopRecoveryEnabled;

        public SubAgentsService(
            ISettingsManager settingsManager,
            IFileSystem fileSystem,
            IStreamingRoundService roundService,
            Func<IToolExecutionManager> toolExecutionManagerResolver,
            IToolQueueProvider queueProvider,
            IChatPersistenceFactory chatPersistenceFactory,
            IProviderPolicy providerPolicy,
            bool loopRecoveryEnabled = DefaultLoopRecoveryEnabled,
            IUsageRecorder usageRecorder = null,
            ITimeProvider timeProvider = null)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _roundService = roundService ?? throw new ArgumentNullException(nameof(roundService));
            _toolExecutionManagerResolver = toolExecutionManagerResolver ?? throw new ArgumentNullException(nameof(toolExecutionManagerResolver));
            _queueProvider = queueProvider ?? throw new ArgumentNullException(nameof(queueProvider));
            _chatPersistenceFactory = chatPersistenceFactory ?? throw new ArgumentNullException(nameof(chatPersistenceFactory));
            _providerPolicy = providerPolicy ?? throw new ArgumentNullException(nameof(providerPolicy));
            _loopRecoveryEnabled = loopRecoveryEnabled;
            _usageRecorder = usageRecorder;
            _timeProvider = timeProvider;

            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                _settingsManager.LocalAppDataFolder ?? "LMLocalChat");

            _logsFolder = Path.Combine(appDataDir, LogsFolderName);

            try
            {
                _fileSystem.CreateDirectory(_logsFolder);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"SubAgentsService: failed to create logs folder '{_logsFolder}'", ex);
            }
        }

        public async Task<SubAgentsRunResponse> ExecutePromptAsync(
            SubAgentRunRequest request,
            CancellationToken cancellationToken)
        {
            var runId = Guid.NewGuid().ToString("N");
            var stopwatch = Stopwatch.StartNew();

            var response = new SubAgentsRunResponse { RunId = runId };

            var tokenReporter = new SubAgentTokenReporter(request == null ? null : request.Progress, _timeProvider);

            int? lastRound = null;
            Guid? chatSessionId = null;

            var usageStartedUtc = DateTime.UtcNow;
            var usage = new UsageAccumulator();
            var usageOutcome = UsageOutcome.Ok;
            string usageError = null;

            var timeoutSeconds = EffectiveTimeoutSeconds(request);

            SubAgentsRunResponse FailRun(
                SubAgentsRunResponse failed,
                SubAgentRunRequest failedRequest,
                string error,
                int? round,
                UsageOutcome outcome = UsageOutcome.Error,
                SubAgentFailureKind kind = SubAgentFailureKind.Unexpected)
            {
                usageOutcome = outcome;
                usageError = error;
                failed.FailureKind = kind;

                InternalLogger.Warn(
                    $"SubAgentsService: run '{failed.RunId}' of '{failedRequest?.AgentName}' failed"
                    + (round.HasValue ? $" at round {round.Value + 1}" : string.Empty)
                    + (chatSessionId.HasValue ? $" (session {chatSessionId.Value})" : string.Empty)
                    + $": {error}");

                return Fail(failed, error);
            }

            SubAgentsRunResponse StopRun(SubAgentsRunResponse stopped, SubAgentRunRequest stoppedRequest, int? round, int? steps)
            {
                return IsStoppedByCaller(stoppedRequest)
                    ? FailRun(stopped, stoppedRequest, DescribeCallerCancel(stoppedRequest, round), round,
                        UsageOutcome.Cancelled, SubAgentFailureKind.Cancelled)
                    : FailRun(stopped, stoppedRequest, DescribeCancellation(stoppedRequest, round, steps), round,
                        UsageOutcome.Cancelled, SubAgentFailureKind.Cancelled);
            }

            try
            {
                if (request == null)
                {
                    return FailRun(
                        response, null, "SubAgent run request is required.", null,
                        UsageOutcome.Error, SubAgentFailureKind.InvalidRequest);
                }

                if (string.IsNullOrWhiteSpace(request.Prompt))
                {
                    return FailRun(
                        response, request, "Prompt is required.", null,
                        UsageOutcome.Error, SubAgentFailureKind.InvalidRequest);
                }

                var modelId = request.Model?.Trim();
                if (string.IsNullOrWhiteSpace(modelId))
                {
                    return FailRun(
                        response, request, "Model is required.", null,
                        UsageOutcome.Error, SubAgentFailureKind.InvalidRequest);
                }

                response.Model = modelId;

                var maxRounds = request.MaxRounds ?? DefaultMaxRounds;
                var systemPrompt = !string.IsNullOrWhiteSpace(request.System)
                    ? request.System
                    : _settingsManager.SystemPrompt;

                var toolQueue = _queueProvider.GetSubAgentQueue(request);
                var tools = toolQueue.Definitions;
                var provider = new ProviderContext
                {
                    ProviderType = request.ProviderType,
                    BaseUrl = request.BaseUrl,
                    ApiKey = request.ApiKey
                };

                var persistence = _chatPersistenceFactory.Create(_logsFolder);
                var history = new ChatHistoryManager(_settingsManager, persistence, _providerPolicy);
                await persistence.MarkNewSessionAsync(cancellationToken).ConfigureAwait(false);
                chatSessionId = persistence.CurrentSessionId;

                List<ToolResultMessage> toolResultsForRound = null;

                bool isToolRound = false;
                bool finalAnswerRound = false;
                string promptForRound = request.Prompt;
                string finalRoundNote = null;

                var recovery = new SubAgentRecoveryPolicy(_loopRecoveryEnabled, maxRounds);
                var evidence = new SubAgentRunEvidence();
                var toolExecutor = new SubAgentToolRoundExecutor(_toolExecutionManagerResolver, toolQueue, request.Progress, evidence);

                TimeSpan? AvailableForTools()
                {
                    if (timeoutSeconds <= 0)
                        return null;

                    return TimeSpan.FromSeconds(timeoutSeconds) - stopwatch.Elapsed - TimeSpan.FromSeconds(BudgetReserveSeconds);
                }

                using (var overallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    if (timeoutSeconds > 0)
                    {
                        overallCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                    }

                    for (int round = 0; round < maxRounds || (finalAnswerRound && round == maxRounds); round++)
                    {
                        lastRound = round;
                        overallCts.Token.ThrowIfCancellationRequested();

                        var roundRequest = new StreamRoundRequest
                        {
                            History = history,
                            IsToolRound = isToolRound,
                            ToolResults = isToolRound ? toolResultsForRound : null,
                            Prompt = isToolRound ? null : promptForRound,
                            RoundNote = isToolRound ? finalRoundNote : null,
                            SystemPrompt = systemPrompt,
                            ModelId = modelId,
                            Temperature = request.Temperature,
                            ReasoningEffort = request.ReasoningEffort,
                            MaxOutputTokens = request.MaxTokens,
                            Provider = provider,
                            Tools = finalAnswerRound ? NoTools : tools,
                            OnChunk = tokenReporter.IsEnabled ? ReportTokens(tokenReporter) : null
                        };

                        promptForRound = null;
                        isToolRound = true;
                        finalRoundNote = null;

                        StreamRoundResult roundResult = null;
                        try
                        {
                            roundResult = await _roundService.RunAsync(roundRequest, overallCts.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            tokenReporter.EndRound(roundResult?.Stream?.TokenUsage?.CompletionTokens);
                        }

                        var streamResult = roundResult.Stream;

                        usage.Add(streamResult);

                        if (cancellationToken.IsCancellationRequested)
                        {
                            return StopRun(response, request, round, toolExecutor.Steps);
                        }

                        if (overallCts.Token.IsCancellationRequested)
                        {
                            return FailRun(
                                response, request, DescribeTimeout(request, round), round,
                                UsageOutcome.Timeout, SubAgentFailureKind.Timeout);
                        }

                        switch (roundResult.Outcome)
                        {
                            case StreamRoundOutcome.Error:
                                return FailRun(
                                    response, request, "Provider stream error: " + streamResult.ErrorMessage, round,
                                    UsageOutcome.Error, ClassifyProviderError(streamResult));

                            case StreamRoundOutcome.Cancelled:
                                return FailRun(
                                    response, request, DescribeUnexpectedCancel(request, round), round,
                                    UsageOutcome.Error, SubAgentFailureKind.Provider);

                            case StreamRoundOutcome.Incomplete:
                                return FailRun(
                                    response, request, streamResult.ErrorMessage, round,
                                    UsageOutcome.Incomplete, SubAgentFailureKind.Incomplete);

                            case StreamRoundOutcome.ToolCalls:
                                if (finalAnswerRound && roundRequest.IsToolRound)
                                {
                                    return FailRun(
                                        response,
                                        request,
                                        "SubAgent called tools in the final answer round (agent budget exhausted); run stopped.",
                                        round, UsageOutcome.Error, SubAgentFailureKind.Agent);
                                }

                                var decision = recovery.Evaluate(streamResult, round);
                                if (decision.Action == RecoveryAction.Fail)
                                {
                                    return FailRun(response, request, decision.Error, round,
                                        UsageOutcome.Error, SubAgentFailureKind.Agent);
                                }

                                if (decision.Action == RecoveryAction.Recover)
                                {
                                    CloseTurnForRecovery(history);
                                    AddRecovery(response, request, round, decision.Note);

                                    toolResultsForRound = null;
                                    isToolRound = false;
                                    finalAnswerRound |= decision.DisableTools;
                                    promptForRound = decision.Nudge;
                                    continue;
                                }

                                toolResultsForRound = await toolExecutor
                                    .ExecuteAsync(streamResult.ToolCalls, overallCts.Token, AvailableForTools)
                                    .ConfigureAwait(false);

                                var remainingForTools = AvailableForTools();
                                if (!finalAnswerRound && remainingForTools.HasValue && remainingForTools.Value <= TimeSpan.Zero)
                                {
                                    finalAnswerRound = true;
                                    finalRoundNote = SubAgentRecoveryPolicy.BudgetExhaustedNudge;
                                    AddRecovery(
                                        response,
                                        request,
                                        round,
                                        "SubAgent budget is nearly exhausted and answered from the results gathered so far.");
                                }
                                continue;

                            case StreamRoundOutcome.Completed:
                                var expanded = await evidence.ExpandAsync(streamResult.ContentResponse, _fileSystem, overallCts.Token).ConfigureAwait(false);

                                var answerDecision = recovery.EvaluateAnswer(expanded?.UnresolvedRefs, round);
                                if (answerDecision.Action == RecoveryAction.Recover)
                                {
                                    AddRecovery(response, request, round, answerDecision.Note);

                                    toolResultsForRound = null;
                                    isToolRound = false;
                                    promptForRound = answerDecision.Nudge;
                                    continue;
                                }

                                response.Content = streamResult.ContentResponse;
                                response.Success = true;
                                response.Rounds = round + 1;
                                response.ToolsUsed = new List<string>(toolExecutor.UsedTools);

                                if (expanded != null)
                                {
                                    response.Content = expanded.Content;
                                    response.ExpandedRefCount = expanded.ExpandedRefCount;
                                    response.UnresolvedRefCount = expanded.UnresolvedRefCount;
                                }

                                return response;
                        }
                    }

                    return FailRun(
                        response, request, $"SubAgent exceeded maximum rounds ({maxRounds}). {SubAgentRecoveryPolicy.LimitRetryHint}", lastRound,
                        UsageOutcome.Error, SubAgentFailureKind.Agent);
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested || IsStoppedByCaller(request))
                {
                    return StopRun(response, request, lastRound, null);
                }

                return FailRun(
                    response, request, DescribeTimeout(request, lastRound), lastRound,
                    UsageOutcome.Timeout, SubAgentFailureKind.Timeout);
            }
            catch (ApiException ex)
            {
                InternalLogger.Warn("SubAgentsService: provider API error", ex);
                return FailRun(
                    response, request, "Provider API error: " + ExceptionFormatter.Format(ex), lastRound,
                    UsageOutcome.Error, ClassifyProviderError(StreamingRoundService.BuildErrorResult(ex)));
            }
            catch (HttpRequestException ex)
            {
                InternalLogger.Warn("SubAgentsService: provider request failed", ex);
                return FailRun(
                    response, request, ExceptionFormatter.Format(ex), lastRound,
                    UsageOutcome.Error, SubAgentFailureKind.Provider);
            }
            catch (InvalidOperationException ex)
            {
                return FailRun(
                    response, request, ExceptionFormatter.Format(ex), lastRound,
                    UsageOutcome.Error, SubAgentFailureKind.Unexpected);
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"SubAgentsService: unexpected error during run '{runId}'", ex);
                return FailRun(
                    response, request, ExceptionFormatter.Format(ex), lastRound,
                    UsageOutcome.Error, SubAgentFailureKind.Unexpected);
            }
            finally
            {
                stopwatch.Stop();
                response.DurationMs = stopwatch.ElapsedMilliseconds;

                ApplyUsage(usage, response);

                await RecordUsageAsync(request, response, usage, usageOutcome, usageError, chatSessionId, usageStartedUtc)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>Forwards the token stats of a round to the call's progress sink for the status bar.</summary>
        private static Func<TextStreamChunk, TokenGenerationStats, Task> ReportTokens(SubAgentTokenReporter reporter)
        {
            return (chunk, stats) =>
            {
                reporter.Report(stats);
                return Task.CompletedTask;
            };
        }

        /// <summary>
        /// Copies the usage of every round of the run into the response the caller shows.
        /// </summary>
        private static void ApplyUsage(UsageAccumulator usage, SubAgentsRunResponse response)
        {
            if (usage == null || response == null)
                return;

            response.PromptTokens = usage.Prompt;
            response.CompletionTokens = usage.Completion;
            response.TotalTokens = usage.Prompt.HasValue || usage.Completion.HasValue
                ? (int?)((usage.Prompt ?? 0) + (usage.Completion ?? 0))
                : null;
            response.TokensPerSecond = usage.GenMs > 0 ? usage.GenCompletion * 1000d / usage.GenMs : 0d;
        }

        /// <summary>
        /// Writes the usage of the run, whatever its outcome: a run that failed after five rounds already spent the
        /// tokens of those rounds. Never throws, so statistics cannot break a run.
        /// </summary>
        private async Task RecordUsageAsync(
            SubAgentRunRequest request,
            SubAgentsRunResponse response,
            UsageAccumulator usage,
            UsageOutcome outcome,
            string error,
            Guid? chatSessionId,
            DateTime startedUtc)
        {
            if (_usageRecorder == null || request == null)
                return;

            try
            {
                var record = new UsageRecord
                {
                    Timestamp = startedUtc,
                    SessionId = request.ParentSessionId,
                    RequestId = request.RequestId,
                    Source = UsageSource.Subagent,
                    Origin = string.IsNullOrWhiteSpace(request.Origin) ? UsageOrigins.Chat : request.Origin,
                    Agent = request.AgentName,
                    Pipeline = request.PipelineId,
                    PipelineRunId = request.PipelineRunId,
                    PipelineStep = request.PipelineStep,
                    PipelineStepId = request.PipelineStepId,
                    PipelineAttempt = request.PipelineAttempt,
                    PipelineBranch = request.PipelineBranch,
                    RunSessionId = UsageIds.SessionId(chatSessionId),
                    Provider = request.ProviderType,
                    BaseUrl = request.BaseUrl,
                    Model = !string.IsNullOrWhiteSpace(response.Model) ? response.Model : request.Model,
                    DurationMs = response.DurationMs,
                    Outcome = outcome,
                    Error = outcome == UsageOutcome.Ok || outcome == UsageOutcome.Cancelled
                        ? null
                        : UsageOutcomeMapper.FirstLine(error)
                };

                usage.ApplyTo(record);

                await _usageRecorder.AppendAsync(record, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"SubAgentsService: failed to record the usage of run '{response?.RunId}'", ex);
            }
        }

        /// <summary>
        /// Adds the recovery note to the response and the internal log.
        /// </summary>
        private static void AddRecovery(SubAgentsRunResponse response, SubAgentRunRequest request, int round, string note)
        {
            if (response.Recoveries == null)
                response.Recoveries = new List<string>();
            response.Recoveries.Add(note);
            InternalLogger.Info($"SubAgentsService: run '{response.RunId}' of '{request.AgentName}' at round {round + 1}: {note}");
        }

        /// <summary>
        /// Closes the current tool turn on the SAME history, so everything already collected stays in context.
        /// </summary>
        private void CloseTurnForRecovery(IChatHistoryManager history)
        {
            history.DiscardPendingAssistant();
            history.AddAssistantMessage(SubAgentRecoveryPolicy.DiscardedTurnPlaceholder, null);
        }

        // Keep the reason and the numbers inside the first ~100 chars: the UI truncates tool errors at 120.
        private static string DescribeCancellation(SubAgentRunRequest request, int? round, int? step)
        {
            const string text = "SubAgent stopped: the request was stopped by the user";
            if (!round.HasValue)
                return text + ".";

            var position = $"round {round.Value + 1}/{EffectiveMaxRounds(request)}";
            if (step.HasValue && step.Value > 0)
                position += $", step {step.Value}";
            return $"{text} ({position}).";
        }

        /// <summary>
        /// Tells a transient provider failure from one a repeated run cannot fix; a rate limit counts as the latter because the round already handled it.
        /// </summary>
        private static SubAgentFailureKind ClassifyProviderError(StreamCompletionResult result)
        {
            if (ContextOverflowDetector.IsContextOverflow(result) || RateLimitRetryPolicy.IsRateLimit(result))
                return SubAgentFailureKind.ProviderNotRetryable;

            var status = result?.HttpStatusCode;
            if (!status.HasValue || status.Value == 408 || status.Value >= 500)
                return SubAgentFailureKind.Provider;

            return SubAgentFailureKind.ProviderNotRetryable;
        }

        private static string DescribeUnexpectedCancel(SubAgentRunRequest request, int round)
            => $"SubAgent stream ended as cancelled without a stop request (round {round + 1}/{EffectiveMaxRounds(request)}).";

        private static string DescribeTimeout(SubAgentRunRequest request, int? round)
        {
            var text = $"SubAgent timed out after {EffectiveTimeoutSeconds(request)}s";
            text = round.HasValue
                ? text + $" (round {round.Value + 1}/{EffectiveMaxRounds(request)})."
                : text + ".";
            return text + " " + SubAgentRecoveryPolicy.LimitRetryHint;
        }

        /// <summary>
        /// Cancellation requested by an outer owner (a parallel pipeline group whose sibling branch failed), not by
        /// the user and not by the watchdog.
        /// </summary>
        private static string DescribeCallerCancel(SubAgentRunRequest request, int? round)
        {
            var text = "SubAgent stopped: cancelled by its caller, not by the user";
            return round.HasValue
                ? text + $" (round {round.Value + 1}/{EffectiveMaxRounds(request)})."
                : text + ".";
        }

        /// <summary>
        /// True when the owner of the token cancelled the run on purpose, so the reason is not a user stop.
        /// </summary>
        private static bool IsStoppedByCaller(SubAgentRunRequest request)
        {
            return request != null && request.Cancellation != null && request.Cancellation.StoppedByCaller;
        }

        private static int EffectiveTimeoutSeconds(SubAgentRunRequest request)
            => request?.TimeoutSeconds ?? DefaultTimeoutSeconds;

        private static int EffectiveMaxRounds(SubAgentRunRequest request)
            => request?.MaxRounds ?? DefaultMaxRounds;

        private static SubAgentsRunResponse Fail(SubAgentsRunResponse response, string error)
        {
            response.Success = false;
            response.Error = error;
            return response;
        }
    }
}
