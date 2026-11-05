using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Chat;
using LMLocal.Application.Tool;
using LMLocal.Core.Common;
using LMLocal.Core.Exceptions;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Responses;
using LMLocal.Infrastructure.ModelsConfig;

namespace LMLocal.Application.ChatSessionStream
{
    /// <summary>
    /// Result of a single generation round, classified for the caller.
    /// </summary>
    internal enum StreamRoundOutcome
    {
        /// <summary>Stream reported an error (ErrorMessage is set). No assistant commit.</summary>
        Error,

        /// <summary>Generation was cancelled by the user (WasCancelled). No assistant commit.</summary>
        Cancelled,

        /// <summary>Generation was incomplete (length / content_filter). Assistant committed as-is, error set.</summary>
        Incomplete,

        /// <summary>Model requested tool calls. Assistant committed as pending (SetPendingAssistant).</summary>
        ToolCalls,

        /// <summary>Normal completion. Assistant committed as a final message.</summary>
        Completed
    }

    /// <summary>
    /// Everything a single generation round needs.
    /// </summary>
    internal class StreamRoundRequest
    {
        /// <summary>History the round reads/writes (main chat: singleton; SubAgent: per-run). Required.</summary>
        public IChatHistoryManager History { get; set; }

        /// <summary>True when this round answers tool results, false for a new user round.</summary>
        public bool IsToolRound { get; set; }

        /// <summary>True when this round retries a request that failed after a forced compaction of the history.</summary>
        public bool IsContextOverflowRetry { get; set; }

        /// <summary>Tool results for a tool round.</summary>
        public List<ToolResultMessage> ToolResults { get; set; }

        /// <summary>User prompt for a user round.</summary>
        public string Prompt { get; set; }

        /// <summary>Active document content attached to the user message. Optional.</summary>
        public string ActiveDocumentContent { get; set; }

        /// <summary>Optional base64 images attached to the user message.</summary>
        public IReadOnlyList<string> Images { get; set; }

        /// <summary> System prompt for model.</summary>
        public string SystemPrompt { get; set; }

        /// <summary>Model id for the request.</summary>
        public string ModelId { get; set; }

        /// <summary>Sampling temperature. Optional.</summary>
        public double? Temperature { get; set; }

        /// <summary>Max output tokens. Optional.</summary>
        public int? MaxOutputTokens { get; set; }

        /// <summary>Reasoning effort override ("none"/"low"/"medium"/"high"). Optional.</summary>
        public string ReasoningEffort { get; set; }

        /// <summary> Explicit provider connection.</summary>
        public ProviderContext Provider { get; set; }

        /// <summary>Restricted tool set for the SubAgent overload.</summary>
        public IReadOnlyList<ToolDefinition> Tools { get; set; }

        /// <summary>Optional streaming callback (chunks/token stats).</summary>
        public Func<TextStreamChunk, TokenGenerationStats, Task> OnChunk { get; set; }

        /// <summary>Calibration of the conversation this round belongs to. Optional.</summary>
        public IContextCalibration Calibration { get; set; }
    }

    /// <summary>Outcome of a round plus the raw stream result.</summary>
    internal class StreamRoundResult
    {
        public StreamRoundOutcome Outcome { get; set; }
        public StreamCompletionResult Stream { get; set; }

        /// <summary>
        /// Estimated size of the request envelope this round sent, before calibration. Zero when unknown.
        /// </summary>
        public int EnvelopeEstimate { get; set; }
    }

    /// <summary>
    /// Runs one LLM generation round: prepares history, builds the request, streams via the adapter, parses with StreamProcessor and classifies/commits the assistant message.
    /// </summary>
    internal interface IStreamingRoundService
    {
        Task<StreamRoundResult> RunAsync(StreamRoundRequest request, CancellationToken cancellationToken);
    }

    internal class StreamingRoundService : IStreamingRoundService
    {
        private readonly IOpenApiAdapter _openApiAdapter;
        private readonly ISettingsManager _settingsManager;
        private readonly IStreamProcessorFactory _streamProcessorFactory;
        private readonly IModelsConfigManager _modelsConfigManager;
        private readonly IToolQueueProvider _toolQueueProvider;

        public StreamingRoundService(
            IOpenApiAdapter openApiAdapter,
            ISettingsManager settingsManager,
            IStreamProcessorFactory streamProcessorFactory,
            IModelsConfigManager modelsConfigManager,
            IToolQueueProvider toolQueueProvider = null)
        {
            _openApiAdapter = openApiAdapter ?? throw new ArgumentNullException(nameof(openApiAdapter));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _streamProcessorFactory = streamProcessorFactory ?? throw new ArgumentNullException(nameof(streamProcessorFactory));
            _modelsConfigManager = modelsConfigManager ?? throw new ArgumentNullException(nameof(modelsConfigManager));
            _toolQueueProvider = toolQueueProvider;
        }

        public async Task<StreamRoundResult> RunAsync(
            StreamRoundRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.History == null) throw new ArgumentNullException(nameof(request.History));

            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var history = request.History;
                bool isToolRound = request.IsToolRound;

                if (!isToolRound && !request.IsContextOverflowRetry)
                {
                    history.EnsureHistoryNormalized();
                }

                if (isToolRound)
                {
                    var pendingToolMessages = new List<ChatMessage>(request.ToolResults?.Count ?? 0);
                    if (request.ToolResults != null)
                    {
                        foreach (var toolResult in request.ToolResults)
                        {
                            pendingToolMessages.Add(ToolMessageFactory.CreateFromToolResult(toolResult));
                        }
                    }

                    history.AddToolExecutionResultMessages(pendingToolMessages);
                }
                else if (!request.IsContextOverflowRetry)
                {
                    history.AddUserMessage(request.Prompt, request.ActiveDocumentContent, request.Images);
                }

                var messages = history.BuildUserMessagesWithHistory(request.SystemPrompt);

                var processor = _streamProcessorFactory.Create(linkedCts);

                var messageContext = new MessageContext(messages);
                var modelProfile = await GetModelProfileAsync(request.ModelId, linkedCts.Token).ConfigureAwait(false);
                var modelContext = new ModelContext(
                    request.ModelId,
                    temperature: request.Temperature,
                    maxOutputTokens: request.MaxOutputTokens ?? modelProfile?.MaxTokens,
                    contextLength: modelProfile?.ContextLength,
                    reasoning: request.ReasoningEffort ?? modelProfile?.ReasoningEffort);

                int envelopeEstimate = EnvelopeEstimator.Estimate(messages, ResolveToolsForEstimate(request));
                int fixedOverhead = Math.Max(0, envelopeEstimate - EnvelopeEstimator.Estimate(WithoutSystemMessage(messages), null));
                request.Calibration?.RecordEnvelope(envelopeEstimate, fixedOverhead);

                StreamCompletionResult result;
                try
                {
                    using (var streaming = await SendStreamingAsync(messageContext, modelContext, request, linkedCts.Token).ConfigureAwait(false))
                    {
                        result = await processor.ProcessStreamAsync(
                            streaming.Stream,
                            linkedCts.Token,
                            request.OnChunk,
                            _settingsManager.BatchIntervalMs).ConfigureAwait(false);
                    }
                }
                catch (ApiException ex) when (!linkedCts.Token.IsCancellationRequested)
                {
                    request.Calibration?.RecordOverflowError(ex.ErrorInfo?.PromptTokens, ex.ErrorInfo?.ContextLength, envelopeEstimate);

                    return new StreamRoundResult
                    {
                        Outcome = StreamRoundOutcome.Error,
                        Stream = BuildErrorResult(ex),
                        EnvelopeEstimate = envelopeEstimate
                    };
                }

                request.Calibration?.RecordSuccessfulRound(result?.TokenUsage?.PromptTokens, envelopeEstimate);

                var classified = ClassifyAndCommit(history, result);
                classified.EnvelopeEstimate = envelopeEstimate;
                return classified;
            }
        }

        /// <summary>
        /// Copies the message list without its system message, so the non-history part of the envelope can be measured.
        /// </summary>
        private static List<ChatMessage> WithoutSystemMessage(IReadOnlyList<ChatMessage> messages)
        {
            var rest = new List<ChatMessage>(messages?.Count ?? 0);
            if (messages == null)
                return rest;

            foreach (var message in messages)
            {
                if (message != null && message.Role != "system")
                    rest.Add(message);
            }

            return rest;
        }

        /// <summary>
        /// Maps an API exception to a stream result carrying the structured error fields.
        /// </summary>
        private static StreamCompletionResult BuildErrorResult(ApiException ex)
        {
            var errorInfo = ex.ErrorInfo;

            return new StreamCompletionResult
            {
                ErrorMessage = !string.IsNullOrEmpty(errorInfo?.Message) ? errorInfo.Message : ex.Message,
                ErrorCode = !string.IsNullOrEmpty(errorInfo?.RawCode) ? errorInfo.RawCode : errorInfo?.Code?.ToString(),
                ErrorType = errorInfo?.Type,
                FinishReason = "error",
                HttpStatusCode = ex.HttpStatusCode
            };
        }

        /// <summary>
        /// Tools that travel with the request: the explicit set of a SubAgent round, or the main chat queue that the
        /// request builder injects for the chat.
        /// </summary>
        private IReadOnlyList<ToolDefinition> ResolveToolsForEstimate(StreamRoundRequest request)
        {
            if (request.Tools != null)
                return request.Tools;

            if (request.Provider != null)
                return null;

            var settings = _settingsManager?.Current;
            if (settings == null || !settings.EnableAiTools)
                return null;

            return _toolQueueProvider?.GetMainQueue()?.Definitions;
        }

        /// <summary>
        /// Resolves the user-defined model profile (json) for the given model id, matching the currently selected provider type and provider profile id.
        /// </summary>
        private async Task<ModelDefinition> GetModelProfileAsync(string modelId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(modelId))
                return null;

            try
            {
                var config = await _modelsConfigManager.GetAsync(cancellationToken).ConfigureAwait(false);
                var current = _settingsManager.Current;

                return ModelProfileLookup.Find(config, modelId, current?.Provider, current?.ProviderId);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetModelProfileAsync failed: " + ex.Message, ex);
                return null;
            }
        }

        private Task<StreamingResponse> SendStreamingAsync(
            MessageContext messageContext,
            ModelContext modelContext,
            StreamRoundRequest request,
            CancellationToken cancellationToken)
        {
            if (request.Provider != null)
            {
                return _openApiAdapter.SendChatStreamingAsync(
                    messageContext,
                    modelContext,
                    request.Provider,
                    request.Tools,
                    cancellationToken);
            }

            return _openApiAdapter.SendChatStreamingAsync(messageContext, modelContext, cancellationToken);
        }

        /// <summary>
        /// Classifies the stream result and commits the assistant message to history.
        /// </summary>
        private static StreamRoundResult ClassifyAndCommit(IChatHistoryManager history, StreamCompletionResult result)
        {
            if (result.WasCancelled)
            {
                return new StreamRoundResult { Outcome = StreamRoundOutcome.Cancelled, Stream = result };
            }

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                return new StreamRoundResult { Outcome = StreamRoundOutcome.Error, Stream = result };
            }

            if (IsGenerationIncomplete(result.FinishReason))
            {
                result.ToolCalls = Array.Empty<ToolCallRecord>();
                result.ErrorMessage = string.Equals(result.FinishReason, "content_filter", StringComparison.OrdinalIgnoreCase)
                    ? "\n\nResponse blocked by content filter."
                    : "\n\nResponse truncated — token limit reached.";

                history.AddAssistantMessage(result.ContentResponse, null);
                return new StreamRoundResult { Outcome = StreamRoundOutcome.Incomplete, Stream = result };
            }

            bool hasToolCalls = result.ToolCalls != null && result.ToolCalls.Count > 0;
            if (hasToolCalls)
            {
                history.SetPendingAssistant(result.ContentResponse, result.ToolCalls);
                return new StreamRoundResult { Outcome = StreamRoundOutcome.ToolCalls, Stream = result };
            }

            history.AddAssistantMessage(result.ContentResponse, result.ToolCalls);
            return new StreamRoundResult { Outcome = StreamRoundOutcome.Completed, Stream = result };
        }

        /// <summary>
        /// True when the finish reason indicates the generation did not complete normally.
        /// </summary>
        private static bool IsGenerationIncomplete(string finishReason)
        {
            if (string.IsNullOrEmpty(finishReason))
                return false;

            var msg = finishReason.ToLower();
            return msg == "length" || msg == "content_filter";
        }
    }
}
