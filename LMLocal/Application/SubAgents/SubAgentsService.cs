using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.SubAgents.ReadRefs;
using LMLocal.Application.Tool;
using LMLocal.Core.Common;
using LMLocal.Core.Exceptions;
using LMLocal.Core.Models;
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

        // Told to the main model when a run hits maxRounds or the timeout: the limit itself is not actionable, the next step is.
        private const string LimitRetryHint = "Split the task into smaller pieces or narrow it and retry.";

        /// <summary>
        /// Enables loop recovery: on an identical repeated tool call the run closes the turn and continues as a new user turn on the same history instead of failing.
        /// </summary>
        internal const bool DefaultLoopRecoveryEnabled = true;

        private readonly ISettingsManager _settingsManager;
        private readonly IFileSystem _fileSystem;
        private readonly IStreamingRoundService _roundService;
        private readonly Func<IToolExecutionManager> _toolExecutionManagerResolver;
        private readonly IToolQueueProvider _queueProvider;
        private readonly IToolCallLoopDetector _loopDetector;
        private readonly IChatPersistenceFactory _chatPersistenceFactory;
        private readonly string _logsFolder;
        private readonly bool _loopRecoveryEnabled;

        public SubAgentsService(
            ISettingsManager settingsManager,
            IFileSystem fileSystem,
            IStreamingRoundService roundService,
            Func<IToolExecutionManager> toolExecutionManagerResolver,
            IToolQueueProvider queueProvider,
            IToolCallLoopDetector loopDetector,
            IChatPersistenceFactory chatPersistenceFactory,
            bool loopRecoveryEnabled = DefaultLoopRecoveryEnabled)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _roundService = roundService ?? throw new ArgumentNullException(nameof(roundService));
            _toolExecutionManagerResolver = toolExecutionManagerResolver ?? throw new ArgumentNullException(nameof(toolExecutionManagerResolver));
            _queueProvider = queueProvider ?? throw new ArgumentNullException(nameof(queueProvider));
            _loopDetector = loopDetector ?? throw new ArgumentNullException(nameof(loopDetector));
            _chatPersistenceFactory = chatPersistenceFactory ?? throw new ArgumentNullException(nameof(chatPersistenceFactory));
            _loopRecoveryEnabled = loopRecoveryEnabled;

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
                InternalLogger.Warn($"SubAgentsService: failed to create logs folder '{_logsFolder}': {ex.Message}");
            }
        }

        public async Task<SubAgentsRunResponse> ExecutePromptAsync(
            SubAgentRunRequest request,
            CancellationToken cancellationToken)
        {
            var runId = Guid.NewGuid().ToString("N");
            var stopwatch = Stopwatch.StartNew();

            var response = new SubAgentsRunResponse { RunId = runId };

            int? lastRound = null;
            Guid? chatSessionId = null;

            SubAgentsRunResponse FailRun(SubAgentsRunResponse failed, SubAgentRunRequest failedRequest, string error, int? round)
            {
                InternalLogger.Warn(
                    $"SubAgentsService: run '{failed.RunId}' of '{failedRequest?.AgentName}' failed"
                    + (round.HasValue ? $" at round {round.Value + 1}" : string.Empty)
                    + (chatSessionId.HasValue ? $" (session {chatSessionId.Value})" : string.Empty)
                    + $": {error}");

                return Fail(failed, error);
            }

            try
            {
                if (request == null)
                {
                    return FailRun(
                        response, null, "SubAgent run request is required.", null);
                }

                if (string.IsNullOrWhiteSpace(request.Prompt))
                {
                    return FailRun(
                        response, request, "Prompt is required.", null);
                }

                var modelId = request.Model?.Trim();
                if (string.IsNullOrWhiteSpace(modelId))
                {
                    return FailRun(
                        response, request, "Model is required.", null);
                }

                response.Model = modelId;

                var maxRounds = request.MaxRounds ?? DefaultMaxRounds;
                var timeoutSeconds = request.TimeoutSeconds ?? DefaultTimeoutSeconds;
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
                var history = new ChatHistoryManager(_settingsManager, persistence);
                await persistence.MarkNewSessionAsync(cancellationToken).ConfigureAwait(false);
                chatSessionId = persistence.CurrentSessionId;

                List<ToolResultMessage> toolResultsForRound = null;

                bool isToolRound = false;
                string promptForRound = request.Prompt;

                var recovery = new SubAgentRecoveryPolicy(_loopDetector, _loopRecoveryEnabled, maxRounds);
                var evidence = new SubAgentRunEvidence();
                var toolExecutor = new SubAgentToolRoundExecutor(_toolExecutionManagerResolver, toolQueue, request.Progress, evidence);

                using (var overallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    if (timeoutSeconds > 0)
                    {
                        overallCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                    }

                    for (int round = 0; round < maxRounds; round++)
                    {
                        lastRound = round;
                        overallCts.Token.ThrowIfCancellationRequested();

                        var roundRequest = new StreamRoundRequest
                        {
                            History = history,
                            IsToolRound = isToolRound,
                            ToolResults = isToolRound ? toolResultsForRound : null,
                            Prompt = isToolRound ? null : promptForRound,
                            SystemPrompt = systemPrompt,
                            ModelId = modelId,
                            Temperature = request.Temperature,
                            ReasoningEffort = request.ReasoningEffort,
                            MaxOutputTokens = request.MaxTokens,
                            Provider = provider,
                            Tools = tools,
                            OnChunk = null
                        };

                        promptForRound = null;
                        isToolRound = true;

                        var roundResult = await _roundService.RunAsync(roundRequest, overallCts.Token).ConfigureAwait(false);
                        var streamResult = roundResult.Stream;

                        if (cancellationToken.IsCancellationRequested)
                        {
                            return FailRun(
                                response, request, DescribeCancellation(request, round, toolExecutor.Steps), round);
                        }

                        if (overallCts.Token.IsCancellationRequested)
                        {
                            return FailRun(
                                response, request, DescribeTimeout(request, round), round);
                        }

                        switch (roundResult.Outcome)
                        {
                            case StreamRoundOutcome.Error:
                                return FailRun(
                                    response, request, "Provider stream error: " + streamResult.ErrorMessage, round);

                            case StreamRoundOutcome.Cancelled:
                                return FailRun(
                                    response, request, DescribeUnexpectedCancel(request, round), round);

                            case StreamRoundOutcome.Incomplete:
                                return FailRun(
                                    response, request, streamResult.ErrorMessage, round);

                            case StreamRoundOutcome.ToolCalls:
                                var decision = recovery.Evaluate(streamResult, round);
                                if (decision.Action == RecoveryAction.Fail)
                                {
                                    return FailRun(response, request, decision.Error, round);
                                }

                                if (decision.Action == RecoveryAction.Recover)
                                {
                                    CloseTurnForRecovery(history);
                                    AddRecovery(response, request, round, decision.Note);

                                    toolResultsForRound = null;
                                    isToolRound = false;
                                    promptForRound = decision.Nudge;
                                    continue;
                                }

                                toolResultsForRound = await toolExecutor.ExecuteAsync(streamResult.ToolCalls, overallCts.Token).ConfigureAwait(false);
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
                                CopyUsage(streamResult, response);
                                response.TokensPerSecond = streamResult.TokensPerSecond;

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
                        response, request, $"SubAgent exceeded maximum rounds ({maxRounds}). {LimitRetryHint}", lastRound);
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return FailRun(
                        response, request, DescribeCancellation(request, lastRound, null), lastRound);
                }

                return FailRun(
                    response, request, DescribeTimeout(request, lastRound), lastRound);
            }
            catch (ApiException ex)
            {
                InternalLogger.Warn($"SubAgentsService: provider API error: {ex.Message}");
                return FailRun(
                    response, request, "Provider API error: " + ExceptionFormatter.Format(ex), lastRound);
            }
            catch (InvalidOperationException ex)
            {
                return FailRun(
                    response, request, ExceptionFormatter.Format(ex), lastRound);
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"SubAgentsService: unexpected error during run '{runId}'", ex);
                return FailRun(
                    response, request, ExceptionFormatter.Format(ex), lastRound);
            }
            finally
            {
                stopwatch.Stop();
                response.DurationMs = stopwatch.ElapsedMilliseconds;
            }
        }

        private static void CopyUsage(StreamCompletionResult stream, SubAgentsRunResponse response)
        {
            if (stream?.TokenUsage != null)
            {
                response.PromptTokens = stream.TokenUsage.PromptTokens;
                response.CompletionTokens = stream.TokenUsage.CompletionTokens;
                response.TotalTokens = stream.TokenUsage.TotalTokens;
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
            history.AddAssistantMessage(_settingsManager.AssistantPlaceholder, null);
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

        private static string DescribeUnexpectedCancel(SubAgentRunRequest request, int round)
            => $"SubAgent stream ended as cancelled without a stop request (round {round + 1}/{EffectiveMaxRounds(request)}).";

        private static string DescribeTimeout(SubAgentRunRequest request, int? round)
        {
            var text = $"SubAgent timed out after {EffectiveTimeoutSeconds(request)}s";
            text = round.HasValue
                ? text + $" (round {round.Value + 1}/{EffectiveMaxRounds(request)})."
                : text + ".";
            return text + " " + LimitRetryHint;
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
