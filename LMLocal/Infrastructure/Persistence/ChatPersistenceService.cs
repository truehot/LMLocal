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
using LMLocal.Application.Abstractions.Ports;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Persistence
{
    internal class ChatPersistenceService : IChatPersistenceService
    {
        private readonly string _chatHistoryDir;
        private readonly IFileSystem _fileSystem;
        private readonly ISettingsManager _settingsManager;
        private readonly SemaphoreSlim _writeSemaphore = new SemaphoreSlim(1, 1);
        private readonly IFileLockManager _fileLockManager;

        /// <summary>
        /// Identifies the current chat session. Regenerated every time
        /// </summary>
        private Guid _currentSessionId;

        /// <summary>
        /// Id of the session currently being written or loaded (the live session). Read-only.
        /// </summary>
        public Guid? CurrentSessionId => _currentSessionId;

        /// <summary>
        /// Creates a persistence service bound to the configured local chat history directory.
        /// </summary>
        public ChatPersistenceService(ISettingsManager settingsManager, IFileSystem fileSystem)
            : this(settingsManager, fileSystem, null, FileLockManager.Shared)
        {
        }

        /// <summary>
        /// Creates a persistence service bound to an explicit directory.
        /// </summary>
        public ChatPersistenceService(ISettingsManager settingsManager, IFileSystem fileSystem, string explicitDirectory)
            : this(settingsManager, fileSystem, explicitDirectory, FileLockManager.Shared)
        {
        }

        /// <summary>
        /// Creates a persistence service bound to the configured local chat history directory, using the supplied process-wide file lock manager to serialize appends to the shared hourly jsonl file.
        /// </summary>
        public ChatPersistenceService(ISettingsManager settingsManager, IFileSystem fileSystem, IFileLockManager fileLockManager)
            : this(settingsManager, fileSystem, null, fileLockManager)
        {
        }

        /// <summary>
        /// Creates a persistence service bound to an explicit directory and file lock manager.
        /// </summary>
        public ChatPersistenceService(ISettingsManager settingsManager, IFileSystem fileSystem, string explicitDirectory, IFileLockManager fileLockManager)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _fileLockManager = fileLockManager ?? throw new ArgumentNullException(nameof(fileLockManager));

            _chatHistoryDir = string.IsNullOrWhiteSpace(explicitDirectory)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    _settingsManager.LocalAppDataFolder,
                    _settingsManager.ChatHistoryFolder
                )
                : explicitDirectory;
            _fileSystem.CreateDirectory(_chatHistoryDir);

            _currentSessionId = Guid.NewGuid();
        }

        /// <summary>
        /// Appends a single chat message line (JSON) for the current session. Falls back to a unique file name when the primary write fails.
        /// </summary>
        public async Task SaveLastMessageAsync(ChatMessage message, CancellationToken cancellationToken = default)
        {
            Guid sessionId = _currentSessionId;

            if (_settingsManager?.Current?.EnableChatLogging != true || message == null) return;

            await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                string fileName = ChatLogSerializer.BuildFileName(DateTime.UtcNow, _settingsManager.ChatHistoryFileLabel);
                string filePath = Path.Combine(_chatHistoryDir, fileName);

                string jsonLine = ChatLogSerializer.BuildMessageLine(message, sessionId.ToString(), DateTime.UtcNow);

                await AppendLineAsync(filePath, jsonLine, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Failed to save chat message history, trying again", ex);
                await TryFallbackSaveAsync(message, sessionId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        /// <summary>
        /// Appends multiple chat message lines in a single write, acquiring the write semaphore once.
        /// </summary>
        public async Task SaveMessagesAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
        {
            Guid sessionId = _currentSessionId;

            if (_settingsManager?.Current?.EnableChatLogging != true || messages == null)
                return;

            var list = messages as IReadOnlyList<ChatMessage> ?? messages.Where(m => m != null).ToList();
            if (list.Count == 0) return;

            await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                string fileName = ChatLogSerializer.BuildFileName(DateTime.UtcNow, _settingsManager.ChatHistoryFileLabel);
                string filePath = Path.Combine(_chatHistoryDir, fileName);

                var sb = new StringBuilder(list.Count * 256);
                for (int i = 0; i < list.Count; i++)
                    sb.Append(ChatLogSerializer.BuildMessageLine(list[i], sessionId.ToString(), DateTime.UtcNow));

                await AppendLineAsync(filePath, sb.ToString(), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Failed to batch-save chat messages, trying fallback", ex);
                foreach (var msg in list)
                    await TryFallbackSaveAsync(msg, sessionId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        /// <summary>
        /// Writes a session_start marker to the current jsonl file and rotates the current session ID.
        /// </summary>
        public async Task MarkNewSessionAsync(CancellationToken cancellationToken = default)
        {
            if (_settingsManager?.Current?.EnableChatLogging != true) return;

            Guid newSessionId = Guid.NewGuid();
            _currentSessionId = newSessionId;

            await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                string fileName = ChatLogSerializer.BuildFileName(DateTime.UtcNow, _settingsManager.ChatHistoryFileLabel);
                string filePath = Path.Combine(_chatHistoryDir, fileName);

                string jsonLine = ChatLogSerializer.BuildSessionStartMarker(newSessionId.ToString(), DateTime.UtcNow);

                await AppendLineAsync(filePath, jsonLine, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // No rollback.
                InternalLogger.Error("Failed to write session_start marker", ex);
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        /// <summary>
        /// Starts a new session and writes its session_start marker together with the supplied messages as a single append, under one semaphore acquisition and one session ID rotation.
        /// </summary>
        public async Task<bool> StartSessionWithMessagesAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
        {
            if (_settingsManager?.Current?.EnableChatLogging != true)
                return false;

            List<ChatMessage> validMessages = null;
            if (messages != null)
            {
                for (int i = 0; i < messages.Count; i++)
                {
                    var message = messages[i];
                    if (message == null) continue;

                    if (validMessages == null)
                        validMessages = new List<ChatMessage>(messages.Count);

                    validMessages.Add(message);
                }
            }

            Guid previousSessionId = _currentSessionId;
            Guid newSessionId = Guid.NewGuid();
            _currentSessionId = newSessionId;

            await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                string fileName = ChatLogSerializer.BuildFileName(DateTime.UtcNow, _settingsManager.ChatHistoryFileLabel);
                string filePath = Path.Combine(_chatHistoryDir, fileName);

                DateTime utcNow = DateTime.UtcNow;
                string sessionId = newSessionId.ToString();

                var sb = new StringBuilder((validMessages?.Count ?? 0) * 256 + 128);
                sb.Append(ChatLogSerializer.BuildSessionStartMarker(sessionId, utcNow));

                if (validMessages != null)
                {
                    for (int i = 0; i < validMessages.Count; i++)
                        sb.Append(ChatLogSerializer.BuildMessageLine(validMessages[i], sessionId, utcNow));
                }

                await AppendLineAsync(filePath, sb.ToString(), cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                // Roll back.
                _currentSessionId = previousSessionId;
                InternalLogger.Error("Failed to start chat session with messages", ex);
                return false;
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        /// <summary>
        /// Scans all jsonl chat files, finds the most recent session_start marker, and returns all messages belonging to that session in chronological order.
        /// </summary>
        public async Task<List<ChatMessage>> LoadLastSessionAsync(CancellationToken cancellationToken = default)
        {
            var files = await ReadJsonlFilesAsync(cancellationToken).ConfigureAwait(false);

            if (files.Count == 0)
                return new List<ChatMessage>();

            string targetSessionId = null;
            var messages = new List<ChatMessage>();

            foreach (var file in files)
            {
                var lines = file.Lines;

                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    JObject obj = lines[i];

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

                    var chatMessage = ChatLogSerializer.ParseChatMessage(obj);
                    if (chatMessage != null)
                        messages.Add(chatMessage);
                }
            }

            messages.Reverse();
            return messages;
        }

        /// <summary>
        /// Appends a UTF-8 line to the file, creating it if it does not exist.
        /// </summary>
        private async Task AppendLineAsync(string filePath, string jsonLine, CancellationToken cancellationToken)
        {
            byte[] data = Encoding.UTF8.GetBytes(jsonLine);

            await _fileLockManager.WaitAsync(filePath, cancellationToken).ConfigureAwait(false);
            try
            {
                if (_fileSystem.FileExists(filePath))
                {
                    await _fileSystem.AppendAllBytesAsync(filePath, data, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _fileSystem.WriteAllBytesAsync(filePath, data, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _fileLockManager.Release(filePath);
            }
        }

        /// <summary>
        /// Fallback save: writes to a unique file name when the primary write fails.
        /// </summary>
        private async Task TryFallbackSaveAsync(ChatMessage message, Guid sessionId, CancellationToken cancellationToken)
        {
            try
            {
                string baseName = ChatLogSerializer.BuildFileName(DateTime.UtcNow, _settingsManager.ChatHistoryFileLabel);
                string fileName = baseName.Replace(".jsonl", $"_{Guid.NewGuid():N}.jsonl");
                string filePath = Path.Combine(_chatHistoryDir, fileName);

                string jsonLine = ChatLogSerializer.BuildMessageLine(message, sessionId.ToString(), DateTime.UtcNow);

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
        /// Enumerates the newest jsonl files (max ChatLogSerializer.MaxJsonlFilesToScan), parsing each non-empty line into a JObject and grouping the results per file in forward line order.
        /// </summary>
        private async Task<List<JsonlFile>> ReadJsonlFilesAsync(CancellationToken cancellationToken)
        {
            var files = (_fileSystem.GetFiles(_chatHistoryDir, "*.jsonl") ?? Array.Empty<string>())
                         .OrderByDescending(f => f, StringComparer.Ordinal)
                         .Take(ChatLogSerializer.MaxJsonlFilesToScan)
                         .ToList();

            var result = new List<JsonlFile>(files.Count);

            foreach (string filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var lines = await ReadAllLinesAsync(filePath, cancellationToken).ConfigureAwait(false);

                    var parsed = new List<JObject>(lines.Count);
                    foreach (string line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        JObject obj;
                        try { obj = JObject.Parse(line); }
                        catch { continue; }

                        parsed.Add(obj);
                    }

                    result.Add(new JsonlFile
                    {
                        FilePath = filePath,
                        FileName = Path.GetFileName(filePath),
                        Lines = parsed
                    });
                }
                catch (Exception ex)
                {
                    InternalLogger.Error($"Error reading chat history file '{filePath}'", ex);
                }
            }

            return result;
        }

        /// <summary>
        /// Scans jsonl chat files and returns lightweight summaries of the last <paramref name="limit"/> sessions, each containing the first user message (truncated), timestamp, and message count.
        /// </summary>
        public async Task<List<ChatSessionSummary>> GetChatSessionsAsync(int limit = ChatLogSerializer.DefaultSessionListLimit, CancellationToken cancellationToken = default)
        {
            if (limit <= 0) return new List<ChatSessionSummary>();

            var files = await ReadJsonlFilesAsync(cancellationToken).ConfigureAwait(false);

            if (files.Count == 0) return new List<ChatSessionSummary>();

            var sessionData = new Dictionary<string, SessionAccumulator>(StringComparer.Ordinal);
            int sequence = 0;

            foreach (var file in files)
            {
                foreach (JObject obj in file.Lines)
                {
                    string sessionId = obj.Value<string>("session_id");
                    if (string.IsNullOrEmpty(sessionId)) continue;

                    string entryType = obj.Value<string>("type");
                    if (entryType == "session_start") continue;

                    string role = obj.Value<string>("role");
                    string timestamp = obj.Value<string>("timestamp") ?? string.Empty;
                    string content = obj.Value<object>("content")?.ToString() ?? string.Empty;

                    if (!sessionData.TryGetValue(sessionId, out var entry))
                    {
                        entry = new SessionAccumulator { Timestamp = timestamp, Sequence = ++sequence };
                        sessionData[sessionId] = entry;
                    }

                    entry.MessageCount++;

                    if (string.IsNullOrEmpty(entry.Timestamp))
                        entry.Timestamp = timestamp;

                    if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
                    {
                        entry.Prompt = ChatLogSerializer.TruncatePrompt(content);
                    }
                }
            }

            var summaries = sessionData
                .OrderByDescending(kvp => kvp.Value.Timestamp, StringComparer.Ordinal)
                .ThenByDescending(kvp => kvp.Value.Sequence)
                .Take(limit)
                .Select(kvp => new ChatSessionSummary
                {
                    SessionId = kvp.Key,
                    Prompt = kvp.Value.Prompt ?? string.Empty,
                    Timestamp = kvp.Value.Timestamp ?? string.Empty,
                    MessageCount = kvp.Value.MessageCount
                })
                .ToList();

            return summaries;
        }

        /// <summary>
        /// Scans jsonl chat files for a specific session by ID and returns all its messages in chronological order.
        /// </summary>
        public async Task<List<ChatMessage>> LoadSessionByIdAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return new List<ChatMessage>();

            var files = await ReadJsonlFilesAsync(cancellationToken).ConfigureAwait(false);

            if (files.Count == 0) return new List<ChatMessage>();

            var messages = new List<ChatMessage>();
            bool currentSessionSet = false;

            foreach (var file in files)
            {
                var lines = file.Lines;

                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    JObject obj = lines[i];

                    string lineSessionId = obj.Value<string>("session_id");
                    if (!string.Equals(lineSessionId, sessionId, StringComparison.Ordinal))
                        continue;

                    if (!currentSessionSet && Guid.TryParse(sessionId, out var parsedSessionId))
                    {
                        _currentSessionId = parsedSessionId;
                        currentSessionSet = true;
                    }

                    string entryType = obj.Value<string>("type");
                    if (entryType == "session_start")
                    {
                        messages.Reverse();
                        return messages;
                    }

                    var chatMessage = ChatLogSerializer.ParseChatMessage(obj);
                    if (chatMessage != null)
                        messages.Add(chatMessage);
                }
            }

            messages.Reverse();
            return messages;
        }

        /// <summary>
        /// Read-only single-pass scan of the newest jsonl files that returns every session's messages in chronological order, each paired with its UTC timestamp.
        /// </summary>
        public async Task<List<ChatSessionLog>> ReadAllSessionsAsync(CancellationToken cancellationToken = default)
        {
            var files = await ReadJsonlFilesAsync(cancellationToken).ConfigureAwait(false);

            if (files.Count == 0)
                return new List<ChatSessionLog>();

            var bySession = new Dictionary<string, ChatSessionLog>(StringComparer.Ordinal);

            foreach (var file in files)
            {
                var lines = file.Lines;

                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    JObject obj = lines[i];

                    string sessionId = obj.Value<string>("session_id");
                    if (string.IsNullOrEmpty(sessionId)) continue;

                    string entryType = obj.Value<string>("type");
                    if (entryType == "session_start") continue;

                    var chatMessage = ChatLogSerializer.ParseChatMessage(obj);
                    if (chatMessage == null) continue;

                    if (!bySession.TryGetValue(sessionId, out var log))
                    {
                        log = new ChatSessionLog { SessionId = sessionId, Entries = new List<ChatLogEntry>() };
                        bySession[sessionId] = log;
                    }

                    log.Entries.Add(new ChatLogEntry
                    {
                        Message = chatMessage,
                        TimestampUtc = ParseTimestamp(obj)
                    });
                }
            }

            foreach (var log in bySession.Values)
                log.Entries.Reverse();

            return new List<ChatSessionLog>(bySession.Values);
        }

        /// <summary>
        /// Parses the "timestamp" field of a jsonl line (ISO-8601 round-trip) into UTC, or <see cref="DateTime.MinValue"/> when absent/unparsable.
        /// </summary>
        private static DateTime ParseTimestamp(JObject obj)
        {
            string timestamp = obj.Value<string>("timestamp");
            if (!string.IsNullOrEmpty(timestamp)
                && DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
            {
                return parsed.ToUniversalTime();
            }

            return DateTime.MinValue;
        }

        /// <summary>
        /// Parsed JSON lines of a single jsonl file, kept in forward line order.
        /// </summary>
        private sealed class JsonlFile
        {
            public string FilePath { get; set; }
            public string FileName { get; set; }
            public List<JObject> Lines { get; set; }
        }

        /// <summary>
        /// Mutable accumulator used while building session summaries from jsonl files.
        /// </summary>
        private sealed class SessionAccumulator
        {
            public string Prompt { get; set; }
            public string Timestamp { get; set; }
            public int MessageCount { get; set; }
            public int Sequence { get; set; }
        }
    }
}
