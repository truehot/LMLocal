using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.VisualStudio;
using LMLocal.Infrastructure.WebView.Models;
using LMLocal.Models;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.WebView
{
    /// <summary>
    /// Bridge class for communication between WebView2 and backend logic.
    /// </summary>
    public interface IWebViewBridge
    {
        Task<bool> CopyToClipboardAsync(string text);
        Task ExecutePromptAsync(string requestJson);
        Task<bool> ResetHistoryWithActionAsync(string action);
        Task<bool> SummarizeAndCompactAsync(string modelId);
        Task<string> GetLastChatSessionAsync();
        Task StopExecutionAsync();
        Task<bool> GetSnapshotAsync();
        Task<bool> DiscardChangesAsync();
        Task<bool> AcceptChangesAsync();
        Task<bool> ReviewFileAsync(string filePath);
        Task<bool> ReviewAllFilesAsync(string filePathsJson);
        Task<bool> OpenAllFilesAsync(string filePathsJson);
        Task<bool> DiscardFileAsync(string filePath);
    }


    [ComVisible(true)]
    public class WebViewBridge : IWebViewBridge
    {
        private readonly IWebViewScriptExecutor _scriptExecutor;
        private readonly IGetActiveDocument _activeDocumentTool;
        private readonly ISessionManager _sessionManager;
        private readonly IChatHistoryManager _chatHistoryManager;
        private readonly IHistoryCompactor _historyCompactor;
        private readonly ISnapshotManager _snapshotManager;

        internal WebViewBridge(
            IWebViewScriptExecutor scriptExecutor,
            IGetActiveDocument activeDocumentTool,
            ISessionManager sessionManager,
            IChatHistoryManager chatHistoryManager,
            IHistoryCompactor historyCompactor,
            ISnapshotManager snapshotManager)
        {
            _scriptExecutor = scriptExecutor ?? throw new ArgumentNullException(nameof(scriptExecutor));
            _activeDocumentTool = activeDocumentTool ?? throw new ArgumentNullException(nameof(activeDocumentTool));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _chatHistoryManager = chatHistoryManager ?? throw new ArgumentNullException(nameof(chatHistoryManager));
            _historyCompactor = historyCompactor ?? throw new ArgumentNullException(nameof(historyCompactor));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
            _snapshotManager.SnapshotChangedAsync += OnSnapshotChangedAsync;
        }

        /// <summary>
        /// Executes the provided prompt against LM Studio and streams the response to WebView2.
        /// </summary>
        public async Task ExecutePromptAsync(string requestJson)
        {
            if (string.IsNullOrWhiteSpace(requestJson))
            {
                InternalLogger.Error("ExecutePromptAsync: requestJson is null or empty");
                return;
            }

            ExecutePromptRequest request = requestJson.FromJson<ExecutePromptRequest>();
            if (request == null)
            {
                InternalLogger.Error("ExecutePromptAsync: deserialized request is null");
                return;
            }

            try
            {
                var context = new GenerateStreamContext
                {
                    Prompt = request.Prompt,
                    ActiveDocumentContent = request.IncludeContent ? await _activeDocumentTool.GetContentAsync() : null,
                    AdditionalPrompt = request.AdditionalPrompt,
                    ModelId = request.ModelId,
                    Temperature = request.Temperature
                };

                async Task OnMessageAsync(WebView2ScriptMessage message)
                {
                    await _scriptExecutor.PostMessageAsJsonAsync(message).ConfigureAwait(false);
                }

                if (!await _sessionManager.TryStartSessionAsync(
                    context,
                    OnMessageAsync,
                    cancellationToken: CancellationToken.None).ConfigureAwait(false))
                {
                    InternalLogger.Info("ExecutePromptAsync: Session already running");
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error("ExecutePromptAsync failed", ex);
            }
        }


        /// <summary>
        /// Resets the chat history with the specified action.
        /// Returns false if a session is running, true if successful.
        /// </summary>
        public Task<bool> ResetHistoryWithActionAsync(string action)
        {
            try
            {
                if (_sessionManager.IsSessionRunning)
                {
                    InternalLogger.Info("ResetHistoryWithActionAsync: Cannot reset while session is running");
                    return Task.FromResult(false);
                }

                switch (action)
                {
                    case "last-prompt":
                        _chatHistoryManager.MoveLastExchangeToNewSession();
                        InternalLogger.Info("ResetHistoryWithActionAsync: Moved last exchange to new session");
                        break;
                    case "last-exchange":
                        _chatHistoryManager.ConsolidateLastExchange();
                        InternalLogger.Info("ResetHistoryWithActionAsync: Consolidated last exchange");
                        break;
                    default:
                        _chatHistoryManager.Clear();
                        InternalLogger.Info("ResetHistoryWithActionAsync: History cleared successfully");
                        break;
                }

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("ResetHistoryWithActionAsync failed", ex);
                return Task.FromResult(false);
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

                _chatHistoryManager.Clear();

                if (!string.IsNullOrWhiteSpace(summary))
                {
                    _chatHistoryManager.AddUserMessage("Provide a brief summary of our previous session to continue.");
                    _chatHistoryManager.AddAssistantMessage(summary, null);
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
        /// Stops the current text generation process and active tools.
        /// </summary>
        public Task StopExecutionAsync()
        {
            InternalLogger.Info("StopExecutionAsync called");
            _sessionManager.TryStopSession();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Copies the specified text to the clipboard.
        /// </summary>
        public async Task<bool> CopyToClipboardAsync(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return false;
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                System.Windows.Clipboard.SetText(text);
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("CopyToClipboardAsync failed", ex);
                return false;
            }
        }

        public async Task<bool> GetSnapshotAsync()
        {
            try
            {
                await _snapshotManager.LoadSnapshotAsync().ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetSnapshotAsync failed", ex);
                return false;
            }
        }

        public async Task<bool> DiscardChangesAsync()
        {
            try
            {
                await _snapshotManager.RollbackAllAsync().ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("DiscardChangesAsync failed", ex);
                return false;
            }
        }

        public async Task<bool> AcceptChangesAsync()
        {
            try
            {
                await _snapshotManager.CommitAllAsync().ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("AcceptChangesAsync failed", ex);
                return false;
            }
        }

        public async Task<bool> ReviewFileAsync(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    return false;

                var leftPath = await _snapshotManager.GetSnapshotFilePathAsync(filePath).ConfigureAwait(false);
                var rightPath = _snapshotManager.GetCurrentFilePath(filePath);
                var tmpDirectory = _snapshotManager.GetTmpDirectoryPath();

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                await DiffViewer.ShowDiffAsync(leftPath, rightPath, tmpDirectory).ConfigureAwait(false);

                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("ReviewFileDiffAsync failed", ex);
                return false;
            }
        }

        public async Task<bool> ReviewAllFilesAsync(string filePathsJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePathsJson))
                    return false;

                var filePaths = JsonConvert.DeserializeObject<string[]>(filePathsJson);
                if (filePaths == null || filePaths.Length == 0)
                    return false;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                var tmpDirectory = _snapshotManager.GetTmpDirectoryPath();

                foreach (var relativePath in filePaths)
                {
                    if (string.IsNullOrWhiteSpace(relativePath))
                        continue;

                    var leftPath = await _snapshotManager.GetSnapshotFilePathAsync(relativePath).ConfigureAwait(false);
                    var rightPath = _snapshotManager.GetCurrentFilePath(relativePath);

                    await DiffViewer.ShowDiffAsync(leftPath, rightPath, tmpDirectory).ConfigureAwait(false);
                }

                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("ReviewAllFilesAsync failed", ex);
                return false;
            }
        }

        public async Task<bool> OpenAllFilesAsync(string filePathsJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePathsJson))
                    return false;

                var filePaths = JsonConvert.DeserializeObject<string[]>(filePathsJson);
                if (filePaths == null || filePaths.Length == 0)
                    return false;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                foreach (var relativePath in filePaths)
                {
                    if (string.IsNullOrWhiteSpace(relativePath))
                        continue;

                    var absolutePath = _snapshotManager.GetCurrentFilePath(relativePath);
                    if (absolutePath == null)
                        continue;

                    await FileViewer.OpenFileAsync(absolutePath).ConfigureAwait(false);
                }

                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("OpenAllFilesAsync failed", ex);
                return false;
            }
        }

        public async Task<bool> DiscardFileAsync(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    return false;

                await _snapshotManager.RollbackFilesAsync(new[] { filePath }).ConfigureAwait(false);

                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("DiscardFileAsync failed", ex);
                return false;
            }
        }

        public async Task<bool> AcceptFileAsync(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    return false;

                await _snapshotManager.CommitFilesAsync(new[] { filePath }).ConfigureAwait(false);

                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("AcceptFileAsync failed", ex);
                return false;
            }
        }

        private async Task OnSnapshotChangedAsync(IReadOnlyList<SnapshotFileChange> changedFiles)
        {
            var message = new WebView2SnapshotMessage
            {
                ChangedFiles = changedFiles.ToList(),
            };
            await _scriptExecutor.PostMessageAsJsonAsync(message).ConfigureAwait(false);
        }
    }
}
