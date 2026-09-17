using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.WebView.Models;

namespace LMLocal.Infrastructure.WebView.Controllers
{
    /// <summary>
    /// Bridge class for communication between WebView2 and backend chat session logic.
    /// </summary>
    public interface IChatSessionController
    {
        Task<string> GetLastChatSessionAsync();
        Task<string> GetCurrentChatHistoryAsync();
        Task<string> GetChatSessionsAsync();
        Task<string> GetChatSessionByIdAsync(string sessionId);
    }

    [System.Runtime.InteropServices.ComVisible(true)]
    public class ChatSessionController : IChatSessionController
    {
        private readonly IChatHistoryManager _chatHistoryManager;

        internal ChatSessionController(IChatHistoryManager chatHistoryManager)
        {
            _chatHistoryManager = chatHistoryManager ?? throw new ArgumentNullException(nameof(chatHistoryManager));
        }

        /// <summary>
        /// Returns the last session persisted on disk. Used for startup auto-load.
        /// </summary>
        public async Task<string> GetLastChatSessionAsync()
        {
            try
            {
                var messages = await _chatHistoryManager.LoadLastSessionAsync().ConfigureAwait(false);
                return BuildSessionResponse(messages);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetLastChatSessionAsync failed", ex);
                return new GetLastChatSessionResponse().ToJson();
            }
        }

        /// <summary>
        /// Returns the current in-memory chat history — the source of truth for the active session.
        /// </summary>
        public Task<string> GetCurrentChatHistoryAsync()
        {
            try
            {
                var messages = _chatHistoryManager.GetHistoryCopy();
                return Task.FromResult(BuildSessionResponse(messages));
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetCurrentChatHistoryAsync failed", ex);
                return Task.FromResult(new GetLastChatSessionResponse().ToJson());
            }
        }

        /// <summary>
        /// Returns lightweight summaries of recent chat sessions (up to the configured session list limit).
        /// </summary>
        public async Task<string> GetChatSessionsAsync()
        {
            try
            {
                var sessions = await _chatHistoryManager.GetChatSessionsAsync().ConfigureAwait(false);
                var response = new ChatSessionsResponse
                {
                    Sessions = sessions.Select(s => new ChatSessionSummaryResponse
                    {
                        SessionId = s.SessionId,
                        Prompt = s.Prompt,
                        Timestamp = s.Timestamp,
                        MessageCount = s.MessageCount
                    }).ToList()
                };
                return response.ToJson();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetChatSessionsAsync failed", ex);
                return new ChatSessionsResponse().ToJson();
            }
        }

        /// <summary>
        /// Returns all messages for a specific session by ID.
        /// </summary>
        public async Task<string> GetChatSessionByIdAsync(string sessionId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sessionId))
                    return new GetLastChatSessionResponse().ToJson();

                var messages = await _chatHistoryManager.LoadSessionByIdAsync(sessionId).ConfigureAwait(false);
                return BuildSessionResponse(messages);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetChatSessionByIdAsync failed", ex);
                return new GetLastChatSessionResponse().ToJson();
            }
        }

        /// <summary>
        /// Maps chat messages into the shared session JSON view-model used by the frontend.
        /// </summary>
        private static string BuildSessionResponse(IReadOnlyList<ChatMessage> messages)
        {
            var list = messages ?? (IReadOnlyList<ChatMessage>)Array.Empty<ChatMessage>();
            var response = new GetLastChatSessionResponse
            {
                HasSession = list.Count > 0,
                Messages = list.Select(m => new ChatMessageResponse
                {
                    Role = m.Role,
                    Content = m.Content,
                    ToolCallId = m.ToolCallId,
                    ToolCalls = m.ToolCalls
                }).ToList()
            };
            return response.ToJson();
        }
    }
}
