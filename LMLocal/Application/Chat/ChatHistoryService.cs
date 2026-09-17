using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.ChatSession;
using LMLocal.Core.Common;
using LMLocal.Core.Models;

namespace LMLocal.Application.Chat
{
    /// <summary>
    /// Application-level use cases that manipulate the current chat history/session:
    /// resetting the history and summarizing+compacting it.
    /// </summary>
    internal interface IChatHistoryService
    {
        /// <summary>
        /// Resets the current chat history using the given action ("none", "last-prompt", "last-exchange").
        /// </summary>
        Task<bool> ResetHistoryAsync(string action);

        /// <summary>
        /// Summarizes the current history via the given model and replaces it with a compact summary pair.
        /// </summary>
        Task<bool> SummarizeAndCompactAsync(string modelId);
    }

    internal class ChatHistoryService : IChatHistoryService
    {
        private readonly IChatHistoryManager _chatHistoryManager;
        private readonly IHistoryCompactor _historyCompactor;
        private readonly ISessionManager _sessionManager;

        public ChatHistoryService(
            IChatHistoryManager chatHistoryManager,
            IHistoryCompactor historyCompactor,
            ISessionManager sessionManager)
        {
            _chatHistoryManager = chatHistoryManager ?? throw new ArgumentNullException(nameof(chatHistoryManager));
            _historyCompactor = historyCompactor ?? throw new ArgumentNullException(nameof(historyCompactor));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        }

        /// <summary>
        /// Resets the chat history with the specified action.
        /// </summary>
        public async Task<bool> ResetHistoryAsync(string action)
        {
            try
            {
                if (_sessionManager.IsSessionRunning)
                {
                    InternalLogger.Info("ResetHistoryAsync: Cannot reset while session is running");
                    return false;
                }

                switch (action)
                {
                    case "last-prompt":
                        await _chatHistoryManager.MoveLastExchangeToNewSessionAsync().ConfigureAwait(false);
                        InternalLogger.Info("ResetHistoryAsync: Moved last exchange to new session");
                        break;
                    case "last-exchange":
                        await _chatHistoryManager.ConsolidateLastExchangeAsync().ConfigureAwait(false);
                        InternalLogger.Info("ResetHistoryAsync: Consolidated last exchange");
                        break;
                    default:
                        _chatHistoryManager.Clear();
                        InternalLogger.Info("ResetHistoryAsync: History cleared successfully");
                        break;
                }

                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("ResetHistoryAsync failed", ex);
                return false;
            }
        }

        /// <summary>
        /// Summarizes the current chat history via LLM, then replaces it with a compact user instruction + summary assistant pair.
        /// </summary>
        public async Task<bool> SummarizeAndCompactAsync(string modelId)
        {
            try
            {
                if (_sessionManager.IsSessionRunning)
                {
                    InternalLogger.Info("SummarizeAndCompactAsync: Cannot run while session is active");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(modelId))
                {
                    InternalLogger.Info("SummarizeAndCompactAsync: No active model");
                    return false;
                }

                var snapshot = _chatHistoryManager.GetHistoryCopy();
                if (snapshot.Count == 0)
                {
                    _chatHistoryManager.Clear();
                    return true;
                }

                var summary = await _historyCompactor.SummarizeAsync(snapshot, modelId, CancellationToken.None).ConfigureAwait(false);

                var summaryMessages = new List<ChatMessage>();
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    summaryMessages.Add(new ChatMessage("user", "Provide a brief summary of our previous session to continue."));
                    summaryMessages.Add(new ChatMessage("assistant", summary));
                }

                await _chatHistoryManager.ClearAndSaveMessagesAsync(summaryMessages).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(summary))
                {
                    InternalLogger.Info("SummarizeAndCompactAsync: History summarized and compacted");
                }
                else
                {
                    InternalLogger.Warn("SummarizeAndCompactAsync: Summarization failed, history cleared");
                }

                return !string.IsNullOrWhiteSpace(summary);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("SummarizeAndCompactAsync failed", ex);
                _chatHistoryManager.Clear();
                return false;
            }
        }
    }
}
