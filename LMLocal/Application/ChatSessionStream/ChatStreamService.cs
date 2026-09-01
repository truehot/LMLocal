using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Core.Models;


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
        private readonly IStreamingRoundService _roundService;
        private readonly IChatHistoryManager _history;
        private readonly SemaphoreSlim _requestLock = new SemaphoreSlim(1, 1);

        public ChatStreamService(IStreamingRoundService roundService, IChatHistoryManager history)
        {
            _roundService = roundService ?? throw new ArgumentNullException(nameof(roundService));
            _history = history ?? throw new ArgumentNullException(nameof(history));
        }

        public async Task GenerateStreamAsync(
            GenerateStreamContext context,
            List<ToolResultMessage> toolResults,
            Func<TextStreamChunk, TokenGenerationStats, Task> onChunk,
            Func<StreamCompletionResult, Task> onComplete,
            CancellationToken cancellationToken)
        {

            await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var request = new StreamRoundRequest
                {
                    History = _history,
                    IsToolRound = toolResults != null && toolResults.Count > 0,
                    ToolResults = toolResults,
                    Prompt = context.Prompt,
                    ActiveDocumentContent = context.ActiveDocumentContent,
                    Images = context.Images,
                    SystemPrompt = context.AdditionalPrompt,
                    ModelId = context.ModelId,
                    Temperature = context.Temperature,
                    OnChunk = onChunk
                };

                var roundResult = await _roundService.RunAsync(request, cancellationToken).ConfigureAwait(false);

                if (onComplete != null)
                {
                    await onComplete(roundResult.Stream).ConfigureAwait(false);
                }
            }
            finally
            {
                _requestLock.Release();
            }
        }
    }
}
