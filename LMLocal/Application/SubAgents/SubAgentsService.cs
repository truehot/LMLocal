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
        private const int DefaultTimeoutSeconds = 120;
        private const int MaxDuplicateToolRounds = 2;

        private readonly ISettingsManager _settingsManager;
        private readonly IFileSystem _fileSystem;
        private readonly IStreamingRoundService _roundService;
        private readonly Func<IToolExecutionManager> _toolExecutionManagerResolver;
        private readonly IToolQueueProvider _queueProvider;
        private readonly IToolCallLoopDetector _loopDetector;
        private readonly IChatPersistenceFactory _chatPersistenceFactory;
        private readonly ISubAgentFailureLogger _failureLogger;
        private readonly string _logsFolder;

        public SubAgentsService(
            ISettingsManager settingsManager,
            IFileSystem fileSystem,
            IStreamingRoundService roundService,
            Func<IToolExecutionManager> toolExecutionManagerResolver,
            IToolQueueProvider queueProvider,
            IToolCallLoopDetector loopDetector,
            IChatPersistenceFactory chatPersistenceFactory,
            ISubAgentFailureLogger failureLogger)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _roundService = roundService ?? throw new ArgumentNullException(nameof(roundService));
            _toolExecutionManagerResolver = toolExecutionManagerResolver ?? throw new ArgumentNullException(nameof(toolExecutionManagerResolver));
            _queueProvider = queueProvider ?? throw new ArgumentNullException(nameof(queueProvider));
            _loopDetector = loopDetector ?? throw new ArgumentNullException(nameof(loopDetector));
            _chatPersistenceFactory = chatPersistenceFactory ?? throw new ArgumentNullException(nameof(chatPersistenceFactory));
            _failureLogger = failureLogger ?? throw new ArgumentNullException(nameof(failureLogger));

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

            try
            {
                if (request == null)
                {
                    return await FailLoggedAsync(
                        response, null, "SubAgent run request is required.",
                        null, null).ConfigureAwait(false);
                }

                if (string.IsNullOrWhiteSpace(request.Prompt))
                {
                    return await FailLoggedAsync(
                        response, request, "Prompt is required.",
                        null, null).ConfigureAwait(false);
                }

                var modelId = request.Model?.Trim();
                if (string.IsNullOrWhiteSpace(modelId))
                {
                    return await FailLoggedAsync(
                        response, request, "Model is required.",
                        null, null).ConfigureAwait(false);
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
                bool isFirstRound = true;
                int stepCounter = 0;

                var ledger = new ReadLedger();

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
                            IsToolRound = !isFirstRound,
                            ToolResults = isFirstRound ? null : toolResultsForRound,
                            Prompt = isFirstRound ? request.Prompt : null,
                            SystemPrompt = systemPrompt,
                            ModelId = modelId,
                            Temperature = request.Temperature,
                            ReasoningEffort = request.ReasoningEffort,
                            MaxOutputTokens = request.MaxTokens,
                            Provider = provider,
                            Tools = tools,
                            OnChunk = null
                        };
                        isFirstRound = false;

                        var roundResult = await _roundService.RunAsync(roundRequest, overallCts.Token).ConfigureAwait(false);
                        var streamResult = roundResult.Stream;

                        if (cancellationToken.IsCancellationRequested)
                        {
                            return Fail(response, "SubAgent execution was cancelled.");
                        }

                        if (overallCts.Token.IsCancellationRequested)
                        {
                            return await FailLoggedAsync(
                                response, request, "SubAgent timed out (no response within the configured window).",
                                round, chatSessionId).ConfigureAwait(false);
                        }

                        switch (roundResult.Outcome)
                        {
                            case StreamRoundOutcome.Error:
                                return await FailLoggedAsync(
                                    response, request, "Provider stream error: " + streamResult.ErrorMessage,
                                    round, chatSessionId).ConfigureAwait(false);

                            case StreamRoundOutcome.Cancelled:
                                return Fail(response, "SubAgent execution was cancelled.");

                            case StreamRoundOutcome.Incomplete:
                                return await FailLoggedAsync(
                                    response, request, streamResult.ErrorMessage,
                                    round, chatSessionId).ConfigureAwait(false);

                            case StreamRoundOutcome.ToolCalls:
                                if (previousToolCalls != null && _loopDetector.AreSameToolCalls(streamResult.ToolCalls, previousToolCalls))
                                {
                                    consecutiveDuplicateRounds++;
                                    if (consecutiveDuplicateRounds >= MaxDuplicateToolRounds)
                                    {
                                        return await FailLoggedAsync(
                                            response, request, "SubAgent detected a repeated tool call loop and stopped.",
                                            round, chatSessionId).ConfigureAwait(false);
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

                                    var execResult = await _toolExecutionManagerResolver()
                                        .ExecuteToolAsync(toolCall, overallCts.Token, toolQueue)
                                        .ConfigureAwait(false);

                                    object resultValue = string.IsNullOrEmpty(execResult.Error) ? execResult.Result : execResult.Error;

                                    if (string.IsNullOrEmpty(execResult.Error)
                                        && string.Equals(toolCall.FunctionName, ReadResultRecorder.ReadFileLinesToolName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        ReadResultRecorder.TryRecord(ledger, execResult.Result);
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

                                if (ledger.Count > 0 && !string.IsNullOrEmpty(response.Content))
                                {
                                    var expander = new ReadRefExpander(ledger, _fileSystem);
                                    var expanded = await expander.ExpandAsync(response.Content, overallCts.Token).ConfigureAwait(false);
                                    response.Content = expanded.Content;
                                    response.ExpandedRefCount = expanded.ExpandedRefCount;
                                    response.UnresolvedRefCount = expanded.UnresolvedRefCount;
                                }

                                return response;
                        }
                    }

                    return await FailLoggedAsync(
                        response, request, $"SubAgent exceeded maximum rounds ({maxRounds}).",
                        lastRound, chatSessionId).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return Fail(response, "SubAgent execution was cancelled.");
                }

                return await FailLoggedAsync(
                    response, request, "SubAgent timed out (no response within the configured window).",
                    lastRound, chatSessionId).ConfigureAwait(false);
            }
            catch (ApiException ex)
            {
                InternalLogger.Warn($"SubAgentsService: provider API error: {ex.Message}");
                return await FailLoggedAsync(
                    response, request, "Provider API error: " + ExceptionFormatter.Format(ex),
                    lastRound, chatSessionId, ex).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                return await FailLoggedAsync(
                    response, request, ExceptionFormatter.Format(ex),
                    lastRound, chatSessionId, ex).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"SubAgentsService: unexpected error during run '{runId}'", ex);
                return await FailLoggedAsync(
                    response, request, ExceptionFormatter.Format(ex),
                    lastRound, chatSessionId, ex).ConfigureAwait(false);
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

        private static SubAgentsRunResponse Fail(SubAgentsRunResponse response, string error)
        {
            response.Success = false;
            response.Error = error;
            return response;
        }

        /// <summary>
        /// Marks the run as failed AND writes a replayable record.
        /// </summary>
        private async Task<SubAgentsRunResponse> FailLoggedAsync(
            SubAgentsRunResponse response,
            SubAgentRunRequest request,
            string error,
            int? round,
            Guid? chatSessionId,
            Exception exception = null)
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
                    CancellationToken.None)
                .ConfigureAwait(false);

            return response;
        }
    }
}
