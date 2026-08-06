using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.Tool;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.WebView;
using LMLocal.Services.Tool;

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
        private readonly ISnapshotManager _snapshotManager;
        private readonly IToolCallLoopDetector _loopDetector;
        private readonly object _resetLock = new object();

        private const int MAX_TOOL_ITERATIONS = 9999;
        private const int MAX_STATE_ITERATIONS = 9999;
        private const int TOOL_EXECUTION_TIMEOUT_MS = 30000;
        private const int MAX_DUPLICATE_TOOL_ROUNDS = 3;

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
                { ChatSessionState.ProcessingResult, (self, ctx, gen, msg, ct) => self.HandleProcessingResultStateAsync(ctx, msg) },
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
            ISnapshotManager snapshotManager,
            IToolCallLoopDetector loopDetector)
        {
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _toolManager = toolManager ?? throw new ArgumentNullException(nameof(toolManager));
            _compactor = compactor ?? throw new ArgumentNullException(nameof(compactor));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
            _loopDetector = loopDetector ?? throw new ArgumentNullException(nameof(loopDetector));
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

                        var nextState = await handler(this, sessionContext, context, onMessage, sessionToken).ConfigureAwait(false);

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
            Func<WebView2ScriptMessage, Task> onMessage)
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

            if (context.LastResult.ToolCalls == null || context.LastResult.ToolCalls.Count == 0)
            {
                InternalLogger.Info($"ChatSessionOrchestrator: No tool calls detected in generation result. Completing session.");
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
        /// ExecutingTools state: Executes all tool calls from current generation result.
        /// Collects results and transitions back to Generating for next round with tool context.
        /// Prevents infinite loops by limiting consecutive tool iterations per request.
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

                    InternalLogger.Error(
                        $"ChatSessionOrchestrator: Tool call loop detected. " +
                        $"Tool(s) [{names}] called with identical arguments " +
                        $"{context.ConsecutiveDuplicateToolRounds} times in a row. Halting.");

                    context.LastException = new InvalidOperationException(
                        $"Execution halted: tool(s) [{names}] called with identical arguments " +
                        $"{context.ConsecutiveDuplicateToolRounds} times in a row. " +
                        $"The model appears to be stuck in a loop.");

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
                foreach (var toolCall in context.LastResult.ToolCalls)
                {
                    ct.ThrowIfCancellationRequested();

                    var processingMessage = _toolManager.GetProcessingMessage(toolCall);

                    await onMessage(new WebView2ToolCallMessage
                    {
                        Type = WebView2MessageType.StreamToolCall,
                        FunctionName = toolCall.FunctionName,
                        CallId = toolCall.CallId,
                        ArgumentsJson = toolCall.ArgumentsJson,
                        Message = processingMessage
                    }).ConfigureAwait(false);

                    using (var toolCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        toolCts.CancelAfter(TOOL_EXECUTION_TIMEOUT_MS);

                        var toolResult = await _toolManager.ExecuteToolAsync(toolCall, toolCts.Token).ConfigureAwait(false);

                        context.ToolResultsForNextRound.Add(new ToolResultMessage
                        {
                            ToolCallId = toolCall.CallId,
                            ToolName = toolCall.FunctionName,
                            Result = string.IsNullOrEmpty(toolResult.Error) ? toolResult.Result : toolResult.Error,
                            Error = toolResult.Error
                        });

                        await onMessage(new WebView2ToolCallMessage
                        {
                            Type = WebView2MessageType.StreamToolEnd,
                            FunctionName = toolCall.FunctionName,
                            CallId = toolCall.CallId,
                            Message = toolResult.CompletionMessage,
                            Error = toolResult.Error,
                            IsError = !string.IsNullOrEmpty(toolResult.Error)
                        }).ConfigureAwait(false);
                    }
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
                await onMessage(new WebView2ChatSessionIteratingMessage
                {
                    Type = WebView2MessageType.ChatSessionIterating,
                    RoundNumber = context.RoundNumber,
                    ToolCount = completedToolCount,
                    IsFinalRound = false
                }).ConfigureAwait(false);

                return ChatSessionState.Generating;
            }

            context.ConsecutiveToolIterationCount = 0;
            return ChatSessionState.Completing;
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
                    var completeMsg = new WebView2SessionCompleteMessage
                    {
                        Type = WebView2MessageType.ChatSessionComplete,
                        FinishReason = finalResult.FinishReason,
                        TotalTokens = finalResult.TokenUsage?.TotalTokens,
                        PromptTokens = finalResult.TokenUsage?.PromptTokens,
                        CompletionTokens = finalResult.TokenUsage?.CompletionTokens,
                        ReasoningTokens = finalResult.TokenUsage?.ReasoningTokens,
                        RefusalReason = finalResult.RefusalReason
                    };

                    await onMessage(completeMsg).ConfigureAwait(false);
                }
            }

            return ChatSessionState.CompactingHistory;
        }

        /// <summary>
        /// Error state: Sends error messages and transitions to Terminated.
        /// </summary>
        private async Task<ChatSessionState> HandleErrorStateAsync(
            SessionStateContext context,
            Func<WebView2ScriptMessage, Task> onMessage)
        {
            var errorMsg = context.LastResult?.ErrorMessage
                ?? context.LastException?.Message
                ?? "Unknown error occurred";

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
                await onMessage(new WebView2ScriptMessage
                {
                    Type = WebView2MessageType.CompactionEnd,
                    Payload = null
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
