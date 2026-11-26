using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Application.Tool;
using LMLocal.Application.Usage;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Core.Models.Usage;
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
        private readonly IHistoryCompactor _compactor;
        private readonly IChatHistoryManager _history;
        private readonly ChatToolRoundRunner _toolRoundRunner;
        private readonly IActiveModelContext _activeModelContext;
        private readonly ChatUsageWriter _usageWriter;
        private readonly object _resetLock = new object();

        private const int MAX_TOOL_ITERATIONS = 9999;
        private const int MAX_STATE_ITERATIONS = 9999;
        private const int MAX_DUPLICATE_TOOL_ROUNDS = 1;
        private const int MAX_CONTEXT_OVERFLOW_RETRIES = 2;

        /// <summary>Length of the session title written into a usage record.</summary>
        private const int SessionTitleLength = 80;

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
                { ChatSessionState.CompactingHistory, (self, ctx, gen, msg, ct) => self.HandleCompactingHistoryStateAsync(ctx, gen, msg, ct) },
                { ChatSessionState.Error, (self, ctx, gen, msg, ct) => self.HandleErrorStateAsync(ctx, msg) },
                { ChatSessionState.Cancelled, (self, ctx, gen, msg, ct) => self.HandleCancelledStateAsync(msg) }
            };

        private CancellationTokenSource _sessionCts;

        public ChatSessionOrchestrator(
            IChatStreamService chatService,
            IHistoryCompactor compactor,
            IChatHistoryManager history,
            ChatToolRoundRunner toolRoundRunner,
            IActiveModelContext activeModelContext,
            ChatUsageWriter usageWriter)
        {
            _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));
            _compactor = compactor ?? throw new ArgumentNullException(nameof(compactor));
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _toolRoundRunner = toolRoundRunner ?? throw new ArgumentNullException(nameof(toolRoundRunner));
            _activeModelContext = activeModelContext ?? throw new ArgumentNullException(nameof(activeModelContext));
            _usageWriter = usageWriter ?? throw new ArgumentNullException(nameof(usageWriter));
        }

        public async Task RunSessionAsync(
            GenerateStreamContext context,
            Func<WebView2ScriptMessage, Task> onMessage,
            CancellationToken cancellationToken)
        {
            if (onMessage == null) throw new ArgumentNullException(nameof(onMessage));

            lock (_resetLock)
            {
                _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }
            var sessionToken = _sessionCts.Token;

            var messageSink = new SerializingMessageSink(onMessage);
            var rateLimitNotifier = new BoundedMessageNotifier(onMessage);

            Task rateLimitDelivery = Task.Run(() => rateLimitNotifier.RunAsync());

            var sessionContext = new SessionStateContext
            {
                CurrentState = ChatSessionState.Initial,
                RoundNumber = 0,
                AllResults = new List<StreamCompletionResult>(),
                ToolResultsForNextRound = new List<ToolResultMessage>(),
                SessionCancellationToken = sessionToken,
                RateLimitWaitSink = info => OnRateLimitWaitAsync(rateLimitNotifier, info),
                RequestId = Guid.NewGuid().ToString("N"),
                StartedUtc = DateTime.UtcNow
            };

            _history.EnsureChatDateLine();

            var requestStopwatch = Stopwatch.StartNew();
            var terminalState = ChatSessionState.Terminated;

            UsageRequestScope.Scope usageScope = null;

            try
            {
                if (!string.IsNullOrEmpty(context.ReplaceLast))
                {
                    if (!ReplaceLastModes.TryGetKeepUserMessage(context.ReplaceLast, out bool keepUserMessage))
                    {
                        InternalLogger.Warn($"ChatSessionOrchestrator: Unknown replaceLast '{context.ReplaceLast}' ignored.");
                    }
                    else
                    {
                        var outcome = await _history
                            .TruncateLastExchangeAsync(keepUserMessage, context.ReplaceLastPrompt)
                            .ConfigureAwait(false);

                        if (outcome != TruncateLastExchangeOutcome.Replaced)
                        {
                            InternalLogger.Info($"ChatSessionOrchestrator: The last exchange was not replaced ({outcome}).");

                            await messageSink.SendAsync(new WebView2ScriptMessage
                            {
                                Type = WebView2MessageType.ChatSessionError,
                                Payload = outcome == TruncateLastExchangeOutcome.OutOfSync
                                    ? "The chat is out of sync with the history. Nothing was changed."
                                    : "The last prompt cannot be asked again. Nothing was changed."
                            }).ConfigureAwait(false);

                            return;
                        }

                        InternalLogger.Info($"ChatSessionOrchestrator: Replaced the last exchange (replaceLast={context.ReplaceLast}) before the first round.");
                    }
                }

                usageScope = BeginUsageScope(sessionContext, context);

                int stateIterationCount = 0;

                while (sessionContext.CurrentState != ChatSessionState.Terminated)
                {
                    stateIterationCount++;
                    if (stateIterationCount > MAX_STATE_ITERATIONS)
                    {
                        InternalLogger.Warn($"ChatSessionOrchestrator: State machine exceeded max iterations ({MAX_STATE_ITERATIONS})");
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
                            InternalLogger.Warn($"ChatSessionOrchestrator: Unknown state '{sessionContext.CurrentState}'");
                            sessionContext.CurrentState = ChatSessionState.Error;
                            sessionContext.LastException = new InvalidOperationException($"Unknown state: {sessionContext.CurrentState}");
                            continue;
                        }

                        var nextState = await handler(this, sessionContext, context, messageSink.SendAsync, sessionToken).ConfigureAwait(false);

                        InternalLogger.Info($"ChatSessionOrchestrator: State {sessionContext.CurrentState} -> {nextState}");

                        if (nextState == ChatSessionState.Terminated)
                            terminalState = sessionContext.CurrentState;

                        sessionContext.CurrentState = nextState;
                    }
                    catch (OperationCanceledException)
                    {
                        InternalLogger.Info("ChatSessionOrchestrator: Session cancelled by user");
                        sessionContext.CurrentState = ChatSessionState.Cancelled;
                    }
                    catch (Exception ex)
                    {
                        // The error file gets it once, with the stack, from "Session terminated with error" below.
                        InternalLogger.Warn($"ChatSessionOrchestrator: Unhandled exception in state handler {sessionContext.CurrentState}");
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

                rateLimitNotifier.Complete();
                await rateLimitDelivery.ConfigureAwait(false);

                if (sessionContext.LastException != null && !(sessionContext.LastException is ToolLoopHaltedException))
                {
                    InternalLogger.Error(
                        "ChatSessionOrchestrator: Session terminated with error " +
                        $"(model={context?.ModelId}, round={sessionContext.RoundNumber}, session={sessionContext.SessionId}, request={sessionContext.RequestId})",
                        sessionContext.LastException);
                }

                requestStopwatch.Stop();

                await _usageWriter
                    .RecordAsync(sessionContext, terminalState, requestStopwatch.ElapsedMilliseconds, context?.ModelId)
                    .ConfigureAwait(false);

                usageScope?.Dispose();
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
                context.RateLimitWaitSink,
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
        /// Maps a rate-limit wait to its UI message and hands it to the bounded notifier.
        /// </summary>
        private static Task OnRateLimitWaitAsync(BoundedMessageNotifier notifier, RateLimitWaitInfo info)
        {
            notifier.Notify(new WebView2RateLimitWaitMessage
            {
                WaitId = info.WaitId,
                Attempt = info.Attempt,
                MaxAttempts = info.MaxAttempts,
                DelaySeconds = info.Delay.TotalSeconds,
                Source = DescribeSource(info.Source),
                Phase = info.Phase == RateLimitWaitPhase.Begin ? "Begin" : "End"
            });

            return Task.CompletedTask;
        }

        /// <summary>
        /// Wire name of a delay source.
        /// </summary>
        private static string DescribeSource(RetryAfterSource source)
        {
            switch (source)
            {
                case RetryAfterSource.Header:
                    return "header";
                case RetryAfterSource.Body:
                    return "body";
                default:
                    return "fallback";
            }
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
                InternalLogger.Info("ChatSessionOrchestrator: Generation completed with null result");
                context.LastException = new InvalidOperationException("LastResult is null after generation");
                return ChatSessionState.Error;
            }

            var overflowState = await HandleContextOverflowAsync(context, generateContext, onMessage, ct).ConfigureAwait(false);
            if (overflowState.HasValue)
                return overflowState.Value;

            if (!string.IsNullOrEmpty(context.LastResult.ErrorMessage))
            {
                InternalLogger.Info($"ChatSessionOrchestrator: Generation completed with error: {context.LastResult.ErrorMessage}");
                context.LastException = new InvalidOperationException(context.LastResult.ErrorMessage);
                return ChatSessionState.Error;
            }

            if (context.LastResult.WasCancelled)
            {
                InternalLogger.Info("ChatSessionOrchestrator: Generation was cancelled by user");
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
                    _activeModelContext.MaxContextLength > 0 ? _activeModelContext.MaxContextLength : (int?)null))
            {
                return null;
            }

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

            if (compactedHistory?.Count > 0)
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

            if (previousTools != null && ToolCallLoopDetector.AreSameToolCalls(currentTools, previousTools))
            {
                context.ConsecutiveDuplicateToolRounds++;

                if (context.ConsecutiveDuplicateToolRounds >= MAX_DUPLICATE_TOOL_ROUNDS)
                {
                    var names = string.Join(", ", currentTools.Select(t => t.FunctionName));

                    int identicalRounds = context.ConsecutiveDuplicateToolRounds + 1;

                    InternalLogger.Warn(
                        "ChatSessionOrchestrator: Tool call loop detected. " +
                        $"{identicalRounds} identical tool rounds in a row [{names}]. Halting.");

                    context.LastException = new ToolLoopHaltedException(
                        "Execution halted: the model repeated its previous tool round with identical arguments " +
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

            var usageContext = BuildUsageContext(context);

            var results = await _toolRoundRunner
                .RunAsync(context.LastResult.ToolCalls, usageContext, onMessage, ct)
                .ConfigureAwait(false);

            context.ToolResultsForNextRound.AddRange(results);

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
        /// Completing state: Sends ChatSessionComplete message with metadata.
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
                    var usage = new UsageAccumulator();
                    usage.Add(context.AllResults);

                    int? Prompt = usage.Prompt;
                    int? Completion = usage.Completion;
                    int? Cached = usage.Cached;

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
        /// Context handed to the tools of this request, so a subagent or a compaction writes its usage under the
        /// session and the request of the main chat.
        /// </summary>
        private UsageCallContext BuildUsageContext(SessionStateContext context)
        {
            return UsageCallContext.ForChat(context.SessionId, context.RequestId);
        }

        /// <summary>
        /// Id of the live chat session in the format of the chat log.
        /// </summary>
        private string CurrentSessionId() => UsageIds.SessionId(_history.CurrentSessionId);

        /// <summary>
        /// Fixes the session and title of the request right before its first round and opens the usage scope for it.
        /// </summary>
        private UsageRequestScope.Scope BeginUsageScope(SessionStateContext sessionContext, GenerateStreamContext context)
        {
            sessionContext.SessionId = CurrentSessionId();
            sessionContext.Title = BuildSessionTitle(context?.Prompt);

            return UsageRequestScope.Begin(BuildUsageContext(sessionContext));
        }

        /// <summary>
        /// Session title for the stats dialog: the first user message of the session, cut at
        /// <see cref="SessionTitleLength"/> characters.
        /// </summary>
        private string BuildSessionTitle(string requestPrompt)
        {
            try
            {
                var history = _history.GetHistoryCopy();

                if (history != null)
                {
                    foreach (var message in history)
                    {
                        if (message == null || !string.Equals(message.Role, "user", StringComparison.Ordinal))
                            continue;

                        string title = CompactTitle(ContentTextExtractor.ExtractTextContent(message.Content));

                        if (!string.IsNullOrEmpty(title))
                            return title;
                    }
                }

                return CompactTitle(requestPrompt);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("ChatSessionOrchestrator: failed to build the session title", ex);
            }

            return null;
        }

        /// <summary>
        /// Collapses the whitespace of a message into single spaces and cuts it at the title length.
        /// </summary>
        private static string CompactTitle(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var builder = new StringBuilder(SessionTitleLength);
            bool pendingSpace = false;

            foreach (char symbol in text)
            {
                if (char.IsWhiteSpace(symbol))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }

                builder.Append(symbol);

                if (builder.Length >= SessionTitleLength)
                    break;
            }

            return builder.ToString();
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
            SessionStateContext context,
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
            catch (OperationCanceledException)
            {
                InternalLogger.Info("ChatSessionOrchestrator: History compaction cancelled");
            }
            catch (Exception ex)
            {
                InternalLogger.Error("ChatSessionOrchestrator: History compaction failed", ex);
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

        /// <summary>Halt of a model that repeats one tool round: shown to the user, kept out of the error file.</summary>
        private sealed class ToolLoopHaltedException : InvalidOperationException
        {
            public ToolLoopHaltedException(string message) : base(message)
            {
            }
        }
    }
}
