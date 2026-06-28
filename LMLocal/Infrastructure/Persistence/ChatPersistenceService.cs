using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi.Requests;
using LMLocal.Infrastructure.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Persistence
{

    /// <summary>
    /// Saves chat messages to a local file in JSON Lines format for later retrieval.
    /// </summary>
    internal interface IChatPersistenceService
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
        /// Writes a session_start marker to the current jsonl file, establishing a new session boundary.
        /// </summary>
        Task MarkNewSessionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Scans all jsonl chat files, finds the most recent session_start marker, and returns all messages belonging to that session in chronological order.
        /// </summary>
        Task<List<ChatMessage>> LoadLastSessionAsync(CancellationToken cancellationToken = default);
    }

    internal class ChatPersistenceService : IChatPersistenceService
    {
        private readonly string _chatHistoryDir;
        private readonly IFileSystem _fileSystem;
        private readonly ISettingsManager _settingsManager;
        private readonly SemaphoreSlim _writeSemaphore = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Identifies the current chat session. Regenerated every time
        /// </summary>
        private Guid _currentSessionId;

        public ChatPersistenceService(ISettingsManager settingsManager, IFileSystem fileSystem)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

            _chatHistoryDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                _settingsManager.LocalAppDataFolder,
                _settingsManager.ChatHistoryFolder
            );
            _fileSystem.CreateDirectory(_chatHistoryDir);

            _currentSessionId = Guid.NewGuid();
        }

        public async Task SaveLastMessageAsync(ChatMessage message, CancellationToken cancellationToken = default)
        {
            if (_settingsManager?.Current?.EnableChatLogging != true || message == null) return;

            await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                string fileName = BuildFileName();
                string filePath = Path.Combine(_chatHistoryDir, fileName);

                string jsonLine = BuildMessageLine(message);

                await AppendLineAsync(filePath, jsonLine, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Failed to save chat message history, trying again", ex);
                await TryFallbackSaveAsync(message, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        public async Task SaveMessagesAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
        {
            if (_settingsManager?.Current?.EnableChatLogging != true || messages == null)
                return;

            var list = messages as IReadOnlyList<ChatMessage> ?? messages.Where(m => m != null).ToList();
            if (list.Count == 0) return;

            await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                string fileName = BuildFileName();
                string filePath = Path.Combine(_chatHistoryDir, fileName);

                var sb = new StringBuilder(list.Count * 256);
                for (int i = 0; i < list.Count; i++)
                    sb.Append(BuildMessageLine(list[i]));

                await AppendLineAsync(filePath, sb.ToString(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Failed to batch-save chat messages, trying fallback", ex);
                foreach (var msg in list)
                    await TryFallbackSaveAsync(msg, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        public async Task MarkNewSessionAsync(CancellationToken cancellationToken = default)
        {
            if (_settingsManager?.Current?.EnableChatLogging != true) return;

            _currentSessionId = Guid.NewGuid();

            await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                string fileName = BuildFileName();
                string filePath = Path.Combine(_chatHistoryDir, fileName);

                var marker = new Dictionary<string, object>
                {
                    { "type", "session_start" },
                    { "session_id", _currentSessionId.ToString() },
                    { "timestamp", DateTime.UtcNow.ToString("o") }
                };

                string jsonLine = JsonConvert.SerializeObject(marker) + Environment.NewLine;

                await AppendLineAsync(filePath, jsonLine, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Failed to write session_start marker", ex);
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        public async Task<List<ChatMessage>> LoadLastSessionAsync(CancellationToken cancellationToken = default)
        {
            var files = (_fileSystem.GetFiles(_chatHistoryDir, "*.jsonl") ?? Array.Empty<string>())
                         .OrderByDescending(f => f, StringComparer.Ordinal)
                         .Take(50)
                         .ToList();

            if (files.Count == 0)
                return new List<ChatMessage>();

            string targetSessionId = null;
            var messages = new List<ChatMessage>();

            foreach (string filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var lines = await ReadAllLinesAsync(filePath, cancellationToken).ConfigureAwait(false);

                    for (int i = lines.Count - 1; i >= 0; i--)
                    {
                        string line = lines[i];
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        JObject obj;
                        try { obj = JObject.Parse(line); }
                        catch { continue; }

                        string sessionId = obj.Value<string>("session_id");
                        if (string.IsNullOrEmpty(sessionId)) continue;

                        if (targetSessionId == null)
                        {
                            targetSessionId = sessionId;

                            if (Guid.TryParse(sessionId, out var parsed))
                                _currentSessionId = parsed;
                        }
                        else if (!string.Equals(sessionId, targetSessionId, StringComparison.Ordinal))
                        {
                            messages.Reverse();
                            return messages;
                        }

                        string entryType = obj.Value<string>("type");
                        if (entryType == "session_start")
                        {
                            messages.Reverse();
                            return messages;
                        }

                        var chatMessage = ParseChatMessage(obj);
                        if (chatMessage != null)
                            messages.Add(chatMessage);
                    }
                }
                catch (Exception ex)
                {
                    InternalLogger.Error($"Error reading chat history file '{filePath}'", ex);
                }
            }

            messages.Reverse();
            return messages;
        }

        /// <summary>
        /// Builds the consistent hourly file name: yyyyMMdd_HH_label.jsonl
        /// </summary>
        private string BuildFileName()
        {
            return $"{DateTime.UtcNow:yyyyMMdd_HH}_{_settingsManager.ChatHistoryFileLabel}.jsonl";
        }

        /// <summary>
        /// Serializes a ChatMessage into a JSON line with type, session_id, and timestamp.
        /// </summary>
        private string BuildMessageLine(ChatMessage message)
        {
            var entry = new Dictionary<string, object>
            {
                { "type", "message" },
                { "session_id", _currentSessionId.ToString() },
                { "timestamp", DateTime.UtcNow.ToString("o") },
                { "role", message.Role },
                { "content", message.Content },
                { "tool_call_id", message.ToolCallId },
                { "tool_calls", message.ToolCalls }
            };

            return JsonConvert.SerializeObject(entry) + Environment.NewLine;
        }

        /// <summary>
        /// Appends a UTF-8 line to the file, creating it if it does not exist.
        /// </summary>
        private async Task AppendLineAsync(string filePath, string jsonLine, CancellationToken cancellationToken)
        {
            byte[] data = Encoding.UTF8.GetBytes(jsonLine);

            if (_fileSystem.FileExists(filePath))
            {
                await _fileSystem.AppendAllBytesAsync(filePath, data, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _fileSystem.WriteAllBytesAsync(filePath, data, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Fallback save: writes to a unique file name when the primary write fails.
        /// </summary>
        private async Task TryFallbackSaveAsync(ChatMessage message, CancellationToken cancellationToken)
        {
            try
            {
                string fileName = $"{DateTime.UtcNow:yyyyMMdd_HH}_{_settingsManager.ChatHistoryFileLabel}_{Guid.NewGuid():N}.jsonl";
                string filePath = Path.Combine(_chatHistoryDir, fileName);

                string jsonLine = BuildMessageLine(message);

                await _fileSystem.WriteAllBytesAsync(filePath, Encoding.UTF8.GetBytes(jsonLine), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex2)
            {
                InternalLogger.Error("Failed to save chat message history", ex2);
            }
        }

        /// <summary>
        /// Reads all lines from a file via IFileSystem.
        /// </summary>
        private async Task<List<string>> ReadAllLinesAsync(string filePath, CancellationToken cancellationToken)
        {
            try
            {
                string content = await _fileSystem.ReadAllTextWithSharedReadAsync(filePath, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(content))
                    return new List<string>();

                return new List<string>(content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None));
            }
            catch (FileNotFoundException)
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Deserializes a JObject (from jsonl) to a ChatMessage.
        /// </summary>
        private static ChatMessage ParseChatMessage(JObject obj)
        {
            try
            {
                string role = obj.Value<string>("role");
                object content = obj["content"]?.ToObject<object>();
                string toolCallId = obj.Value<string>("tool_call_id");

                List<ToolCall> toolCalls = null;
                var toolCallsToken = obj["tool_calls"];
                if (toolCallsToken != null && toolCallsToken.Type != JTokenType.Null)
                {
                    toolCalls = toolCallsToken.ToObject<List<ToolCall>>();
                }

                return new ChatMessage(role ?? "unknown", content, toolCallId)
                {
                    ToolCalls = toolCalls
                };
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Failed to parse chat message from jsonl line", ex);
                return null;
            }
        }
    }
}
