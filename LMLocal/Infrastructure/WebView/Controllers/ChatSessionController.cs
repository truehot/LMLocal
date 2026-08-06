using System;
using System.Linq;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.WebView.Models;

namespace LMLocal.Infrastructure.WebView.Controllers
{
    /// <summary>
    /// Bridge class for communication between WebView2 and backend chat session logic.
    /// </summary>
    public interface IChatSessionController
    {
        Task<string> GetLastChatSessionAsync();
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
        /// Returns the last persisted chat session.
        /// </summary>
        public async Task<string> GetLastChatSessionAsync()
        {
            try
            {
                var messages = await _chatHistoryManager.LoadLastSessionAsync().ConfigureAwait(false);
                var response = new GetLastChatSessionResponse
                {
                    HasSession = messages.Count > 0,
                    Messages = messages.Select(m => new ChatMessageResponse
                    {
                        Role = m.Role,
                        Content = m.Content,
                        ToolCallId = m.ToolCallId,
                        ToolCalls = m.ToolCalls
                    }).ToList()
                };
                return response.ToJson();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetLastChatSessionAsync failed", ex);
                return new GetLastChatSessionResponse().ToJson();
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
                var response = new GetLastChatSessionResponse
                {
                    HasSession = messages.Count > 0,
                    Messages = messages.Select(m => new ChatMessageResponse
                    {
                        Role = m.Role,
                        Content = m.Content,
                        ToolCallId = m.ToolCallId,
                        ToolCalls = m.ToolCalls
                    }).ToList()
                };
                return response.ToJson();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetChatSessionByIdAsync failed", ex);
                return new GetLastChatSessionResponse().ToJson();
            }
        }
    }
}
