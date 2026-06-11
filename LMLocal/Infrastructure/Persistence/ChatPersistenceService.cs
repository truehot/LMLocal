using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Settings;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Persistence
{

    /// <summary>
    /// Saves chat messages to a local file in JSON Lines format for later retrieval. 
    /// </summary>
    internal interface IChatPersistenceService
    {
        Task SaveLastMessageAsync(ChatMessage message, CancellationToken cancellationToken = default);
    }

    internal class ChatPersistenceService : IChatPersistenceService
    {
        private readonly string _chatHistoryDir;
        private readonly IFileSystem _fileSystem;
        private readonly ISettingsManager _settingsManager;
        private readonly SemaphoreSlim _writeSemaphore = new SemaphoreSlim(1, 1);

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
        }

        public async Task SaveLastMessageAsync(ChatMessage message, CancellationToken cancellationToken = default)
        {
            if (_settingsManager?.Current?.EnableChatLogging != true || message == null) return;

            await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                string fileName = $"{_settingsManager.ChatHistoryFilePrefix}{DateTime.UtcNow:yyyyMMdd_HH}.jsonl";
                string filePath = Path.Combine(_chatHistoryDir, fileName);

                var entry = new
                {
                    timestamp = DateTime.UtcNow,
                    role = message.Role,
                    content = message.Content,
                    tool_call_id = message.ToolCallId,
                    tool_calls = message.ToolCalls
                };

                string jsonLine = JsonConvert.SerializeObject(entry) + Environment.NewLine;
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
            catch (Exception ex)
            {
                InternalLogger.Error("Failed to save chat message history, trying again", ex);
                try
                {
                    string fileName = $"{_settingsManager.ChatHistoryFilePrefix}{DateTime.UtcNow:yyyyMMdd_HH}_{Guid.NewGuid():N}.jsonl";
                    string filePath = Path.Combine(_chatHistoryDir, fileName);

                    var entry = new
                    {
                        timestamp = DateTime.UtcNow,
                        role = message.Role,
                        content = message.Content,
                        tool_call_id = message.ToolCallId,
                        tool_calls = message.ToolCalls
                    };

                    string jsonLine = JsonConvert.SerializeObject(entry) + Environment.NewLine;
                    byte[] data = Encoding.UTF8.GetBytes(jsonLine);

                    await _fileSystem.WriteAllBytesAsync(filePath, data, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex2)
                {
                    InternalLogger.Error("Failed to save chat message history", ex2);
                }
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }
    }
}
