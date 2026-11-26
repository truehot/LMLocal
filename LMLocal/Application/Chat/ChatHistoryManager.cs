using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Chat.Normalization;
using LMLocal.Core.Common;
using LMLocal.Core.Models;

namespace LMLocal.Application.Chat
{
    /// <summary>
    /// Keeps track of the chat history, including user and assistant messages, and provides methods to manipulate and retrieve the history.
    /// </summary>
    internal interface IChatHistoryManager
    {
        /// <summary>
        /// Adds a user message to history and persists it.
        /// </summary>
        void AddUserMessage(string content, string activeDocumentContent = null, IReadOnlyList<string> imageDataUrls = null);

        /// <summary>
        /// Adds an assistant message (optionally with tool calls) to history and persists it.
        /// </summary>
        void AddAssistantMessage(string content, IReadOnlyList<ToolCallRecord> toolCalls, string modelId = null);

        /// <summary>
        /// Clears all messages from history and marks a new session boundary.
        /// </summary>
        void Clear();

        /// <summary>
        /// Returns a snapshot copy of the current in-memory history.
        /// </summary>
        IReadOnlyList<ChatMessage> GetHistoryCopy();

        /// <summary>
        /// Atomically replaces history (summary + recent) only if size matches expectedSize, then persists the new session to disk.
        /// </summary>
        Task<bool> ReplaceHistoryAndPersistAsync(string summary, IEnumerable<ChatMessage> recent, int expectedSize);

        /// <summary>
        /// Replaces the in-memory history without touching persistence. The disk log keeps the full conversation.
        /// </summary>
        void ReplaceHistoryInMemory(IEnumerable<ChatMessage> messages);

        /// <summary>
        /// Builds a message list for the current provider.
        /// </summary>
        List<ChatMessage> BuildUserMessagesWithHistory(string additionalSystemPrompt = null);

        /// <summary>
        /// Sets the line with the date and time the chat started, which <see cref="BuildUserMessagesWithHistory"/>
        /// appends to the system message. Taken once per chat: a changed line would change the prompt prefix.
        /// </summary>
        void EnsureChatDateLine();

        /// <summary>
        /// Removes the assistant message an unsuccessful round committed, and drops the pending assistant state, so the retry neither sees a truncated answer nor commits its tool calls.
        /// </summary>
        void DiscardLastAssistantFromFailedRound();

        /// <summary>
        /// Replaces the in-memory history and writes it as a new session: the marker and every message go out as one append, so a restart loads the compacted history instead of the version that already overflowed.
        /// </summary>
        Task<bool> ReplaceHistoryWithNewSessionAsync(IEnumerable<ChatMessage> messages);

        /// <summary>
        /// Drops the last exchange - everything after the last user message - and replaces the history with the rest.
        /// </summary>
        Task<TruncateLastExchangeOutcome> TruncateLastExchangeAsync(bool keepUserMessage, string expectedPrompt = null);

        /// <summary>
        /// Adds tool execution result messages to history and persists them.
        /// </summary>
        void AddToolExecutionResultMessages(IEnumerable<ChatMessage> messages);

        /// <summary>
        /// Loads the last persisted session into in-memory history (if empty) and returns its messages.
        /// </summary>
        Task<List<ChatMessage>> LoadLastSessionAsync();

        /// <summary>
        /// Normalizes history for providers (e.g. LlamaCpp) that require strict user/assistant alternation and lack native tool role support.
        /// </summary>
        void EnsureHistoryNormalized();

        /// <summary>
        /// Queues an assistant with tool calls to be committed together with the next AddToolExecutionResultMessages, ensuring tool call is saved before results.
        /// </summary>
        void SetPendingAssistant(string content, IReadOnlyList<ToolCallRecord> toolCalls, string modelId = null);

        /// <summary>
        /// Drops the queued assistant tool call without committing it, so the next AddToolExecutionResultMessages call cannot resurrect a tool call that was rejected (e.g. an identical repeated call detected as a loop).
        /// </summary>
        void DiscardPendingAssistant();

        /// <summary>
        /// Reasoning text for the next assistant message committed to history (AddAssistantMessage, directly or through
        /// SetPendingAssistant); null clears it. Kept in memory only.
        /// </summary>
        void SetNextAssistantReasoning(string reasoning);

        /// <summary>
        /// Clears history and starts a new session, carrying over the last completed step 1:1 (user message + assistant reply with text + tool calls/results in between).
        /// </summary>
        Task MoveLastExchangeToNewSessionAsync();

        /// <summary>
        /// Clears history and starts a new session, consolidating the last completed step into a clean user/assistant pair.
        /// </summary>
        Task ConsolidateLastExchangeAsync();

        /// <summary>
        /// Returns lightweight summaries of recent chat sessions (last <paramref name="limit"/> sessions).
        /// </summary>
        Task<List<ChatSessionSummary>> GetChatSessionsAsync(int limit = ChatLogConstants.DefaultSessionListLimit);

        /// <summary>
        /// Loads all messages for a specific session by ID and makes it the working session (replaces in-memory history without spawning a new session boundary).
        /// </summary>
        Task<List<ChatMessage>> LoadSessionByIdAsync(string sessionId);

        /// <summary>
        /// Clears history, starts a new session, and persists the provided messages in that new session.
        /// </summary>
        Task ClearAndSaveMessagesAsync(IEnumerable<ChatMessage> messages);
        /// <summary>
        /// Id of the session that is being written or was loaded last (the live session), or null when nothing is
        /// persisted. Read-only: querying it never mutates state.
        /// </summary>
        Guid? CurrentSessionId { get; }
    }

    /// <summary>
    /// Outcome of dropping the last exchange from the history.
    /// </summary>
    internal enum TruncateLastExchangeOutcome
    {
        /// <summary>The history was replaced; the prompt can be answered again.</summary>
        Replaced,

        /// <summary>There is no user message, or it is a service message the model cannot be asked again.</summary>
        NothingToReplace,

        /// <summary>The last user message is not the prompt the UI was showing; nothing was changed.</summary>
        OutOfSync
    }

    internal class ChatHistoryManager : IChatHistoryManager
    {
        private readonly List<ChatMessage> _history = new List<ChatMessage>();
        private readonly object _lock = new object();
        private readonly IChatPersistenceService _persistence;
        private readonly ISettingsManager _settingsManager;
        private readonly IToolResultMarkdownFormatter _formatter;
        private readonly ITimeProvider _timeProvider;
        private readonly IProviderPolicy _providerPolicy;
        private const int LookbackLimit = 800;

        private readonly HistoryNormalizer _normalizer = new HistoryNormalizer();

        private string _pendingAssistantContent;
        private IReadOnlyList<ToolCallRecord> _pendingAssistantToolCalls;
        private string _pendingAssistantModelId;
        private string _nextAssistantReasoning;

        private string _chatDateLine;

        /// <summary>
        /// Creates a chat history manager backed by the given persistence service.
        /// </summary>
        public ChatHistoryManager(
            ISettingsManager settingsManager,
            IChatPersistenceService persistence,
            IProviderPolicy providerPolicy,
            IToolResultMarkdownFormatter formatter = null,
            ITimeProvider timeProvider = null)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _providerPolicy = providerPolicy ?? throw new ArgumentNullException(nameof(providerPolicy));
            _formatter = formatter;
            _timeProvider = timeProvider;
        }

        /// <summary>
        /// Id of the session that is being written or was loaded last, or null when persistence has no session yet.
        /// </summary>
        public Guid? CurrentSessionId
        {
            get { return _persistence?.CurrentSessionId; }
        }

        /// <summary>
        /// Sets the line to the current time when the chat has none yet: local servers reuse the cached prompt prefix,
        /// so the line is taken once per chat and must not change while the chat lives.
        /// </summary>
        public void EnsureChatDateLine()
        {
            if (_timeProvider == null || _chatDateLine != null)
                return;

            _chatDateLine = CurrentDateLine.Format(_timeProvider.UtcNow, _timeProvider.LocalTimeZone);
        }

        /// <summary>
        /// Adds a user message to history and persists it.
        /// </summary>
        public void AddUserMessage(string userPrompt, string activeDocumentContent = null, IReadOnlyList<string> imageDataUrls = null)
        {
            bool hasImages = imageDataUrls?.Count > 0;
            if (string.IsNullOrEmpty(userPrompt) && string.IsNullOrEmpty(activeDocumentContent) && !hasImages)
                return;

            object content;

            if (!hasImages)
            {
                string merged = userPrompt ?? "";
                if (!string.IsNullOrEmpty(activeDocumentContent))
                    merged = activeDocumentContent + "\n\n" + userPrompt;
                content = merged;
            }
            else
            {
                var parts = new List<ContentPart>();
                string text = userPrompt ?? "";
                if (!string.IsNullOrEmpty(activeDocumentContent))
                    text = activeDocumentContent + "\n\n" + text;
                if (!string.IsNullOrEmpty(text))
                    parts.Add(new ContentPart { Type = "text", Text = text });

                foreach (var dataUrl in imageDataUrls)
                {
                    parts.Add(new ContentPart
                    {
                        Type = "image_url",
                        ImageUrl = new ImageUrlInfo { Url = dataUrl, Detail = "auto" }
                    });
                }
                content = parts;
            }

            ChatMessage userMessage = new ChatMessage("user", content) { Timestamp = DateTime.UtcNow };

            lock (_lock)
            {
                _history.Add(userMessage);
            }

            _ = _persistence.SaveLastMessageAsync(userMessage, CancellationToken.None);
        }

        /// <summary>
        /// Adds an assistant message with text-only content to history and persists it.
        /// </summary>
        public void AddAssistantMessage(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;

            ChatMessage assistantMessage = new ChatMessage("assistant", content) { Timestamp = DateTime.UtcNow };

            lock (_lock)
            {
                _history.Add(assistantMessage);
            }
            _ = _persistence.SaveLastMessageAsync(assistantMessage, CancellationToken.None);
        }

        /// <summary>
        /// Adds a single assistant message that may contain both text content and tool calls.
        /// </summary>
        public void AddAssistantMessage(string content, IReadOnlyList<ToolCallRecord> toolCalls, string modelId = null)
        {
            bool hasContent = !string.IsNullOrWhiteSpace(content);
            bool hasToolCalls = toolCalls?.Count > 0;

            string reasoning = _nextAssistantReasoning;
            _nextAssistantReasoning = null;

            if (!hasContent && !hasToolCalls) return;

            List<ChatToolCall> toolCallObjects = null;
            if (hasToolCalls)
            {
                toolCallObjects = new List<ChatToolCall>(toolCalls.Count);
                foreach (var toolCall in toolCalls)
                {
                    toolCallObjects.Add(new ChatToolCall
                    {
                        Id = toolCall.CallId,
                        Name = toolCall.FunctionName,
                        Arguments = string.IsNullOrEmpty(toolCall.ArgumentsJson) ? "{}" : toolCall.ArgumentsJson,
                        ExtraContentJson = string.IsNullOrEmpty(toolCall.ExtraContentJson) ? null : toolCall.ExtraContentJson
                    });
                }
            }

            var chatMessage = new ChatMessage("assistant", hasContent ? content : null)
            {
                ToolCalls = toolCallObjects,
                ModelId = modelId,
                Timestamp = DateTime.UtcNow,
                ReasoningContent = reasoning
            };

            lock (_lock)
            {
                _history.Add(chatMessage);
            }
            _ = _persistence.SaveLastMessageAsync(chatMessage, CancellationToken.None);
        }

        /// <summary>
        /// Queues an assistant message with tool calls to be committed with the next AddToolExecutionResultMessages call.
        /// </summary>
        public void SetPendingAssistant(string content, IReadOnlyList<ToolCallRecord> toolCalls, string modelId = null)
        {
            _pendingAssistantContent = content;
            _pendingAssistantToolCalls = toolCalls;
            _pendingAssistantModelId = modelId;
        }

        /// <summary>
        /// Drops the queued assistant tool call without committing it.
        /// </summary>
        public void DiscardPendingAssistant()
        {
            _pendingAssistantContent = null;
            _pendingAssistantToolCalls = null;
            _pendingAssistantModelId = null;
            _nextAssistantReasoning = null;
        }

        public void SetNextAssistantReasoning(string reasoning)
        {
            _nextAssistantReasoning = string.IsNullOrEmpty(reasoning) ? null : reasoning;
        }

        /// <summary>
        /// Replaces the in-memory history and writes it as a new session.
        /// </summary>
        public async Task<bool> ReplaceHistoryWithNewSessionAsync(IEnumerable<ChatMessage> messages)
        {
            var replacement = messages == null
                ? new List<ChatMessage>()
                : messages.Where(m => m != null).ToList();

            _pendingAssistantContent = null;
            _pendingAssistantToolCalls = null;
            _pendingAssistantModelId = null;
            _nextAssistantReasoning = null;

            lock (_lock)
            {
                _history.Clear();
                _history.AddRange(replacement);
                InvalidateCacheLocked();
            }

            return await _persistence.StartSessionWithMessagesAsync(replacement, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Drops the last exchange and replaces the history with the remainder, so the old answer stays in the previous
        /// session in the log.
        /// </summary>
        public async Task<TruncateLastExchangeOutcome> TruncateLastExchangeAsync(bool keepUserMessage, string expectedPrompt = null)
        {
            List<ChatMessage> truncated;

            lock (_lock)
            {
                int lastUserIndex = _history.FindLastIndex(m => m?.Role == "user");
                if (lastUserIndex < 0)
                    return TruncateLastExchangeOutcome.NothingToReplace;

                var lastUser = _history[lastUserIndex];
                if (IsServiceUserMessage(lastUser))
                    return TruncateLastExchangeOutcome.NothingToReplace;

                if (!MatchesExpectedPrompt(lastUser, expectedPrompt))
                    return TruncateLastExchangeOutcome.OutOfSync;

                truncated = _history.GetRange(0, lastUserIndex + (keepUserMessage ? 1 : 0));
            }

            if (!await ReplaceHistoryWithNewSessionAsync(truncated).ConfigureAwait(false))
                InternalLogger.Warn("ChatHistoryManager: the replaced history was not written to the chat log");

            return TruncateLastExchangeOutcome.Replaced;
        }

        /// <summary>
        /// True when the user message was written by the app (summary request, compaction placeholder or the image
        /// placeholder of a loaded log), so it cannot be answered again on its own.
        /// </summary>
        private static bool IsServiceUserMessage(ChatMessage message)
        {
            var text = ContentTextExtractor.ExtractTextContent(message.Content)?.Trim();
            if (string.IsNullOrEmpty(text))
                return false;

            return text == ChatMessagePlaceholders.SummaryRequest
                || text == ChatMessagePlaceholders.ForcedSummaryUser
                || ChatMessagePlaceholders.IsImagePlaceholder(text);
        }

        /// <summary>
        /// True when the last user message really is the prompt the UI was showing.
        /// </summary>
        private static bool MatchesExpectedPrompt(ChatMessage message, string expectedPrompt)
        {
            if (expectedPrompt == null)
                return true;

            var expected = expectedPrompt.Trim();
            var text = ContentTextExtractor.ExtractTextContent(message.Content)?.Trim() ?? string.Empty;

            if (expected.Length == 0)
                return text.Length == 0;

            return string.Equals(text, expected, StringComparison.Ordinal)
                || text.EndsWith("\n\n" + expected, StringComparison.Ordinal);
        }

        /// <summary>
        /// Removes the assistant message an unsuccessful round committed, and drops the pending assistant state, so the retry neither sees a truncated answer nor commits its tool calls.
        /// </summary>
        public void DiscardLastAssistantFromFailedRound()
        {
            _pendingAssistantContent = null;
            _pendingAssistantToolCalls = null;
            _nextAssistantReasoning = null;

            lock (_lock)
            {
                if (_history.Count == 0)
                    return;

                var last = _history[_history.Count - 1];
                if (last == null || last.Role != "assistant")
                    return;

                _history.RemoveAt(_history.Count - 1);
                InvalidateCacheLocked();
            }
        }

        /// <summary>
        /// Adds tool execution result messages to history and persists them.
        /// </summary>
        public void AddToolExecutionResultMessages(IEnumerable<ChatMessage> messages)
        {
            if (_pendingAssistantToolCalls?.Count > 0)
            {
                AddAssistantMessage(_pendingAssistantContent, _pendingAssistantToolCalls, _pendingAssistantModelId);
                _pendingAssistantContent = null;
                _pendingAssistantToolCalls = null;
                _pendingAssistantModelId = null;
            }

            if (messages == null) return;

            List<ChatMessage> validMessages = null;
            foreach (var msg in messages)
            {
                if (msg != null)
                {
                    if (validMessages == null)
                        validMessages = new List<ChatMessage>();
                    validMessages.Add(msg);
                }
            }

            if (validMessages == null || validMessages.Count == 0) return;

            lock (_lock)
            {
                _history.AddRange(validMessages);
            }

            _ = _persistence.SaveMessagesAsync(validMessages, CancellationToken.None);
        }

        /// <summary>
        /// Clears all messages from history and marks a new session boundary. The chat date line goes too: the next
        /// chat starts with a line of its own.
        /// </summary>
        public void Clear()
        {
            ClearInMemory();
            _chatDateLine = null;
            _ = _persistence.MarkNewSessionAsync();
        }

        /// <summary>
        /// Clears pending assistant state and the in-memory history without touching persistence.
        /// </summary>
        private void ClearInMemory()
        {
            _pendingAssistantContent = null;
            _pendingAssistantToolCalls = null;
            _pendingAssistantModelId = null;
            _nextAssistantReasoning = null;

            lock (_lock)
            {
                _history.Clear();
                InvalidateCacheLocked();
            }
        }

        /// <summary>
        /// Clears history, starts a new session, and persists the provided messages in that new session.
        /// </summary>
        public async Task ClearAndSaveMessagesAsync(IEnumerable<ChatMessage> messages)
        {
            ClearInMemory();

            var list = messages == null
                ? new List<ChatMessage>()
                : messages.Where(m => m != null).ToList();

            if (list.Count > 0)
            {
                lock (_lock)
                {
                    _history.AddRange(list);
                    InvalidateCacheLocked();
                }
            }

            await _persistence.StartSessionWithMessagesAsync(list, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Clears history and starts a new session, carrying over the last completed step 1:1.
        /// </summary>
        public async Task MoveLastExchangeToNewSessionAsync()
        {
            List<ChatMessage> historyFragment = null;
            lock (_lock)
            {
                if (_history.Count == 0)
                    return;

                if (TryFindLastTextAnswerStep(LookbackLimit, out var start, out var end))
                    historyFragment = _history.GetRange(start, end - start + 1);
            }

            ClearInMemory();

            if (historyFragment?.Count > 0)
            {
                lock (_lock)
                {
                    _history.AddRange(historyFragment);
                    InvalidateCacheLocked();
                }
            }

            await _persistence.StartSessionWithMessagesAsync(
                historyFragment ?? new List<ChatMessage>(),
                CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Clears history and starts a new session, consolidating the last completed step into a clean user/assistant pair.
        /// </summary>
        public async Task ConsolidateLastExchangeAsync()
        {
            List<ChatMessage> consolidatedFragment = null;
            lock (_lock)
            {
                if (_history.Count == 0)
                    return;

                if (TryFindLastTextAnswerStep(LookbackLimit, out var start, out var end))
                {
                    var fragment = _history.GetRange(start, end - start + 1);

                    var userMessage = fragment[0];
                    var finalAssistant = fragment[fragment.Count - 1];

                    var toolCallFuncMap = new Dictionary<string, string>();
                    var toolResults = new List<(string FunctionName, string Json)>();

                    for (int i = 1; i < fragment.Count - 1; i++)
                    {
                        var msg = fragment[i];

                        if (msg.Role == "assistant" && msg.ToolCalls != null)
                        {
                            foreach (var call in msg.ToolCalls)
                            {
                                if (call != null && call.Id != null)
                                    toolCallFuncMap[call.Id] = call.Name;
                            }
                        }
                        else if (msg.Role == "tool"
                                 && msg.Content is string content
                                 && !string.IsNullOrEmpty(content)
                                 && msg.ToolCallId != null
                                 && toolCallFuncMap.TryGetValue(msg.ToolCallId, out var funcName))
                        {
                            toolResults.Add((funcName, content));
                        }
                    }

                    var originalContent = ContentTextExtractor.ExtractTextContent(userMessage.Content);
                    string consolidatedUserContent;

                    string formattedTools = toolResults.Count > 0 && _formatter != null
                        ? _formatter.FormatToolResults(toolResults)
                        : null;

                    consolidatedUserContent = string.IsNullOrEmpty(formattedTools)
                        ? originalContent
                        : originalContent + "\n\n---\n\n" + formattedTools;

                    consolidatedFragment = new List<ChatMessage>
                    {
                        new ChatMessage("user", consolidatedUserContent),
                        finalAssistant
                    };
                }
            }

            ClearInMemory();

            if (consolidatedFragment?.Count > 0)
            {
                lock (_lock)
                {
                    _history.AddRange(consolidatedFragment);
                    InvalidateCacheLocked();
                }
            }

            await _persistence.StartSessionWithMessagesAsync(
                consolidatedFragment ?? new List<ChatMessage>(),
                CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Finds the last completed step in the trailing messages.
        /// </summary>
        private bool TryFindLastTextAnswerStep(int lookbackLimit, out int start, out int end)
        {
            start = -1;
            end = -1;

            int from = Math.Max(0, _history.Count - lookbackLimit);
            for (int i = _history.Count - 1; i >= from; i--)
            {
                var msg = _history[i];

                if (end == -1 && msg.Role == "assistant" && HasText(msg))
                {
                    end = i;
                    continue;
                }

                if (msg.Role == "user" && end != -1)
                {
                    start = i;
                    break;
                }
            }

            return start != -1 && end != -1;
        }

        /// <summary>
        /// Returns true when the message carries visible text (plain string or text content parts).
        /// </summary>
        private static bool HasText(ChatMessage m) => !string.IsNullOrWhiteSpace(ContentTextExtractor.ExtractTextContent(m.Content));

        /// <summary>
        /// Returns a snapshot copy of the current in-memory history.
        /// </summary>
        public IReadOnlyList<ChatMessage> GetHistoryCopy()
        {
            lock (_lock)
            {
                return _history.ToList();
            }
        }

        /// <summary>
        /// Atomically replaces history (summary + recent) only if the current size matches expectedSize, then persists it as a new session.
        /// </summary>
        public async Task<bool> ReplaceHistoryAndPersistAsync(string summary, IEnumerable<ChatMessage> recent, int expectedSize)
        {
            List<ChatMessage> newHistory;
            lock (_lock)
            {
                if (!TryReplaceHistoryLocked(summary, recent, expectedSize, out newHistory))
                    return false;
            }

            await _persistence.StartSessionWithMessagesAsync(newHistory, CancellationToken.None).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Replaces the in-memory history without touching persistence. The disk log keeps the full conversation.
        /// </summary>
        public void ReplaceHistoryInMemory(IEnumerable<ChatMessage> messages)
        {
            var replacement = messages == null
                ? new List<ChatMessage>()
                : messages.Where(m => m != null).ToList();

            _pendingAssistantContent = null;
            _pendingAssistantToolCalls = null;
            _pendingAssistantModelId = null;
            _nextAssistantReasoning = null;

            lock (_lock)
            {
                _history.Clear();
                _history.AddRange(replacement);
                InvalidateCacheLocked();
            }
        }

        /// <summary>
        /// Builds the replacement message list and swaps it into the in-memory history under the lock.
        /// </summary>
        private bool TryReplaceHistoryLocked(string summary, IEnumerable<ChatMessage> recent, int expectedSize, out List<ChatMessage> newHistory)
        {
            newHistory = null;

            if (_history.Count != expectedSize)
                return false;

            newHistory = new List<ChatMessage>();
            if (!string.IsNullOrEmpty(summary))
            {
                DateTime now = DateTime.UtcNow;
                newHistory.Add(new ChatMessage("user", "Provide a brief summary of our previous session to continue.") { Timestamp = now });
                newHistory.Add(new ChatMessage("assistant", summary) { Timestamp = now });
            }
            if (recent != null)
            {
                newHistory.AddRange(recent);
            }

            _history.Clear();
            _history.AddRange(newHistory);
            InvalidateCacheLocked();
            return true;
        }

        /// <summary>
        /// Loads the last persisted session into in-memory history (if empty) and returns its messages.
        /// </summary>
        public async Task<List<ChatMessage>> LoadLastSessionAsync()
        {
            var messages = await _persistence.LoadLastSessionAsync().ConfigureAwait(false);

            if (messages.Count > 0)
            {
                lock (_lock)
                {
                    if (_history.Count == 0)
                    {
                        _history.AddRange(messages);
                        InvalidateCacheLocked();
                    }
                }
            }

            return messages.ToList();
        }

        /// <summary>
        /// Normalizes history for providers (e.g. LlamaCpp) that require strict user/assistant alternation.
        /// </summary>
        public void EnsureHistoryNormalized()
        {
            if (!_providerPolicy.RequiresStrictRoleAlternation(_settingsManager.Current?.Provider)) return;

            List<ChatMessage> snapshot;
            lock (_lock)
            {
                if (_history.Count == 0 || _normalizer.IsUpToDate(_history.Count)) return;
                snapshot = _history.ToList();
            }

            _normalizer.EnsureNormalized(snapshot, _settingsManager);
        }

        /// <summary>
        /// Builds the message list (system + history) for the current provider backend. The chat date line, when the
        /// chat has one, goes to the end of the system message.
        /// </summary>
        public List<ChatMessage> BuildUserMessagesWithHistory(string additionalSystemPrompt = null)
        {
            string systemPrompt = !string.IsNullOrEmpty(additionalSystemPrompt)
                ? additionalSystemPrompt
                : _settingsManager.SystemPrompt;

            string systemText = ComposeSystemMessage(systemPrompt, _chatDateLine);

            var messages = new List<ChatMessage>(_history.Count + 2);
            if (systemText != null)
                messages.Add(new ChatMessage("system", systemText));

            messages.AddRange(_normalizer.Compose(_history));

            return messages;
        }

        /// <summary>
        /// Appends the chat date line to the end of the system prompt, so the prompt text stays an unchanged prefix and
        /// local servers can keep their cached prompt prefix.
        /// </summary>
        private static string ComposeSystemMessage(string systemPrompt, string chatDateLine)
        {
            if (string.IsNullOrEmpty(chatDateLine))
                return string.IsNullOrEmpty(systemPrompt) ? null : systemPrompt;

            if (string.IsNullOrEmpty(systemPrompt))
                return chatDateLine;

            return systemPrompt.TrimEnd() + "\n\n" + chatDateLine;
        }

        private void InvalidateCacheLocked()
        {
            _normalizer.Invalidate();
        }

        /// <summary>
        /// Returns lightweight summaries of recent chat sessions: the last <paramref name="limit"/> sessions by activity,
        /// newest first.
        /// </summary>
        public async Task<List<ChatSessionSummary>> GetChatSessionsAsync(int limit = ChatLogConstants.DefaultSessionListLimit)
        {
            var logs = await _persistence.ReadAllSessionsAsync().ConfigureAwait(false);
            return ChatSessionSummaryBuilder.Build(logs, limit);
        }

        /// <summary>
        /// Loads all messages for a specific session by ID, forks into a new session (so continuation does not mutate the original), and replaces in-memory history.
        /// </summary>
        public async Task<List<ChatMessage>> LoadSessionByIdAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return new List<ChatMessage>();

            var messages = await _persistence.LoadSessionByIdAsync(sessionId).ConfigureAwait(false);

            if (messages.Count > 0)
            {
                _pendingAssistantContent = null;
                _pendingAssistantToolCalls = null;
                _pendingAssistantModelId = null;
                _nextAssistantReasoning = null;
                _chatDateLine = null;

                lock (_lock)
                {
                    _history.Clear();
                    _history.AddRange(messages);
                    InvalidateCacheLocked();
                }

                await _persistence.StartSessionWithMessagesAsync(messages, CancellationToken.None).ConfigureAwait(false);
            }

            return messages;
        }
    }
}
