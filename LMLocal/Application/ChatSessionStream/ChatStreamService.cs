using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.Settings;


namespace LMLocal.Application.ChatSessionStream
{
    /// <summary>
    /// Manages streaming generation of chat responses from the provider.
    /// </summary>
    internal interface IChatStreamService
    {
        /// <summary>
        /// Generates a response from the provider, with optional tool execution results for follow-up rounds.
        /// </summary>
        Task GenerateStreamAsync(
            GenerateStreamContext context,
            List<ToolResultMessage> toolResults,
            Func<TextStreamChunk, TokenGenerationStats, Task> onChunk,
            Func<StreamCompletionResult, Task> onComplete,
            CancellationToken cancellationToken);
    }

    internal class ChatStreamService : IChatStreamService
    {
        private readonly IOpenApiAdapter _openApiAdapter;
        private readonly IChatHistoryManager _history;
        private readonly ISettingsManager _settingsManager;
        private readonly IStreamProcessorFactory _streamProcessorFactory;

        private readonly SemaphoreSlim _requestLock = new SemaphoreSlim(1, 1);

        public ChatStreamService(
            IOpenApiAdapter openApiAdapter,
            IChatHistoryManager history,
            ISettingsManager settingsManager,
            IStreamProcessorFactory streamProcessorFactory)
        {
            _openApiAdapter = openApiAdapter ?? throw new ArgumentNullException(nameof(openApiAdapter));
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _streamProcessorFactory = streamProcessorFactory ?? throw new ArgumentNullException(nameof(streamProcessorFactory));
        }

        public async Task GenerateStreamAsync(
            GenerateStreamContext context,
            List<ToolResultMessage> toolResults,
            Func<TextStreamChunk, TokenGenerationStats, Task> onChunk,
            Func<StreamCompletionResult, Task> onComplete,
            CancellationToken cancellationToken)
        {
            await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            CancellationTokenSource linkedCts = null;

            try
            {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                bool isToolRound = toolResults != null && toolResults.Count > 0;

                if (!isToolRound)
                {
                    _history.EnsureHistoryNormalized();
                }

                if (isToolRound)
                {
                    var pendingToolMessages = new List<ChatMessage>(toolResults.Count);
                    foreach (var toolResult in toolResults)
                    {
                        string toolContent;
                        if (toolResult.Result == null)
                        {
                            toolContent = "";
                        }
                        else if (toolResult.Result is string str)
                        {
                            toolContent = str;
                        }
                        else
                        {
                            toolContent = toolResult.Result.ToJson();
                        }

                        var chatMessage = new ChatMessage("tool", toolContent, toolResult.ToolCallId.ToString());
                        pendingToolMessages.Add(chatMessage);
                    }

                    _history.AddToolExecutionResultMessages(pendingToolMessages);
                }
                else
                {
                    _history.AddUserMessage(context.Prompt, context.ActiveDocumentContent);
                }

                var messages = _history.BuildUserMessagesWithHistory(context.AdditionalPrompt);

                if (_settingsManager.Current?.EnableHistoryCompression ?? false)
                    messages = ChatHistoryNormalizer.NormalizeMessages(messages);

                var processor = _streamProcessorFactory.Create(linkedCts);

                var messageContext = new MessageContext(messages);
                var modelContext = new ModelContext(context.ModelId, temperature: context.Temperature);

                StreamCompletionResult result;
                using (var streaming = await _openApiAdapter.SendChatStreamingAsync(messageContext, modelContext, linkedCts.Token).ConfigureAwait(false))
                {
                    result = await processor.ProcessStreamAsync(
                        streaming.Stream,
                        linkedCts.Token,
                        onChunk,
                        _settingsManager.BatchIntervalMs).ConfigureAwait(false);
                }

                if (!result.WasCancelled && string.IsNullOrEmpty(result.ErrorMessage))
                {
                    if (IsGenerationIncomplete(result.FinishReason))
                    {
                        result.ToolCalls = Array.Empty<ToolCallRecord>();
                        result.ErrorMessage = string.Equals(result.FinishReason, "content_filter", StringComparison.OrdinalIgnoreCase)
                            ? "\n\nResponse blocked by content filter."
                            : "\n\nResponse truncated — token limit reached.";

                        _history.AddAssistantMessage(result.ContentResponse, null);
                    }
                    else
                    {
                        bool hasToolCalls = result.ToolCalls != null && result.ToolCalls.Count > 0;
                        if (hasToolCalls)
                        {
                            _history.SetPendingAssistant(result.ContentResponse, result.ToolCalls);
                        }
                        else
                        {
                            _history.AddAssistantMessage(result.ContentResponse, result.ToolCalls);
                        }
                    }
                }

                if (onComplete != null)
                {
                    await onComplete(result).ConfigureAwait(false);
                }
            }

            finally
            {
                linkedCts?.Dispose();
                _requestLock.Release();
            }
        }

        /// <summary>
        /// True when the finish reason indicates the generation did not complete normally.
        /// </summary>
        private bool IsGenerationIncomplete(string finishReason)
        {
            if (string.IsNullOrEmpty(finishReason))
                return false;

            var msg = finishReason.ToLower();
            return msg == "length" || msg == "content_filter";
        }
    }
}
