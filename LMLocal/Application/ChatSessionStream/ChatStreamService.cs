using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Api;
using LMLocal.Infrastructure.Settings;

namespace LMLocal.Application.ChatSessionStream
{
    /// <summary>
    /// Manages streaming generation of chat responses from the LM backend.
    /// </summary>
    internal interface IChatStreamService
    {
        /// <summary>
        /// Generates initial response from LLM without tool results.
        /// </summary>
        Task GenerateStreamAsync(
            GenerateStreamContext context,
            Func<TextStreamChunk, TokenGenerationStats, Task> onChunk,
            Func<StreamCompletionResult, Task> onComplete,
            CancellationToken cancellationToken);

        /// <summary>
        /// Generates response from LLM in follow-up round, providing tool execution results.
        /// </summary>
        Task GenerateWithToolResultsAsync(
            GenerateStreamContext context,
            List<ToolResultMessage> toolResults,
            Func<TextStreamChunk, TokenGenerationStats, Task> onChunk,
            Func<StreamCompletionResult, Task> onComplete,
            CancellationToken cancellationToken);

        Task<bool> ResetHistoryAsync();
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
            Func<TextStreamChunk, TokenGenerationStats, Task> onChunk,
            Func<StreamCompletionResult, Task> onComplete,
            CancellationToken cancellationToken)
        {
            await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            CancellationTokenSource linkedCts = null;

            try
            {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                var messages = _history.BuildUserMessagesWithHistory(
                    context.Prompt,
                    context.ActiveDocumentContent,
                    context.AdditionalPrompt);

                _history.AddUserMessage(context.Prompt);

                var processor = _streamProcessorFactory.Create(linkedCts);

                var messageContext = new MessageContext(messages);
                var modelContext = new ModelContext(context.ModelId, temperature: context.Temperature);

                using (var streaming = await _openApiAdapter.SendChatStreamingAsync(
                    messageContext,
                    modelContext,
                    linkedCts.Token).ConfigureAwait(false))
                {
                    var result = await processor.ProcessStreamAsync(
                        streaming.Stream,
                        linkedCts.Token,
                        onChunk,
                        _settingsManager.BatchIntervalMs).ConfigureAwait(false);

                    if (!result.WasCancelled && !linkedCts.Token.IsCancellationRequested)
                    {
                        _history.AddAssistantMessage(result.ContentResponse);
                        _history.AddAssistantToolRequestMessage(result.ToolCalls);
                    }

                    if (onComplete != null)
                    {
                        await onComplete(result).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                linkedCts?.Dispose();
                _requestLock.Release();
            }
        }

        public async Task GenerateWithToolResultsAsync(
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

                var messages = _history.BuildUserMessagesWithHistory("");

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
                    messages.Add(chatMessage);

                    _history.AddToolExecutionResultMessage(chatMessage);
                }

                var processor = _streamProcessorFactory.Create(linkedCts);

                var messageContext = new MessageContext(messages);
                var modelContext = new ModelContext(context.ModelId, temperature: context.Temperature);

                using (var streaming = await _openApiAdapter.SendChatStreamingAsync(
                    messageContext,
                    modelContext,
                    linkedCts.Token).ConfigureAwait(false))
                {
                    var result = await processor.ProcessStreamAsync(
                        streaming.Stream,
                        linkedCts.Token,
                        onChunk,
                        _settingsManager.BatchIntervalMs).ConfigureAwait(false);

                    if (!result.WasCancelled && !linkedCts.Token.IsCancellationRequested)
                    {
                        _history.AddAssistantMessage(result.ContentResponse);
                        _history.AddAssistantToolRequestMessage(result.ToolCalls);
                    }

                    if (onComplete != null)
                    {
                        await onComplete(result).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                linkedCts?.Dispose();
                _requestLock.Release();
            }
        }

        public async Task<bool> ResetHistoryAsync()
        {
            if (!await _requestLock.WaitAsync(0).ConfigureAwait(false))
                return false;

            try
            {
                _history.Clear();
            }
            finally
            {
                _requestLock.Release();
            }

            return true;
        }
    }
}
