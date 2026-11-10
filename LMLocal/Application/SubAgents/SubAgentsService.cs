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
using LMLocal.Infrastructure.SubAgents;

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
        private const int MaxDuplicateToolRounds = 2;

        /// <summary>
        /// Enables loop recovery: on an identical repeated tool call the run closes the turn and continues as a new user turn on the same history instead of failing.
        /// </summary>
        internal const bool DefaultLoopRecoveryEnabled = true;

        /// <summary>
        /// How many times a single run may recover from a repeated tool call before it fails.
        /// </summary>
        private const int LoopRecoveryMaxAttempts = 2;

        /// <summary>
        /// User-turn instruction injected after a repeated tool call.
        /// </summary>

        internal const string LoopRecoveryNudge = @"[AUTOMATED GUARD: DUPLICATE TOOL CALL BLOCKED]
Your last call repeated an earlier one; its result is above.
Answer the task now, or make one call with different arguments.
Answer directly.";



        /// <summary>
        /// How many tool-call attempts are carried into a failure record (see <see cref="SubAgentRecentCall"/>).
        /// </summary>
        private const int MaxRecentCalls = 3;

        private readonly ISettingsManager _settingsManager;
        private readonly IFileSystem _fileSystem;
        private readonly IStreamingRoundService _roundService;
        private readonly Func<IToolExecutionManager> _toolExecutionManagerResolver;
        private readonly IToolQueueProvider _queueProvider;
        private readonly IToolCallLoopDetector _loopDetector;
        private readonly IChatPersistenceFactory _chatPersistenceFactory;
        private readonly ISubAgentFailureLogger _failureLogger;
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
            ISubAgentFailureLogger failureLogger,
            bool loopRecoveryEnabled = DefaultLoopRecoveryEnabled)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _roundService = roundService ?? throw new ArgumentNullException(nameof(roundService));
            _toolExecutionManagerResolver = toolExecutionManagerResolver ?? throw new ArgumentNullException(nameof(toolExecutionManagerResolver));
            _queueProvider = queueProvider ?? throw new ArgumentNullException(nameof(queueProvider));
            _loopDetector = loopDetector ?? throw new ArgumentNullException(nameof(loopDetector));
            _chatPersistenceFactory = chatPersistenceFactory ?? throw new ArgumentNullException(nameof(chatPersistenceFactory));
            _failureLogger = failureLogger ?? throw new ArgumentNullException(nameof(failureLogger));
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

            var recentCalls = new List<SubAgentRecentCall>(MaxRecentCalls);

            SubAgentRecentCall TrackCall(ToolCallRecord call, int round, int? step)
            {
                var entry = new SubAgentRecentCall
                {
                    ToolName = call.FunctionName,
                    CallId = call.CallId,
                    Round = round,
                    Step = step,
                    ArgumentsJson = call.ArgumentsJson
                };

                recentCalls.Add(entry);
                if (recentCalls.Count > MaxRecentCalls)
                    recentCalls.RemoveAt(0);

                return entry;
            }

            try
            {
                if (request == null)
                {
                    return await FailLoggedAsync(
                        response, null, "SubAgent run request is required.",
                        null, null, recentCalls: recentCalls).ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(request.Prompt))
                {
                    return await FailLoggedAsync(
                        response, request, "Prompt is required.",
                        null, null, recentCalls: recentCalls).ConfigureAwait(false);
                }

                var modelId = request.Model?.Trim();
                if (string.IsNullOrWhiteSpace(modelId))
                {
                    return await FailLoggedAsync(
                        response, request, "Model is required.",
                        null, null, recentCalls: recentCalls).ConfigureAwait(false);
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

                var usedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                IReadOnlyList<ToolCallRecord> previousToolCalls = null;
                int consecutiveDuplicateRounds = 0;
                List<ToolResultMessage> toolResultsForRound = null;


                bool isToolRound = false;
                string promptForRound = request.Prompt;
                int reevaluationAttempts = 0;
                int stepCounter = 0;

                var ledger = new ReadLedger();
                var searchLedger = new SearchLedger();

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
                            return await FailLoggedAsync(
                                response, request, DescribeCancellation(request, round, stepCounter),
                                round, chatSessionId, recentCalls: recentCalls).ConfigureAwait(false);
                        }

                        if (overallCts.Token.IsCancellationRequested)
                        {
                            return await FailLoggedAsync(
                                response, request, DescribeTimeout(request, round),
                                round, chatSessionId, recentCalls: recentCalls).ConfigureAwait(false);
                        }

                        switch (roundResult.Outcome)
                        {
                            case StreamRoundOutcome.Error:
                                return await FailLoggedAsync(
                                    response, request, "Provider stream error: " + streamResult.ErrorMessage,
                                    round, chatSessionId, recentCalls: recentCalls).ConfigureAwait(false);

                            case StreamRoundOutcome.Cancelled:
                                return await FailLoggedAsync(
                                    response, request, DescribeUnexpectedCancel(request, round),
                                    round, chatSessionId, recentCalls: recentCalls).ConfigureAwait(false);

                            case StreamRoundOutcome.Incomplete:
                                return await FailLoggedAsync(
                                    response, request, streamResult.ErrorMessage,
                                    round, chatSessionId, recentCalls: recentCalls).ConfigureAwait(false);

                            case StreamRoundOutcome.ToolCalls:
                                if (previousToolCalls != null && _loopDetector.AreSameToolCalls(streamResult.ToolCalls, previousToolCalls))
                                {
                                    consecutiveDuplicateRounds++;
                                    if (consecutiveDuplicateRounds >= MaxDuplicateToolRounds)
                                    {
                                        if (CanRecoverFromLoop(reevaluationAttempts, round, maxRounds))
                                        {
                                            ApplyLoopRecovery(history);

                                            await _failureLogger
                                                .LogFromRequestAsync(
                                                    request,
                                                    "Recovered",
                                                    $"SubAgent recovered from a repeated tool call loop (attempt {reevaluationAttempts + 1} of {LoopRecoveryMaxAttempts}).",
                                                    response.RunId,
                                                    round,
                                                    chatSessionId,
                                                    cancellationToken: CancellationToken.None)
                                                .ConfigureAwait(false);

                                            reevaluationAttempts++;
                                            previousToolCalls = null;
                                            consecutiveDuplicateRounds = 0;
                                            toolResultsForRound = null;

                                            isToolRound = false;
                                            promptForRound = LoopRecoveryNudge;
                                            continue;
                                        }

                                        return await FailLoggedAsync(
                                            response, request, "SubAgent detected a repeated tool call loop and stopped.",
                                            round, chatSessionId, recentCalls: recentCalls).ConfigureAwait(false);
                                    }
                                }
                                else
                                {
                                    consecutiveDuplicateRounds = 0;
                                }
                                previousToolCalls = streamResult.ToolCalls;

                                var toolResults = new List<ToolResultMessage>(streamResult.ToolCalls.Count);
                                foreach (var toolCall in streamResult.ToolCalls)
                                {
                                    overallCts.Token.ThrowIfCancellationRequested();

                                    if (!toolQueue.Allows(toolCall.FunctionName))
                                    {
                                        TrackCall(toolCall, round, null).Error =
                                            $"Tool '{toolCall.FunctionName}' is not allowed in SubAgent context.";

                                        toolResults.Add(new ToolResultMessage
                                        {
                                            ToolCallId = toolCall.CallId,
                                            ToolName = toolCall.FunctionName,
                                            Result = $"Error: tool '{toolCall.FunctionName}' is not allowed in SubAgent context."
                                        });
                                        continue;
                                    }

                                    usedTools.Add(toolCall.FunctionName);

                                    request.Progress?.Report(new ToolActivityEvent
                                    {
                                        ToolName = toolCall.FunctionName,
                                        Step = ++stepCounter
                                    });

                                    var trackedCall = TrackCall(toolCall, round, stepCounter);

                                    var execResult = await _toolExecutionManagerResolver()
                                        .ExecuteToolAsync(toolCall, overallCts.Token, toolQueue)
                                        .ConfigureAwait(false);

                                    object resultValue = string.IsNullOrEmpty(execResult.Error) ? execResult.Result : execResult.Error;

                                    trackedCall.ResultText = SubAgentFailureLogger.StringifyResult(execResult.Result);
                                    trackedCall.Error = toolCall.IsInvalid && string.IsNullOrEmpty(execResult.Error)
                                        ? "Tool-call arguments were not valid JSON and were sanitized to '{}'."
                                        : execResult.Error;

                                    if (string.IsNullOrEmpty(execResult.Error)
                                        && string.Equals(toolCall.FunctionName, ReadResultRecorder.ReadFileLinesToolName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        ReadResultRecorder.TryRecord(ledger, execResult.Result);
                                    }
                                    else if (string.IsNullOrEmpty(execResult.Error)
                                        && string.Equals(toolCall.FunctionName, SearchResultRecorder.SearchFileContentToolName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        SearchResultRecorder.TryRecord(searchLedger, execResult.Result);
                                    }

                                    toolResults.Add(new ToolResultMessage
                                    {
                                        ToolCallId = toolCall.CallId,
                                        ToolName = toolCall.FunctionName,
                                        Result = resultValue,
                                        Error = execResult.Error
                                    });
                                }

                                toolResultsForRound = toolResults;
                                continue;

                            case StreamRoundOutcome.Completed:
                                response.Content = streamResult.ContentResponse;
                                response.Success = true;
                                response.Rounds = round + 1;
                                response.ToolsUsed = new List<string>(usedTools);
                                CopyUsage(streamResult, response);
                                response.TokensPerSecond = streamResult.TokensPerSecond;

                                if ((ledger.Count > 0 || searchLedger.Count > 0) && !string.IsNullOrEmpty(response.Content))
                                {
                                    var expander = new ReadRefExpander(ledger, _fileSystem, searchLedger);
                                    var expanded = await expander.ExpandAsync(response.Content, overallCts.Token).ConfigureAwait(false);
                                    response.Content = expanded.Content;
                                    response.ExpandedRefCount = expanded.ExpandedRefCount;
                                    response.UnresolvedRefCount = expanded.UnresolvedRefCount;
                                }

                                return response;
                        }
                    }

                    return await FailLoggedAsync(
                        response, request, $"SubAgent exceeded maximum rounds ({maxRounds}). {LimitRetryHint}",
                        lastRound, chatSessionId, recentCalls: recentCalls).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return await FailLoggedAsync(
                        response, request, DescribeCancellation(request, lastRound, null),
                        lastRound, chatSessionId, recentCalls: recentCalls).ConfigureAwait(false);
                }

                return await FailLoggedAsync(
                    response, request, DescribeTimeout(request, lastRound),
                    lastRound, chatSessionId, recentCalls: recentCalls).ConfigureAwait(false);
            }
            catch (ApiException ex)
            {
                InternalLogger.Warn($"SubAgentsService: provider API error: {ex.Message}");
                return await FailLoggedAsync(
                    response, request, "Provider API error: " + ExceptionFormatter.Format(ex),
                    lastRound, chatSessionId, ex, recentCalls).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                return await FailLoggedAsync(
                    response, request, ExceptionFormatter.Format(ex),
                    lastRound, chatSessionId, ex, recentCalls).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"SubAgentsService: unexpected error during run '{runId}'", ex);
                return await FailLoggedAsync(
                    response, request, ExceptionFormatter.Format(ex),
                    lastRound, chatSessionId, ex, recentCalls).ConfigureAwait(false);
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
        /// Decides whether a repeated tool call may be recovered from instead of failing the run.
        /// </summary>
        private bool CanRecoverFromLoop(int reevaluationAttempts, int round, int maxRounds)
            => _loopRecoveryEnabled
            && reevaluationAttempts < LoopRecoveryMaxAttempts
            && round + 1 < maxRounds;

        /// <summary>
        /// Closes the current tool turn on the SAME history, so everything already collected stays in context.
        /// </summary>
        private void ApplyLoopRecovery(IChatHistoryManager history)
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

        /// <summary>
        /// Marks the run as failed AND writes a replayable record, including the last tool-call attempts made before the failure (<paramref name="recentCalls"/>).
        /// </summary>
        private async Task<SubAgentsRunResponse> FailLoggedAsync(
            SubAgentsRunResponse response,
            SubAgentRunRequest request,
            string error,
            int? round,
            Guid? chatSessionId,
            Exception exception = null,
            IReadOnlyList<SubAgentRecentCall> recentCalls = null)
        {
            Fail(response, error);

            await _failureLogger
                .LogFromRequestAsync(
                    request,
                    "Final",
                    error,
                    response.RunId,
                    round,
                    chatSessionId,
                    exception,
                    recentCalls,
                    CancellationToken.None)
                .ConfigureAwait(false);

            return response;
        }
    }
}
