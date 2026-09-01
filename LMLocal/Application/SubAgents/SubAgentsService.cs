using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.Tool;
using LMLocal.Core.Common;
using LMLocal.Core.Exceptions;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling;

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
        private const int MaxDuplicateToolRounds = 3;

        private readonly ISettingsManager _settingsManager;
        private readonly IFileSystem _fileSystem;
        private readonly IStreamingRoundService _roundService;
        private readonly Func<IToolExecutionManager> _toolExecutionManagerResolver;
        private readonly IToolQueueProvider _queueProvider;
        private readonly IToolCallLoopDetector _loopDetector;
        private readonly string _logsFolder;

        public SubAgentsService(
            ISettingsManager settingsManager,
            IFileSystem fileSystem,
            IStreamingRoundService roundService,
            Func<IToolExecutionManager> toolExecutionManagerResolver,
            IToolQueueProvider queueProvider,
            IToolCallLoopDetector loopDetector)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _roundService = roundService ?? throw new ArgumentNullException(nameof(roundService));
            _toolExecutionManagerResolver = toolExecutionManagerResolver ?? throw new ArgumentNullException(nameof(toolExecutionManagerResolver));
            _queueProvider = queueProvider ?? throw new ArgumentNullException(nameof(queueProvider));
            _loopDetector = loopDetector ?? throw new ArgumentNullException(nameof(loopDetector));

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
            try
            {
                if (request == null)
                {
                    return Fail(response, "SubAgent run request is required.");
                }

                if (string.IsNullOrWhiteSpace(request.Prompt))
                {
                    return Fail(response, "Prompt is required.");
                }

                var modelId = request.Model?.Trim();
                if (string.IsNullOrWhiteSpace(modelId))
                {
                    return Fail(response, "Model is required.");
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

                ChatPersistenceService persistence = new ChatPersistenceService(_settingsManager, _fileSystem, _logsFolder);
                ChatHistoryManager history = new ChatHistoryManager(_settingsManager, persistence);
                await persistence.MarkNewSessionAsync(cancellationToken).ConfigureAwait(false);

                var usedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                IReadOnlyList<ToolCallRecord> previousToolCalls = null;
                int consecutiveDuplicateRounds = 0;
                List<ToolResultMessage> toolResultsForRound = null;
                bool isFirstRound = true;

                using (var overallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    if (timeoutSeconds > 0)
                    {
                        overallCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                    }

                    for (int round = 0; round < maxRounds; round++)
                    {
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
                            return Fail(response, "SubAgent timed out (no response within the configured window).");
                        }

                        switch (roundResult.Outcome)
                        {
                            case StreamRoundOutcome.Error:
                                return Fail(response, "Provider stream error: " + streamResult.ErrorMessage);

                            case StreamRoundOutcome.Cancelled:
                                return Fail(response, "SubAgent execution was cancelled.");

                            case StreamRoundOutcome.Incomplete:
                                return Fail(response, streamResult.ErrorMessage);

                            case StreamRoundOutcome.ToolCalls:
                                if (previousToolCalls != null && _loopDetector.AreSameToolCalls(streamResult.ToolCalls, previousToolCalls))
                                {
                                    consecutiveDuplicateRounds++;
                                    if (consecutiveDuplicateRounds >= MaxDuplicateToolRounds)
                                    {
                                        return Fail(response, "SubAgent detected a repeated tool call loop and stopped.");
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

                                    if (string.IsNullOrEmpty(toolCall.FunctionName) || !IsToolAllowed(toolCall.FunctionName, tools))
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

                                    var execResult = await _toolExecutionManagerResolver()
                                        .ExecuteToolAsync(toolCall, overallCts.Token, toolQueue)
                                        .ConfigureAwait(false);

                                    toolResults.Add(new ToolResultMessage
                                    {
                                        ToolCallId = toolCall.CallId,
                                        ToolName = toolCall.FunctionName,
                                        Result = string.IsNullOrEmpty(execResult.Error) ? execResult.Result : execResult.Error,
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
                                return response;
                        }
                    }

                    return Fail(response, $"SubAgent exceeded maximum rounds ({maxRounds}).");
                }
            }
            catch (OperationCanceledException)
            {
                return Fail(response, cancellationToken.IsCancellationRequested
                    ? "SubAgent execution was cancelled."
                    : "SubAgent timed out (no response within the configured window).");
            }
            catch (ApiException ex)
            {
                InternalLogger.Warn($"SubAgentsService: provider API error: {ex.Message}");
                return Fail(response, "Provider API error: " + ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Fail(response, ex.Message);
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"SubAgentsService: unexpected error during run '{runId}'", ex);
                return Fail(response, ex.Message);
            }
            finally
            {
                stopwatch.Stop();
                response.DurationMs = stopwatch.ElapsedMilliseconds;
            }
        }

        private static bool IsToolAllowed(string toolName, IReadOnlyList<ToolDefinition> tools)
        {
            foreach (var def in tools)
            {
                if (string.Equals(def.Name, toolName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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
    }
}
