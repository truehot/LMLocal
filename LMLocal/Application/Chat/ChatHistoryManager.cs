using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LMLocal.Common;
using LMLocal.Infrastructure.Api.Requests;
using LMLocal.Models;

namespace LMLocal.Services
{

    /// <summary>
    /// Keeps track of the chat history, including user and assistant messages, and provides methods to manipulate and retrieve the history.
    /// </summary>
    internal interface IChatHistoryManager
    {
        void AddUserMessage(string content);
        void AddAssistantMessage(string content);
        void Clear();
        IReadOnlyList<ChatMessage> GetHistoryCopy();
        bool ReplaceHistory(string summary, IEnumerable<ChatMessage> recent, int expectedSize);
        List<ChatMessage> BuildUserMessagesWithHistory(string userPrompt, string includedContent = null, string additionalPrompt = null);
        void AddToolExecutionResultMessage(ChatMessage message);
        void AddAssistantToolRequestMessage(IReadOnlyList<ToolCallRecord> toolCalls);
    }

    internal class ChatHistoryManager : IChatHistoryManager
    {
        private readonly List<ChatMessage> _history = new List<ChatMessage>();
        private readonly object _lock = new object();
        private readonly string _systemPrompt;
        private readonly IChatPersistenceService _persistence;
        private readonly ISettingsManager _settingsManager;

        public ChatHistoryManager(ISettingsManager settingsManager, IChatPersistenceService persistence = null)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));

            _systemPrompt = settingsManager?.SystemPrompt ?? "";

        }

        public void AddUserMessage(string prompt)
        {
            if (string.IsNullOrEmpty(prompt)) return;

            bool compress = _settingsManager?.Current?.EnableHistoryCompression ?? false;

            lock (_lock)
            {
                _history.Add(new ChatMessage("user", compress ? MarkdownStripper.Strip(prompt) : prompt));
            }
            // Save the last user message for persistence, ignore additional prompt and active document content
            _ = _persistence?.SaveLastMessageAsync(new ChatMessage("user", prompt), CancellationToken.None);
        }

        public void AddAssistantMessage(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;

            bool compress = _settingsManager?.Current?.EnableHistoryCompression ?? false;

            ChatMessage assistantMessage = new ChatMessage("assistant", compress ? MarkdownStripper.Strip(content) : content);

            lock (_lock)
            {
                _history.Add(assistantMessage);
            }
            _ = _persistence?.SaveLastMessageAsync(assistantMessage, CancellationToken.None);
        }

        public void AddAssistantToolRequestMessage(IReadOnlyList<ToolCallRecord> toolCalls)
        {
            if (toolCalls == null || toolCalls.Count == 0) return;


            var toolCallObjects = new List<ToolCall>();
            foreach (var toolCall in toolCalls)
            {
                // Normalize empty arguments to "{}" (empty JSON object) per OpenAI API spec
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

            var chatMessage = new ChatMessage("assistant", null);
            chatMessage.ToolCalls = toolCallObjects;

            lock (_lock)
            {
                _history.Add(chatMessage);
            }

            _ = _persistence?.SaveLastMessageAsync(chatMessage, CancellationToken.None);
        }

        public void AddToolExecutionResultMessage(ChatMessage message)
        {
            if (message == null) return;

            lock (_lock)
            {
                _history.Add(message);
            }
            _ = _persistence?.SaveLastMessageAsync(message, CancellationToken.None);
        }

        public void Clear()
        {
            lock (_lock)
            {
                _history.Clear();
            }
        }

        public IReadOnlyList<ChatMessage> GetHistoryCopy()
        {
            lock (_lock)
            {
                return _history.ToList();
            }
        }

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
                    _history.Add(new ChatMessage("assistant", summary));
                }
                if (recent != null)
                {
                    _history.AddRange(recent);
                }
                return true;
            }
        }

        public List<ChatMessage> BuildUserMessagesWithHistory(string userPrompt, string includedContent = null, string additionalSystemPrompt = null)
        {
            if (string.IsNullOrEmpty(userPrompt)) return new List<ChatMessage>();

            bool compress = _settingsManager.Current?.EnableHistoryCompression ?? false;

            var messages = new List<ChatMessage>();
            lock (_lock)
            {
                messages.AddRange(_history);

                if (!string.IsNullOrEmpty(additionalSystemPrompt))
                {
                    messages.Add(new ChatMessage("system", compress ? MarkdownStripper.Strip(additionalSystemPrompt) : additionalSystemPrompt));
                }
                else if (!string.IsNullOrEmpty(_systemPrompt))
                {
                    messages.Add(new ChatMessage("system", compress ? MarkdownStripper.Strip(_systemPrompt) : _systemPrompt));
                }

                if (!string.IsNullOrEmpty(includedContent))
                {
                    messages.Add(new ChatMessage("user", compress ? MarkdownStripper.Strip(FormatIncludedContent(includedContent)) : FormatIncludedContent(includedContent)));
                }

                messages.Add(new ChatMessage("user", compress ? MarkdownStripper.Strip(userPrompt) : userPrompt));
            }

            return messages;
        }

        private static string FormatIncludedContent(string content)
        {
            return $"Reference code:\n\n{content}";
        }
    }
}

