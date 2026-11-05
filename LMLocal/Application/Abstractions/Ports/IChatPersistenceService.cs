using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;

namespace LMLocal.Application.Abstractions.Ports
{
    /// <summary>
    /// Saves and loads chat messages to/from local JSON Lines files.
    /// </summary>
    public interface IChatPersistenceService
    {
        /// <summary>
        /// Appends a single chat message line (JSON) for the current session.
        /// </summary>
        Task SaveLastMessageAsync(ChatMessage message, CancellationToken cancellationToken = default);

        /// <summary>
        /// Appends multiple chat message lines in a single write, acquiring the write semaphore once.
        /// </summary>
        Task SaveMessagesAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes a session_start marker to the current jsonl file, establishing a new session boundary
        /// </summary>
        Task MarkNewSessionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts a new session and writes its session_start marker together with <paramref name="messages"/> as a single append.
        /// </summary>

        Task<bool> StartSessionWithMessagesAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);

        /// <summary>
        /// Scans all jsonl chat files, finds the most recent session_start marker, and returns all messages belonging to that session in chronological order.
        /// </summary>
        Task<List<ChatMessage>> LoadLastSessionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Scans jsonl chat files and returns lightweight summaries of the last <paramref name="limit"/> sessions, each containing the first user message (truncated), timestamp, and message count.
        /// </summary>
        Task<List<ChatSessionSummary>> GetChatSessionsAsync(int limit = ChatLogConstants.DefaultSessionListLimit, CancellationToken cancellationToken = default);

        /// <summary>
        /// Scans jsonl chat files for a specific session by ID, returns all its messages in chronological order, and makes it the current session for subsequent saves.
        /// </summary>
        Task<List<ChatMessage>> LoadSessionByIdAsync(string sessionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Id of the session currently being written or loaded (the live session). Read-only; querying it never mutates state.
        /// </summary>
        Guid? CurrentSessionId { get; }

        /// <summary>
        /// Read-only single-pass scan of the newest jsonl files that returns every session's messages in chronological order, each paired with its UTC timestamp.
        /// </summary>
        Task<List<ChatSessionLog>> ReadAllSessionsAsync(CancellationToken cancellationToken = default);
    }
}
