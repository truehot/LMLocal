using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Api;
using LMLocal.Infrastructure.LlmApi.Provider;
using LMLocal.Infrastructure.LlmApi.Requests;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Tooling;

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
        void AddAssistantMessage(string content, IReadOnlyList<ToolCallRecord> toolCalls);

        /// <summary>
        /// Clears all messages from history and marks a new session boundary.
        /// </summary>
        void Clear();

        /// <summary>
        /// Returns a snapshot copy of the current in-memory history.
        /// </summary>
        IReadOnlyList<ChatMessage> GetHistoryCopy();

        /// <summary>
        /// Atomically replaces history with a summary + recent messages, only if current size matches expectedSize.
        /// </summary>
        bool ReplaceHistory(string summary, IEnumerable<ChatMessage> recent, int expectedSize);

        /// <summary>
        /// Builds a message list for the current provider. 
        /// </summary>
        List<ChatMessage> BuildUserMessagesWithHistory(string additionalSystemPrompt = null);

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
        void SetPendingAssistant(string content, IReadOnlyList<ToolCallRecord> toolCalls);

        /// <summary>
        /// Clears history and starts a new session, carrying over the last user message and the full assistant response (including tool calls/results) that followed it.
        /// </summary>
        Task MoveLastExchangeToNewSessionAsync();

        /// <summary>
        /// Clears history and starts a new session, consolidating the last exchange.
        /// </summary>
        Task ConsolidateLastExchangeAsync();

        /// <summary>
        /// Returns lightweight summaries of recent chat sessions (last <paramref name="limit"/> sessions).
        /// </summary>
        Task<List<ChatSessionSummary>> GetChatSessionsAsync(int limit = ChatLogSerializer.DefaultSessionListLimit);

        /// <summary>
        /// Loads all messages for a specific session by ID and makes it the working session (replaces in-memory history without spawning a new session boundary).
        /// </summary>
        Task<List<ChatMessage>> LoadSessionByIdAsync(string sessionId);
    }

    internal class ChatHistoryManager : IChatHistoryManager
    {
        private readonly List<ChatMessage> _history = new List<ChatMessage>();
        private readonly object _lock = new object();
        private readonly IChatPersistenceService _persistence;
        private readonly ISettingsManager _settingsManager;
        private readonly IToolResultMarkdownFormatter _formatter;

        private List<ChatMessage> _cachedNormalized;
        private int _lastCheckedVersion = 0;

        private string _pendingAssistantContent;
        private IReadOnlyList<ToolCallRecord> _pendingAssistantToolCalls;

        /// <summary>
        /// Creates a chat history manager backed by the given persistence service.
        /// </summary>
        public ChatHistoryManager(ISettingsManager settingsManager, IChatPersistenceService persistence = null, IToolResultMarkdownFormatter formatter = null)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _formatter = formatter;
        }
        /// <summary>
        /// Adds a user message to history and persists it.
        /// </summary>
        public void AddUserMessage(string userPrompt, string activeDocumentContent = null, IReadOnlyList<string> imageDataUrls = null)
        {
            bool hasImages = imageDataUrls != null && imageDataUrls.Count > 0;
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

            ChatMessage userMessage = new ChatMessage("user", content);

            lock (_lock)
            {
                _history.Add(userMessage);
            }

            _ = _persistence?.SaveLastMessageAsync(userMessage, CancellationToken.None);
        }

        /// <summary>
        /// Adds an assistant message with text-only content to history and persists it.
        /// </summary>
        public void AddAssistantMessage(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;

            ChatMessage assistantMessage = new ChatMessage("assistant", content);

            lock (_lock)
            {
                _history.Add(assistantMessage);
            }
            _ = _persistence?.SaveLastMessageAsync(assistantMessage, CancellationToken.None);
        }

        /// <summary>
        /// Adds a single assistant message that may contain both text content and tool calls.
        /// </summary>
        public void AddAssistantMessage(string content, IReadOnlyList<ToolCallRecord> toolCalls)
        {
            bool hasContent = !string.IsNullOrWhiteSpace(content);
            bool hasToolCalls = toolCalls != null && toolCalls.Count > 0;

            if (!hasContent && !hasToolCalls) return;

            List<ToolCall> toolCallObjects = null;
            if (hasToolCalls)
            {
                toolCallObjects = new List<ToolCall>(toolCalls.Count);
                foreach (var toolCall in toolCalls)
                {
                    string normalizedArguments = string.IsNullOrEmpty(toolCall.ArgumentsJson) ? "{}" : toolCall.ArgumentsJson;

                    toolCallObjects.Add(new ToolCall
                    {
                        Id = toolCall.CallId,
                        Type = "function",
                        Function = new FunctionCallDetails
                        {
                            Name = toolCall.FunctionName,
                            Arguments = normalizedArguments
                        }
                    });
                }
            }

            var chatMessage = new ChatMessage("assistant", hasContent ? content : null)
            {
                ToolCalls = toolCallObjects
            };

            lock (_lock)
            {
                _history.Add(chatMessage);
            }
            _ = _persistence?.SaveLastMessageAsync(chatMessage, CancellationToken.None);
        }


        /// <summary>
        /// Queues an assistant message with tool calls to be committed with the next AddToolExecutionResultMessages call.
        /// </summary>
        public void SetPendingAssistant(string content, IReadOnlyList<ToolCallRecord> toolCalls)
        {
            _pendingAssistantContent = content;
            _pendingAssistantToolCalls = toolCalls;
        }

        /// <summary>
        /// Adds tool execution result messages to history and persists them.
        /// </summary>
        public void AddToolExecutionResultMessages(IEnumerable<ChatMessage> messages)
        {
            if (_pendingAssistantToolCalls != null && _pendingAssistantToolCalls.Count > 0)
            {
                AddAssistantMessage(_pendingAssistantContent, _pendingAssistantToolCalls);
                _pendingAssistantContent = null;
                _pendingAssistantToolCalls = null;
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

            _ = _persistence?.SaveMessagesAsync(validMessages, CancellationToken.None);
        }


        /// <summary>
        /// Clears all messages from history and marks a new session boundary.
        /// </summary>
        public void Clear()
        {
            _pendingAssistantContent = null;
            _pendingAssistantToolCalls = null;

            lock (_lock)
            {
                _history.Clear();
                InvalidateCacheLocked();
            }

            _ = _persistence?.MarkNewSessionAsync();
        }

        /// <summary>
        /// Clears history and starts a new session, carrying over the last user message and the assistant response that followed it.
        /// </summary>
        public async Task MoveLastExchangeToNewSessionAsync()
        {
            const int lookbackLimit = 800;

            List<ChatMessage> historyFragment;
            lock (_lock)
            {
                if (_history.Count == 0)
                    return;

                int startIdx = Math.Max(0, _history.Count - lookbackLimit);
                int lastUserIdx = -1;
                bool seenAssistant = false;
                for (int i = _history.Count - 1; i >= startIdx; i--)
                {
                    if (_history[i].Role == "assistant")
                    {
                        seenAssistant = true;
                    }
                    else if (_history[i].Role == "user")
                    {
                        lastUserIdx = i;
                        break;
                    }
                }

                historyFragment = lastUserIdx != -1 && seenAssistant
                    ? _history.Skip(lastUserIdx).ToList()
                    : null;
            }

            Clear();

            if (historyFragment != null && historyFragment.Count > 0)
            {
                lock (_lock)
                {
                    _history.AddRange(historyFragment);
                    InvalidateCacheLocked();
                }
                if (_persistence != null)
                    await _persistence.SaveMessagesAsync(historyFragment, CancellationToken.None).ConfigureAwait(false);
            }
        }


        /// <summary>
        /// Clears history and starts a new session, consolidating the last exchange into a clean user/assistant pair.
        /// </summary>
        public async Task ConsolidateLastExchangeAsync()
        {
            const int lookbackLimit = 800;
            var allowedTools = new HashSet<string>
            {
                "read_file_lines", "get_solution_overview", "get_active_document"
            };

            List<ChatMessage> consolidatedFragment;
            lock (_lock)
            {
                if (_history.Count == 0)
                    return;

                int startIdx = Math.Max(0, _history.Count - lookbackLimit);
                int lastUserIdx = -1;
                bool seenAssistant = false;
                for (int i = _history.Count - 1; i >= startIdx; i--)
                {
                    if (_history[i].Role == "assistant")
                    {
                        seenAssistant = true;
                    }
                    else if (_history[i].Role == "user")
                    {
                        lastUserIdx = i;
                        break;
                    }
                }

                if (lastUserIdx == -1 || !seenAssistant)
                {
                    consolidatedFragment = null;
                }
                else
                {
                    var fragment = _history.Skip(lastUserIdx).ToList();

                    var userMessage = fragment[0];

                    int finalAssistantIdx = -1;
                    for (int i = fragment.Count - 1; i >= 1; i--)
                    {
                        if (fragment[i].Role == "assistant")
                        {
                            finalAssistantIdx = i;
                            break;
                        }
                    }

                    if (finalAssistantIdx == -1)
                    {
                        consolidatedFragment = null;
                    }
                    else
                    {
                        var toolCallFuncMap = new Dictionary<string, string>();
                        var toolResults = new List<(string FunctionName, string Json)>();

                        for (int i = 1; i < finalAssistantIdx; i++)
                        {
                            var msg = fragment[i];

                            if (msg.Role == "assistant" && msg.ToolCalls is List<ToolCall> calls)
                            {
                                foreach (var call in calls)
                                {
                                    if (call?.Function != null && allowedTools.Contains(call.Function.Name))
                                        toolCallFuncMap[call.Id] = call.Function.Name;
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

                        if (toolResults.Count > 0 && _formatter != null)
                        {
                            var formattedTools = _formatter.FormatToolResults(toolResults);
                            consolidatedUserContent = originalContent + "\n\n---\n\n" + formattedTools;
                        }
                        else
                        {
                            consolidatedUserContent = originalContent;
                        }

                        consolidatedFragment = new List<ChatMessage>
                        {
                            new ChatMessage("user", consolidatedUserContent),
                            fragment[finalAssistantIdx]
                        };
                    }
                }
            }

            Clear();

            if (consolidatedFragment != null && consolidatedFragment.Count > 0)
            {
                lock (_lock)
                {
                    _history.AddRange(consolidatedFragment);
                    InvalidateCacheLocked();
                }
                if (_persistence != null)
                    await _persistence.SaveMessagesAsync(consolidatedFragment, CancellationToken.None).ConfigureAwait(false);
            }
        }

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
        /// Atomically replaces history with a summary + recent messages, only if current size matches expectedSize.
        /// </summary>
        public bool ReplaceHistory(string summary, IEnumerable<ChatMessage> recent, int expectedSize)
        {
            lock (_lock)
            {
                if (_history.Count != expectedSize)
                {
                    return false;
                }
                _history.Clear();

                if (!string.IsNullOrEmpty(summary))
                {
                    _history.Add(new ChatMessage("user", "Provide a brief summary of our previous session to continue."));
                    _history.Add(new ChatMessage("assistant", summary));
                }
                if (recent != null)
                {
                    _history.AddRange(recent);
                }
                InvalidateCacheLocked();
                return true;
            }
        }

        /// <summary>
        /// Loads the last persisted session into in-memory history (if empty) and returns its messages.
        /// </summary>
        public async Task<List<ChatMessage>> LoadLastSessionAsync()
        {
            if (_persistence == null)
                return new List<ChatMessage>();

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
            var provider = ProviderResolver.ResolveProvider(_settingsManager.Current?.Provider);
            if (provider != ModelProvider.LlamaCpp) return;

            List<ChatMessage> snapshot;
            lock (_lock)
            {
                if (_history.Count == 0 || _lastCheckedVersion == _history.Count) return;
                snapshot = _history.ToList();
            }

            if (_cachedNormalized == null)
            {
                _cachedNormalized = new List<ChatMessage>();
                _lastCheckedVersion = 0;
            }

            if (snapshot.Count > 0)
            {
                var startFrom = _lastCheckedVersion;
                var isInsidePrompt = false;
                var isInsideTools = false;
                var isInsideToolResults = false;
                for (int i = startFrom; i < snapshot.Count; i++)
                {
                    var msg = snapshot[i];
                    var isLastMessage = (i + 1 >= snapshot.Count);
                    if (isInsidePrompt)
                    {
                        if (isInsideToolResults)
                        {
                            if (msg.Role == "tool")
                            {
                                if (isLastMessage)
                                {
                                    _cachedNormalized.Add(msg);
                                    _cachedNormalized.Add(new ChatMessage("assistant", _settingsManager.AssistantPlaceholder));
                                    isInsideToolResults = false;
                                    isInsideTools = false;
                                    isInsidePrompt = false;
                                    //wait for next user message
                                    continue;
                                }
                                this._cachedNormalized.Add(msg);
                                //wait for other tools
                                continue;
                            }

                            if (msg.Role == "assistant")
                            {
                                if (msg.ToolCalls != null)
                                {
                                    //assistant with tool calls, wait for next tool results
                                    this._cachedNormalized.Add(msg);
                                    continue;
                                }
                                //normal finishing
                                isInsideTools = false;
                                isInsidePrompt = false;
                                isInsideToolResults = false;
                                this._cachedNormalized.Add(msg);
                                //wait for next user message
                                continue;
                            }

                            //anything else except tools or assitant, close validation, wait for next user message
                            _cachedNormalized.Add(new ChatMessage("assistant", _settingsManager.AssistantPlaceholder));
                            isInsideTools = false;
                            isInsidePrompt = false;
                            isInsideToolResults = false;
                            continue;
                        }

                        if (isInsideTools)
                        {
                            if (msg.Role == "tool")
                            {
                                if (isLastMessage)
                                {
                                    _cachedNormalized.Add(msg);
                                    _cachedNormalized.Add(new ChatMessage("assistant", _settingsManager.AssistantPlaceholder));
                                    isInsideToolResults = false;
                                    isInsideTools = false;
                                    isInsidePrompt = false;
                                    //wait for next user message
                                    continue;
                                }

                                isInsideToolResults = true;
                                isInsideTools = false;
                                this._cachedNormalized.Add(msg);
                                continue;
                            }

                            _cachedNormalized.RemoveAt(_cachedNormalized.Count - 1);
                            _cachedNormalized.Add(new ChatMessage("assistant", _settingsManager.AssistantPlaceholder));
                            isInsideTools = false;
                            isInsidePrompt = false;
                            isInsideToolResults = false;
                            //wait for next user message
                            continue;
                        }

                        if (msg.Role == "assistant")
                        {
                            if (msg.ToolCalls != null)
                            {
                                if (isLastMessage)
                                {
                                    _cachedNormalized.Add(new ChatMessage("assistant", _settingsManager.AssistantPlaceholder));
                                    isInsidePrompt = false;
                                    //if no next block finishing
                                    continue;
                                }

                                isInsideTools = true;
                                this._cachedNormalized.Add(msg);
                                continue;
                            }
                            else
                            {
                                //finishing, wait for next user message
                                this._cachedNormalized.Add(msg);
                                isInsidePrompt = false;
                                continue;
                            }
                        }

                        if (msg.Role == "user")
                        {
                            //remove prev user message, as it is not followed by assistant
                            _cachedNormalized.RemoveAt(_cachedNormalized.Count - 1);
                            isInsidePrompt = false;
                            continue;
                        }
                    }

                    if (msg.Role == "user")
                    {
                        if (isLastMessage)
                        {
                            //will not appear in history
                            continue;
                        }
                        isInsidePrompt = true;
                        this._cachedNormalized.Add(msg);
                    }
                }
                _lastCheckedVersion = snapshot.Count;
            }
        }

        /// <summary>
        /// Builds the message list (system + history) for the current provider backend.
        /// </summary>
        public List<ChatMessage> BuildUserMessagesWithHistory(string additionalSystemPrompt = null)
        {
            ChatMessage systemMessage = null;
            if (!string.IsNullOrEmpty(additionalSystemPrompt))
            {
                systemMessage = new ChatMessage("system", additionalSystemPrompt);
            }
            else
            {
                var currentSystemPrompt = _settingsManager.SystemPrompt;
                if (!string.IsNullOrEmpty(currentSystemPrompt))
                {
                    systemMessage = new ChatMessage("system", currentSystemPrompt);
                }
            }

            var messages = new List<ChatMessage>(_history.Count + 2);
            if (systemMessage != null)
                messages.Add(systemMessage);

            if (_cachedNormalized != null)
            {
                var result = new List<ChatMessage>(_cachedNormalized);
                var toAdd = _history.Skip(_lastCheckedVersion);
                result.AddRange(toAdd);
                messages.AddRange(result);
            }
            else
            {
                messages.AddRange(_history);
            }

            return messages;
        }


        private void InvalidateCacheLocked()
        {
            _cachedNormalized = null;
        }

        /// <summary>
        /// Returns lightweight summaries of recent chat sessions (last <paramref name="limit"/> sessions).
        /// </summary>
        public async Task<List<ChatSessionSummary>> GetChatSessionsAsync(int limit = ChatLogSerializer.DefaultSessionListLimit)
        {
            return await _persistence.GetChatSessionsAsync(limit).ConfigureAwait(false);
        }

        /// <summary>
        /// Loads all messages for a specific session by ID, forks into a new session (so continuation does not mutate the original), and replaces in-memory history.
        /// </summary>
        public async Task<List<ChatMessage>> LoadSessionByIdAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return new List<ChatMessage>();

            if (_persistence == null)
                return new List<ChatMessage>();

            var messages = await _persistence.LoadSessionByIdAsync(sessionId).ConfigureAwait(false);

            if (messages.Count > 0)
            {
                _pendingAssistantContent = null;
                _pendingAssistantToolCalls = null;

                await _persistence.MarkNewSessionAsync().ConfigureAwait(false);

                lock (_lock)
                {
                    _history.Clear();
                    _history.AddRange(messages);
                    InvalidateCacheLocked();
                }
            }

            return messages;
        }
    }
}

