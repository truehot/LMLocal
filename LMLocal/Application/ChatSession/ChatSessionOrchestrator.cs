using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.SubAgents;
using LMLocal.Application.Tool;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.WebView;
using LMLocal.Infrastructure.WebView.Messaging;

namespace LMLocal.Application.ChatSession
{

    /// <summary>
    /// Orchestrates complete chat session lifecycle with tool execution support.
    /// Manages multiple LLM generation rounds when tools are invoked.
    /// Controls session boundaries (start/complete/error) and sends all WebView2 messages.
    /// 
    /// State flow:
    /// Initial → [Generating → ProcessingResult → ExecutingTools]* → Completing → CompactingHistory → Terminated
    /// Initial → []* → Error → Terminated
    /// Initial → []* → Cancelled → Terminated
    /// Error and Cancelled states can be reached from any point.
    /// </summary>
    internal interface IChatSessionOrchestrator
    {
        /// <summary>
        /// Generates response with automatic tool execution support.
        /// Handles complete session lifecycle including multi-round tool calls.
        /// </summary>
        Task RunSessionAsync(
            GenerateStreamContext context,
            Func<WebView2ScriptMessage, Task> onMessage,
            CancellationToken cancellationToken);

        /// <summary>
        /// Stops current session generation and tool execution.
        /// </summary>
        void StopSession();
    }

    internal class ChatSessionOrchestrator : IChatSessionOrchestrator
    {
        private readonly IChatStreamService _chatService;
        private readonly IToolExecutionManager _toolManager;
        private readonly IHistoryCompactor _compactor;
        private readonly IChatHistoryManager _history;
        private readonly ISnapshotManager _snapshotManager;
        private readonly IToolCallLoopDetector _loopDetector;
        private readonly ISubAgentsParallelPolicy _parallelPolicy;
        private readonly ISubAgentsParallelRunner _parallelRunner;
        private readonly IActiveModelContext _activeModelContext;
        private readonly object _resetLock = new object();

        private const int MAX_TOOL_ITERATIONS = 9999;
        private const int MAX_STATE_ITERATIONS = 9999;
        private const int TOOL_EXECUTION_TIMEOUT_MS = 30000;
        private const int MAX_DUPLICATE_TOOL_ROUNDS = 1;
        private const int MAX_CONTEXT_OVERFLOW_RETRIES = 2;

        private delegate Task<ChatSessionState> StateHandler(
            ChatSessionOrchestrator instance,
            SessionStateContext context,
            GenerateStreamContext generateContext,
            Func<WebView2ScriptMessage, Task> onMessage,
            CancellationToken ct);

        /// <summary>
        /// Static state handlers dictionary. Allocated once when type is loaded.
        /// </summary>
        private static readonly Dictionary<ChatSessionState, StateHandler> StateHandlers =
            new Dictionary<ChatSessionState, StateHandler>
            {
                { ChatSessionState.Initial, (self, ctx, gen, msg, ct) => self.HandleInitialStateAsync(msg) },
                { ChatSessionState.Generating, (self, ctx, gen, msg, ct) => self.HandleGeneratingStateAsync(ctx, gen, msg) },
                { ChatSessionState.ProcessingResult, (self, ctx, gen, msg, ct) => self.HandleProcessingResultStateAsync(ctx, gen, msg, ct) },
                { ChatSessionState.ExecutingTools, (self, ctx, gen, msg, ct) => self.HandleExecutingToolsStateAsync(ctx, msg, ct) },
                { ChatSessionState.Completing, (self, ctx, gen, msg, ct) => self.HandleCompletingStateAsync(ctx, msg) },
                { ChatSessionState.CompactingHistory, (self, ctx, gen, msg, ct) => self.HandleCompactingHistoryStateAsync(gen, msg, ct) },
                { ChatSessionState.Error, (self, ctx, gen, msg, ct) => self.HandleErrorStateAsync(ctx, msg) },
                { ChatSessionState.Cancelled, (self, ctx, gen, msg, ct) => self.HandleCancelledStateAsync(msg) }
            };

        private CancellationTokenSource _sessionCts;

        public ChatSessionOrchestrator(
            IChatStreamService chatService,
            IToolExecutionManager toolManager,
            IHistoryCompactor compactor,
            IChatHistoryManager history,
            ISnapshotManager snapshotManager,
            IToolCallLoopDetector loopDetector,
            ISubAgentsParallelPolicy parallelPolicy,
            ISubAgentsParallelRunner parallelRunner,
            IActiveModelContext activeModelContext = null)
        {
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _toolManager = toolManager ?? throw new ArgumentNullException(nameof(toolManager));
            _compactor = compactor ?? throw new ArgumentNullException(nameof(compactor));
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
            _loopDetector = loopDetector ?? throw new ArgumentNullException(nameof(loopDetector));
            _parallelPolicy = parallelPolicy ?? throw new ArgumentNullException(nameof(parallelPolicy));
            _parallelRunner = parallelRunner ?? throw new ArgumentNullException(nameof(parallelRunner));
            _activeModelContext = activeModelContext;
        }

        public async Task RunSessionAsync(
            GenerateStreamContext context,
            Func<WebView2ScriptMessage, Task> onMessage,
            CancellationToken cancellationToken)
        {
            lock (_resetLock)
            {
                _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }
            var sessionToken = _sessionCts.Token;

            var messageSink = onMessage != null ? new SerializingMessageSink(onMessage) : null;
            Func<WebView2ScriptMessage, Task> send = messageSink != null
                ? (Func<WebView2ScriptMessage, Task>)messageSink.SendAsync
                : null;

            var sessionContext = new SessionStateContext
            {
                CurrentState = ChatSessionState.Initial,
                RoundNumber = 0,
                AllResults = new List<StreamCompletionResult>(),
                ToolResultsForNextRound = new List<ToolResultMessage>(),
                SessionCancellationToken = sessionToken
            };

            try
            {
                int stateIterationCount = 0;

                while (sessionContext.CurrentState != ChatSessionState.Terminated)
                {
                    stateIterationCount++;
                    if (stateIterationCount > MAX_STATE_ITERATIONS)
                    {
                        InternalLogger.Error($"ChatSessionOrchestrator: State machine exceeded max iterations ({MAX_STATE_ITERATIONS})");
                        sessionContext.LastException = new InvalidOperationException("State machine exceeded maximum iterations");
                        sessionContext.CurrentState = ChatSessionState.Error;
                    }

                    try
                    {
                        if (sessionContext.CurrentState != ChatSessionState.Error && sessionContext.CurrentState != ChatSessionState.Cancelled)
                        {
                            sessionToken.ThrowIfCancellationRequested();
                        }

                        InternalLogger.Info($"ChatSessionOrchestrator: Entering state {sessionContext.CurrentState} (round={sessionContext.RoundNumber}, toolIter={sessionContext.ConsecutiveToolIterationCount}, iter={stateIterationCount})");

                        if (!StateHandlers.TryGetValue(sessionContext.CurrentState, out var handler))
                        {
                            InternalLogger.Error($"ChatSessionOrchestrator: Unknown state '{sessionContext.CurrentState}'");
                            sessionContext.CurrentState = ChatSessionState.Error;
                            sessionContext.LastException = new InvalidOperationException($"Unknown state: {sessionContext.CurrentState}");
                            continue;
                        }

                        var nextState = await handler(this, sessionContext, context, send, sessionToken).ConfigureAwait(false);

                        InternalLogger.Info($"ChatSessionOrchestrator: State {sessionContext.CurrentState} -> {nextState}");

                        sessionContext.CurrentState = nextState;
                    }
                    catch (OperationCanceledException)
                    {
                        InternalLogger.Info("ChatSessionOrchestrator: Session cancelled by user");
                        sessionContext.CurrentState = ChatSessionState.Cancelled;
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Error("ChatSessionOrchestrator: Unhandled exception in state handler", ex);
                        sessionContext.LastException = ex;
                        sessionContext.CurrentState = ChatSessionState.Error;
                    }
                }
            }
            finally
            {
                lock (_resetLock)
                {
                    _sessionCts?.Dispose();
                    _sessionCts = null;
                }

                if (sessionContext.LastException != null)
                {
                    InternalLogger.Error($"ChatSessionOrchestrator: Session terminated with error: {sessionContext.LastException.Message}");
                }
            }
        }

        /// <summary>
        /// Initial state: Sends ChatSessionStart message and transitions to Generating.
        /// </summary>
        private async Task<ChatSessionState> HandleInitialStateAsync(Func<WebView2ScriptMessage, Task> onMessage)
        {
            await onMessage(new WebView2ScriptMessage
            {
                Type = WebView2MessageType.ChatSessionStart,
                Payload = null
            }).ConfigureAwait(false);

            return ChatSessionState.Generating;
        }

        /// <summary>
        /// Generating state: Calls LLM to generate response (with or without tool context).
        /// </summary>
        private async Task<ChatSessionState> HandleGeneratingStateAsync(
            SessionStateContext context,
            GenerateStreamContext generateContext,
            Func<WebView2ScriptMessage, Task> onMessage)
        {
            await _chatService.GenerateStreamAsync(
                generateContext,
                context.ToolResultsForNextRound.Count > 0 ? context.ToolResultsForNextRound : null,
                async (chunk, stats) => await OnChunkReceivedAsync(chunk, stats, onMessage),
                result => OnGenerationCompletedAsync(context, result),
                context.SessionCancellationToken).ConfigureAwait(false);

            context.RoundNumber++;

            if (context.LastResult == null)
            {
                context.LastException = new InvalidOperationException("Generation produced no result");
                return ChatSessionState.Error;
            }

            context.AllResults.Add(context.LastResult);
            return ChatSessionState.ProcessingResult;
        }

        /// <summary>
        /// ProcessingResult state: Analyzes generation result for errors, cancellation, or tool calls.
        /// </summary>
        private async Task<ChatSessionState> HandleProcessingResultStateAsync(
            SessionStateContext context,
            GenerateStreamContext generateContext,
            Func<WebView2ScriptMessage, Task> onMessage,
            CancellationToken ct)
        {
            await onMessage(new WebView2ScriptMessage
            {
                Type = WebView2MessageType.StreamEnd,
                Payload = null
            }).ConfigureAwait(false);

            if (context.LastResult == null)
            {
                InternalLogger.Info($"СhatSessionOrchestrator: Generation completed with null result");
                context.LastException = new InvalidOperationException("LastResult is null after generation");
                return ChatSessionState.Error;
            }

            var overflowState = await HandleContextOverflowAsync(context, generateContext, onMessage, ct).ConfigureAwait(false);
            if (overflowState.HasValue)
                return overflowState.Value;

            if (!string.IsNullOrEmpty(context.LastResult.ErrorMessage))
            {
                InternalLogger.Info($"СhatSessionOrchestrator: Generation completed with error: {context.LastResult.ErrorMessage}");
                context.LastException = new InvalidOperationException(context.LastResult.ErrorMessage);
                return ChatSessionState.Error;
            }

            if (context.LastResult.WasCancelled)
            {
                InternalLogger.Info($"СhatSessionOrchestrator: Generation was cancelled by user");
                return ChatSessionState.Cancelled;
            }

            if (context.ContextOverflowRetryCount > 0 || generateContext.IsContextOverflowRetry)
            {
                InternalLogger.Info("ChatSessionOrchestrator: Round succeeded, resetting context-overflow recovery state.");
                context.ContextOverflowRetryCount = 0;
                generateContext.IsContextOverflowRetry = false;
            }

            if (context.LastResult.ToolCalls == null || context.LastResult.ToolCalls.Count == 0)
            {
                InternalLogger.Info("ChatSessionOrchestrator: No tool calls detected in generation result. Completing session.");
                context.ConsecutiveToolIterationCount = 0;
                context.ConsecutiveDuplicateToolRounds = 0;

                await onMessage(new WebView2ChatSessionIteratingMessage
                {
                    Type = WebView2MessageType.ChatSessionIterating,
                    RoundNumber = context.RoundNumber,
                    ToolCount = 0,
                    IsFinalRound = true
                }).ConfigureAwait(false);

                return ChatSessionState.Completing;
            }

            return ChatSessionState.ExecutingTools;
        }

        /// <summary>
        /// Handles a provider context-overflow error: compacts the history in memory and schedules a retry.
        /// Returns null when the result is not a context overflow and normal error handling must continue.
        /// </summary>
        private async Task<ChatSessionState?> HandleContextOverflowAsync(
            SessionStateContext context,
            GenerateStreamContext generateContext,
            Func<WebView2ScriptMessage, Task> onMessage,
            CancellationToken ct)
        {
            if (!ContextOverflowDetector.IsContextOverflow(
                    context.LastResult,
                    null,
                    _activeModelContext?.MaxContextLength > 0 ? _activeModelContext.MaxContextLength : (int?)null))
                return null;

            InternalLogger.Info("ChatSessionOrchestrator: Context overflow detected, attempting recovery.");

            if (!_compactor.IsCompactionEnabled())
            {
                InternalLogger.Info("ChatSessionOrchestrator: History compaction disabled, cannot recover from context overflow.");
                context.LastException = new InvalidOperationException(
                    "The request does not fit the model context window. Enable \"Summarize long conversations\" in settings to compact the history automatically.");
                return ChatSessionState.Error;
            }

            if (context.ContextOverflowRetryCount >= MAX_CONTEXT_OVERFLOW_RETRIES)
            {
                InternalLogger.Info("ChatSessionOrchestrator: Context overflow recovery attempts exhausted.");
                context.LastException = new InvalidOperationException(context.LastResult?.ErrorMessage);
                return ChatSessionState.Error;
            }

            CompactionOutcome outcome;
            IReadOnlyList<ChatMessage> compactedHistory = null;

            _history.DiscardLastAssistantFromFailedRound();

            await onMessage(new WebView2ScriptMessage
            {
                Type = WebView2MessageType.CompactionStart,
                Payload = null
            }).ConfigureAwait(false);

            (outcome, compactedHistory) = await _compactor
                .CompactForcedAsync(generateContext.ModelId, context.ContextOverflowRetryCount, ct)
                .ConfigureAwait(false);

            if (outcome == CompactionOutcome.Cancelled)
                return ChatSessionState.Cancelled;

            if (outcome != CompactionOutcome.Success)
            {
                InternalLogger.Info($"ChatSessionOrchestrator: Context overflow recovery failed ({outcome}).");
                if (outcome == CompactionOutcome.NothingChanged)
                {
                    context.LastException = new InvalidOperationException(
                        "There is nothing left to compact, and the prompt or its attachment together with the enabled tools does not fit the model context window. Shorten it, or attach a smaller document.");
                }

                return ChatSessionState.Error;
            }

            if (compactedHistory != null && compactedHistory.Count > 0)
            {
                await _history.ReplaceHistoryWithNewSessionAsync(compactedHistory).ConfigureAwait(false);
                context.HistoryCompacted = true;
            }

            context.ContextOverflowRetryCount++;

            if (context.AllResults.Count > 0)
                context.AllResults.RemoveAt(context.AllResults.Count - 1);

            context.LastResult = null;
            context.ToolResultsForNextRound.Clear();
            context.ConsecutiveDuplicateToolRounds = 0;
            context.ConsecutiveToolIterationCount = 0;
            generateContext.IsContextOverflowRetry = true;

            await onMessage(new WebView2CompactionEndMessage
            {
                Type = WebView2MessageType.CompactionEnd,
                Payload = null,
                Continues = true
            }).ConfigureAwait(false);

            InternalLogger.Info($"ChatSessionOrchestrator: Context overflow recovered, retrying (attempt {context.ContextOverflowRetryCount}).");
            return ChatSessionState.Generating;
        }

        /// <summary>
        /// ExecutingTools state: Executes all tool calls from current generation result.
        /// Collects results and transitions back to Generating for next round with tool context.
        /// </summary>
        private async Task<ChatSessionState> HandleExecutingToolsStateAsync(
            SessionStateContext context,
            Func<WebView2ScriptMessage, Task> onMessage,
            CancellationToken ct)
        {
            if (context.LastResult?.ToolCalls == null || context.LastResult.ToolCalls.Count == 0)
            {
                return ChatSessionState.Completing;
            }

            var currentTools = context.LastResult.ToolCalls;
            IReadOnlyList<ToolCallRecord> previousTools = null;

            if (context.AllResults.Count >= 2)
            {
                previousTools = context.AllResults[context.AllResults.Count - 2]?.ToolCalls;
            }

            if (previousTools != null && _loopDetector.AreSameToolCalls(currentTools, previousTools))
            {
                context.ConsecutiveDuplicateToolRounds++;

                if (context.ConsecutiveDuplicateToolRounds >= MAX_DUPLICATE_TOOL_ROUNDS)
                {
                    var names = string.Join(", ", currentTools.Select(t => t.FunctionName));

                    int identicalRounds = context.ConsecutiveDuplicateToolRounds + 1;

                    InternalLogger.Error(
                        $"ChatSessionOrchestrator: Tool call loop detected. " +
                        $"{identicalRounds} identical tool rounds in a row [{names}]. Halting.");

                    context.LastException = new InvalidOperationException(
                        $"Execution halted: the model repeated its previous tool round with identical arguments " +
                        $"({identicalRounds} identical rounds in a row: {names}). " +
                        "It appears to be stuck in a loop; change the request or the arguments to continue.");

                    return ChatSessionState.Error;
                }

                InternalLogger.Warn(
                    $"ChatSessionOrchestrator: Duplicate tool round #{context.ConsecutiveDuplicateToolRounds}. " +
                    $"Threshold is {MAX_DUPLICATE_TOOL_ROUNDS}.");
            }
            else
            {
                context.ConsecutiveDuplicateToolRounds = 0;
            }

            context.ConsecutiveToolIterationCount++;
            context.ToolResultsForNextRound.Clear();

            await _snapshotManager.BeginBatchAsync(ct).ConfigureAwait(false);
            try
            {
                var allCalls = context.LastResult.ToolCalls;

                var parallelIdx = new List<int>();
                var sequentialIdx = new List<int>();

                for (int i = 0; i < allCalls.Count; i++)
                {
                    var call = allCalls[i];
                    if (call != null && _parallelPolicy.IsParallelEnabled(call.FunctionName))
                        parallelIdx.Add(i);
                    else
                        sequentialIdx.Add(i);
                }

                var ordered = new ToolResultMessage[allCalls.Count];

                if (parallelIdx.Count > 1)
                {
                    var parallelCalls = new List<ToolCallRecord>(parallelIdx.Count);
                    foreach (var i in parallelIdx)
                        parallelCalls.Add(allCalls[i]);

                    var groupMaxParallel = parallelCalls
                        .Select(c => _parallelPolicy.ResolveMaxParallel(c.FunctionName))
                        .DefaultIfEmpty(SubAgentsConfig.DefaultMaxParallel)
                        .Min();
                    if (groupMaxParallel < 1)
                        groupMaxParallel = SubAgentsConfig.DefaultMaxParallel;

                    InternalLogger.Info($"ChatSessionOrchestrator: Running {parallelCalls.Count} SubAgents in parallel (max {groupMaxParallel} concurrently).");


                    string groupId = Guid.NewGuid().ToString("N");
                    var groupCallIds = parallelCalls.Select(c => c.CallId).ToList();
                    int groupTotal = parallelCalls.Count;
                    int groupCompleted = 0;

                    await onMessage(new WebView2ToolGroupMessage
                    {
                        Type = WebView2MessageType.StreamToolGroupStart,
                        GroupId = groupId,
                        CallIds = groupCallIds,
                        Total = groupTotal,
                        Completed = 0
                    }).ConfigureAwait(false);

                    var groupResult = await _parallelRunner.RunAsync(
                        parallelCalls,
                        async (call, callCt) =>
                        {
                            var callResult = await RunToolCallAsync(call, onMessage, callCt).ConfigureAwait(false);

                            int completed = Interlocked.Increment(ref groupCompleted);
                            await onMessage(new WebView2ToolGroupMessage
                            {
                                Type = WebView2MessageType.StreamToolGroupProgress,
                                GroupId = groupId,
                                CallIds = groupCallIds,
                                Total = groupTotal,
                                Completed = completed
                            }).ConfigureAwait(false);

                            return callResult;
                        },
                        groupMaxParallel,
                        ct).ConfigureAwait(false);

                    await onMessage(new WebView2ToolGroupMessage
                    {
                        Type = WebView2MessageType.StreamToolGroupEnd,
                        GroupId = groupId,
                        CallIds = groupCallIds,
                        Total = groupTotal,
                        Completed = groupTotal
                    }).ConfigureAwait(false);

                    for (int j = 0; j < parallelCalls.Count; j++)
                    {
                        ordered[parallelIdx[j]] = ToToolResultMessage(parallelCalls[j], groupResult.Results[j]);
                    }
                }
                else if (parallelIdx.Count == 1)
                {
                    // A single agent has nothing to overlap with; run it inline.
                    ct.ThrowIfCancellationRequested();
                    var call = allCalls[parallelIdx[0]];
                    var result = await RunToolCallAsync(call, onMessage, ct).ConfigureAwait(false);
                    ordered[parallelIdx[0]] = ToToolResultMessage(call, result);
                }

                foreach (var i in sequentialIdx)
                {
                    ct.ThrowIfCancellationRequested();
                    var call = allCalls[i];
                    var result = await RunToolCallAsync(call, onMessage, ct).ConfigureAwait(false);
                    ordered[i] = ToToolResultMessage(call, result);
                }

                foreach (var result in ordered)
                {
                    if (result != null)
                        context.ToolResultsForNextRound.Add(result);
                }
            }

            catch (Exception)
            {
                await _snapshotManager.CancelBatchAsync(ct).ConfigureAwait(false);
                throw;
            }
            finally
            {
                await _snapshotManager.EndBatchAsync(ct).ConfigureAwait(false);
            }

            await SendSnapshotUpdateAsync(onMessage).ConfigureAwait(false);

            if (context.ConsecutiveToolIterationCount >= MAX_TOOL_ITERATIONS)
            {
                InternalLogger.Info($"ChatSessionOrchestrator: Reached max consecutive tool iterations ({MAX_TOOL_ITERATIONS}). Ending session to prevent infinite loop.");
                context.ConsecutiveToolIterationCount = 0;

                await onMessage(new WebView2ChatSessionIteratingMessage
                {
                    Type = WebView2MessageType.ChatSessionIterating,
                    RoundNumber = context.RoundNumber,
                    ToolCount = 0,
                    IsFinalRound = true
                }).ConfigureAwait(false);

                return ChatSessionState.Completing;
            }

            if (context.ToolResultsForNextRound.Count > 0)
            {
                InternalLogger.Info($"ChatSessionOrchestrator: Starting tool iteration {context.ConsecutiveToolIterationCount + 1} of {MAX_TOOL_ITERATIONS}");

                int completedToolCount = context.LastResult?.ToolCalls?.Count ?? 0;
                int estimatedToolResultTokens = ToolResultTokenEstimator.Estimate(context.ToolResultsForNextRound);

                await onMessage(new WebView2ChatSessionIteratingMessage
                {
                    Type = WebView2MessageType.ChatSessionIterating,
                    RoundNumber = context.RoundNumber,
                    ToolCount = completedToolCount,
                    IsFinalRound = false,
                    EstimatedToolResultTokens = estimatedToolResultTokens
                }).ConfigureAwait(false);

                return ChatSessionState.Generating;
            }

            context.ConsecutiveToolIterationCount = 0;
            return ChatSessionState.Completing;
        }

        /// <summary>
        /// Runs one tool call: emits StreamToolCall, executes with a per-call timeout while forwarding progress steps, then emits StreamToolEnd.
        /// </summary>
        private async Task<ToolExecutionResult> RunToolCallAsync(
            ToolCallRecord toolCall,
            Func<WebView2ScriptMessage, Task> onMessage,
            CancellationToken ct)
        {
            var processingMessage = _toolManager.GetProcessingMessage(toolCall);

            var uiCallId = "ui_" + Guid.NewGuid().ToString("N");

            await onMessage(new WebView2ToolCallMessage
            {
                Type = WebView2MessageType.StreamToolCall,
                FunctionName = toolCall.FunctionName,
                CallId = uiCallId,
                ArgumentsJson = toolCall.ArgumentsJson,
                Message = processingMessage
            }).ConfigureAwait(false);

            ToolExecutionResult toolResult;
            using (var toolCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var toolTimeout = _toolManager.GetToolTimeout(toolCall.FunctionName) ?? TimeSpan.FromMilliseconds(TOOL_EXECUTION_TIMEOUT_MS);
                toolCts.CancelAfter(toolTimeout);

                var stepProgress = new ToolActivityForwarder(uiCallId, onMessage);
                toolResult = await _toolManager.ExecuteToolAsync(toolCall, toolCts.Token, stepProgress).ConfigureAwait(false);

                if (toolCts.IsCancellationRequested && !ct.IsCancellationRequested && !toolResult.IsSuccess)
                {
                    var timeoutText = $"Tool '{toolCall.FunctionName}' timed out after {(int)toolTimeout.TotalSeconds} s.";
                    toolResult.Error = timeoutText;
                    toolResult.Result = null;
                    toolResult.UserMessage = timeoutText;
                }
            }

            ct.ThrowIfCancellationRequested();

            await onMessage(new WebView2ToolCallMessage
            {
                Type = WebView2MessageType.StreamToolEnd,
                FunctionName = toolCall.FunctionName,
                CallId = uiCallId,
                Message = string.IsNullOrEmpty(toolResult.Error) ? toolResult.CompletionMessage : toolResult.UserMessage,
                IsError = !string.IsNullOrEmpty(toolResult.Error)
            }).ConfigureAwait(false);

            return toolResult;
        }

        private static ToolResultMessage ToToolResultMessage(ToolCallRecord toolCall, ToolExecutionResult toolResult)
        {
            return new ToolResultMessage
            {
                ToolCallId = toolCall.CallId,
                ToolName = toolCall.FunctionName,
                Result = string.IsNullOrEmpty(toolResult.Error) ? toolResult.Result : toolResult.Error,
                Error = toolResult.Error
            };
        }

        /// <summary>
        /// Completing state: Sends ChatSessionComplete message with metadata.
        /// Transitions to CompactingHistory to perform history compaction before terminating.
        /// </summary>
        private async Task<ChatSessionState> HandleCompletingStateAsync(
            SessionStateContext context,
            Func<WebView2ScriptMessage, Task> onMessage)
        {
            if (context.AllResults.Count > 0)
            {
                var finalResult = context.AllResults[context.AllResults.Count - 1];

                if (!finalResult.WasCancelled && string.IsNullOrEmpty(finalResult.ErrorMessage))
                {
                    var (Prompt, Completion, Cached) = SumRequestTokens(context.AllResults);

                    var completeMsg = new WebView2SessionCompleteMessage
                    {
                        Type = WebView2MessageType.ChatSessionComplete,
                        FinishReason = finalResult.FinishReason,
                        TotalTokens = finalResult.TokenUsage?.TotalTokens,
                        PromptTokens = finalResult.TokenUsage?.PromptTokens,
                        CompletionTokens = finalResult.TokenUsage?.CompletionTokens,
                        RequestPromptTokens = Prompt,
                        RequestCompletionTokens = Completion,
                        RequestCachedTokens = Cached,
                        ReasoningTokens = finalResult.TokenUsage?.ReasoningTokens,
                        CachedTokens = finalResult.TokenUsage?.CachedTokens,
                        TokensPerSecond = finalResult.TokensPerSecond
                    };

                    await onMessage(completeMsg).ConfigureAwait(false);
                }
            }

            return context.HistoryCompacted
                ? ChatSessionState.Terminated
                : ChatSessionState.CompactingHistory;
        }

        /// <summary>
        /// Sums prompt, completion and cached prompt tokens across every generation round of the request.
        /// </summary>
        private static (int? Prompt, int? Completion, int? Cached) SumRequestTokens(
            IReadOnlyList<StreamCompletionResult> results)
        {
            if (results == null || results.Count == 0)
            {
                return (null, null, null);
            }

            int prompt = 0;
            int completion = 0;
            int cached = 0;
            bool hasPrompt = false;
            bool hasCompletion = false;
            bool hasCached = false;

            foreach (var result in results)
            {
                if (result?.TokenUsage?.PromptTokens is int promptTokens)
                {
                    prompt += promptTokens;
                    hasPrompt = true;
                }

                if (result?.TokenUsage?.CompletionTokens is int completionTokens)
                {
                    completion += completionTokens;
                    hasCompletion = true;
                }

                if (result?.TokenUsage?.CachedTokens is int cachedTokens)
                {
                    cached += cachedTokens;
                    hasCached = true;
                }
            }

            return (hasPrompt ? prompt : (int?)null,
                    hasCompletion ? completion : (int?)null,
                    hasCached ? cached : (int?)null);
        }

        /// <summary>
        /// Error state: Sends error messages and transitions to Terminated.
        /// </summary>
        private async Task<ChatSessionState> HandleErrorStateAsync(
            SessionStateContext context,
            Func<WebView2ScriptMessage, Task> onMessage)
        {

            var errorMsg = context.LastException != null
                ? ExceptionFormatter.Format(context.LastException)
                : context.LastResult?.ErrorMessage;

            await onMessage(new WebView2ScriptMessage
            {
                Type = WebView2MessageType.ChatSessionError,
                Payload = errorMsg
            }).ConfigureAwait(false);

            return ChatSessionState.Terminated;
        }

        /// <summary>
        /// CompactingHistory state: Performs history compaction if needed and sends status messages.
        /// </summary>
        private async Task<ChatSessionState> HandleCompactingHistoryStateAsync(
            GenerateStreamContext generateContext,
            Func<WebView2ScriptMessage, Task> onMessage,
            CancellationToken ct)
        {
            if (!_compactor.NeedsCompaction())
            {
                return ChatSessionState.Terminated;
            }

            try
            {
                await onMessage(new WebView2ScriptMessage
                {
                    Type = WebView2MessageType.CompactionStart,
                    Payload = null
                }).ConfigureAwait(false);

                await _compactor.CompactIfNeededAsync(generateContext.ModelId, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException exc)
            {
                InternalLogger.Error($"ChatSessionOrchestrator: Session cancelled: {exc.Message}");
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"ChatSessionOrchestrator: History compaction failed: {ex.Message}");
            }
            finally
            {
                await onMessage(new WebView2CompactionEndMessage
                {
                    Type = WebView2MessageType.CompactionEnd,
                    Payload = null,
                    Continues = false
                }).ConfigureAwait(false);
            }

            return ChatSessionState.Terminated;
        }

        private async Task<ChatSessionState> HandleCancelledStateAsync(
            Func<WebView2ScriptMessage, Task> onMessage)
        {
            await onMessage(new WebView2ScriptMessage
            {
                Type = WebView2MessageType.ChatSessionCancelled,
                Payload = "Generation stopped by user"
            }).ConfigureAwait(false);

            return ChatSessionState.Terminated;
        }

        private async Task OnChunkReceivedAsync(
            TextStreamChunk chunk,
            TokenGenerationStats stats,
            Func<WebView2ScriptMessage, Task> onMessage)
        {
            var msg = new WebView2ScriptMessageWithCount
            {
                Type = chunk.Kind == ChunkKind.Reasoning ? WebView2MessageType.StreamThought : WebView2MessageType.StreamContent,
                Payload = chunk.Text,
                Count = stats.TotalTokens,
                TokensPerSecond = stats.TokensPerSecond
            };

            await onMessage(msg).ConfigureAwait(false);
        }

        private Task OnGenerationCompletedAsync(
            SessionStateContext context,
            StreamCompletionResult result)
        {
            context.LastResult = result;
            return Task.CompletedTask;
        }

        public void StopSession()
        {
            CancellationTokenSource ctsToCancel = null;

            lock (_resetLock)
            {
                ctsToCancel = _sessionCts;
            }

            if (ctsToCancel != null)
            {
                try
                {
                    ctsToCancel.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    InternalLogger.Warn("ChatSessionOrchestrator: Object already disposed");
                }
            }
        }

        private async Task SendSnapshotUpdateAsync(Func<WebView2ScriptMessage, Task> onMessage)
        {
            try
            {
                var changedFiles = await _snapshotManager.GetChangedFilesWithStatusAsync().ConfigureAwait(false);

                var message = new WebView2SnapshotMessage
                {
                    ChangedFiles = changedFiles.ToList(),
                };

                if (onMessage != null)
                {
                    await onMessage(message).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error("SendSnapshotUpdateAsync failed", ex);
            }
        }
    }
}
