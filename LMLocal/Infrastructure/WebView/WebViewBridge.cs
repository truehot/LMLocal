using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Application.ChatSession;
using LMLocal.Application.ModelsList;
using LMLocal.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Instructions;
using LMLocal.Infrastructure.Mcp;
using LMLocal.Infrastructure.Providers;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Infrastructure.WebView.Models;
using LMLocal.Models;
using Microsoft.VisualStudio.Shell;

namespace LMLocal.Infrastructure.WebView
{
    /// <summary>
    /// Bridge class for communication between WebView2 and backend logic.
    /// </summary>
    public interface IWebViewBridge
    {
        Task<bool> CopyToClipboardAsync(string text);
        Task ExecutePromptAsync(string requestJson);
        Task<string> GetSettingsAsync();
        Task<string> ListModelsAsync();
        Task<bool> SetActiveModelAsync(string modelId, int contextLength);
        Task<bool> ResetHistoryAsync();
        Task StopExecutionAsync();
        Task<bool> UpdateSettingsAsync(string newSettingsJson);
        Task<string> GetInstructionsAsync();
        Task<bool> UpdateInstructionsAsync(string newInstructionsJson);
        Task<string> TestConnectionAsync(string payload);
        Task<string> GetMcpConfigAsync();
        Task<bool> UpdateMcpConfigAsync(string newMcpConfigJson);
        Task<string> TestMcpConnectionAsync(string payload);
        Task<string> GetProvidersAsync();
        Task<bool> UpdateProvidersAsync(string providersConfigJson);
    }



    [ComVisible(true)]
    public class WebViewBridge : IWebViewBridge
    {
        private readonly IModelsListService _modelsListService;
        private readonly IWebViewScriptExecutor _scriptExecutor;
        private readonly IInstructionsManager _instructionsManager;
        private readonly IMcpConfigManager _mcpConfigManager;
        private readonly IMcpToolManager _mcpToolManager;
        private readonly ISettingsManager _settingsManager;
        private readonly IActiveDocument _activeDocumentTool;
        private readonly ISessionManager _sessionManager;
        private readonly IActiveModelContext _activeModelContext;
        private readonly IChatHistoryManager _chatHistoryManager;
        private readonly IProvidersConfigManager _providersConfigManager;

        internal WebViewBridge(
            ISettingsManager settingsManager,
            IModelsListService modelsListService,
            IWebViewScriptExecutor scriptExecutor,
            IInstructionsManager instructionsManager,
            IMcpConfigManager mcpConfigManager,
            IMcpToolManager mcpToolManager,
            IProvidersConfigManager providersConfigManager,
            IActiveDocument activeDocumentTool,
            ISessionManager sessionManager,
            IActiveModelContext activeModelContext,
            IChatHistoryManager chatHistoryManager)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _scriptExecutor = scriptExecutor ?? throw new ArgumentNullException(nameof(scriptExecutor));
            _modelsListService = modelsListService ?? throw new ArgumentNullException(nameof(modelsListService));
            _activeDocumentTool = activeDocumentTool ?? throw new ArgumentNullException(nameof(activeDocumentTool));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _activeModelContext = activeModelContext ?? throw new ArgumentNullException(nameof(activeModelContext));
            _chatHistoryManager = chatHistoryManager ?? throw new ArgumentNullException(nameof(chatHistoryManager));
            _instructionsManager = instructionsManager ?? throw new ArgumentNullException(nameof(instructionsManager));
            _mcpConfigManager = mcpConfigManager ?? throw new ArgumentNullException(nameof(mcpConfigManager));
            _mcpToolManager = mcpToolManager ?? throw new ArgumentNullException(nameof(mcpToolManager));
            _providersConfigManager = providersConfigManager ?? throw new ArgumentNullException(nameof(providersConfigManager));
        }

        /// <summary>
        /// Returns list of available models from different providers and activeModel if any.
        /// </summary>
        public async Task<string> ListModelsAsync()
        {
            var requestTimeout = _settingsManager.RequestTimeoutSeconds;
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(requestTimeout)))
                {
                    var response = await _modelsListService.ListModelsAsync(_activeModelContext.CurrentModelId, cts.Token).ConfigureAwait(false);
                    return response == null ? "{}" : response.ToJson();
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error("ListModelsAsync failed", ex);
                return new { Error = "Failed to list models: " + ex.Message }.ToJson();
            }
        }

        /// <summary>
        /// Sets the active model and its max context length. If contextLength is not provided or <= 0, defaults to 16384.
        /// </summary>
        public Task<bool> SetActiveModelAsync(string modelId, int contextLength)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(modelId)) return Task.FromResult(false);

                var maxContext = contextLength <= 0 ? 16384 : contextLength;
                _activeModelContext.SetActiveModel(modelId, maxContext);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("SetActiveModelAsync failed", ex);
                return Task.FromResult(false);
            }
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
        /// Resets the chat history. Returns false if a session is running, true if successful.
        /// </summary>
        public Task<bool> ResetHistoryAsync()
        {
            try
            {
                if (_sessionManager.IsSessionRunning)
                {
                    InternalLogger.Info("ResetHistoryAsync: Cannot reset while session is running");
                    return Task.FromResult(false);
                }

                _chatHistoryManager.Clear();
                InternalLogger.Info("ResetHistoryAsync: History cleared successfully");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("ResetHistoryAsync failed", ex);
                return Task.FromResult(false);
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

        public Task<string> GetSettingsAsync()
        {
            try
            {
                return Task.FromResult(_settingsManager.Current.ToJson());
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetSettingsAsync failed", ex);
                return Task.FromResult<string>(null);
            }
        }

        public async Task<bool> UpdateSettingsAsync(string newSettingsJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(newSettingsJson))
                {
                    return false;
                }

                var newSettings = newSettingsJson.FromJson<AppSettings>();

                await _settingsManager.SaveAsync(newSettings).ConfigureAwait(false);

                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("UpdateSettingsAsync failed", ex);
                return false;
            }
        }

        public async Task<string> GetInstructionsAsync()
        {
            try
            {
                return await _instructionsManager.GetAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetInstructionsAsync failed", ex);
                return "{}";
            }
        }

        public async Task<bool> UpdateInstructionsAsync(string newInstructionsJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(newInstructionsJson))
                {
                    return false;
                }

                await _instructionsManager.UpdateAsync(newInstructionsJson).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("UpdateInstructionsAsync failed", ex);
                return false;
            }
        }

        public async Task<string> TestConnectionAsync(string payload)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(payload))
                    return new { success = false, error = "Invalid parameters" }.ToJson();

                var request = payload.FromJson<TestConnectionRequest>();
                if (request == null || string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.Url))
                    return new { success = false, error = "Provider and URL are required" }.ToJson();

                var requestTimeout = _settingsManager.RequestTimeoutSeconds;
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(requestTimeout)))
                {
                    bool success = await _modelsListService.TestConnectionAsync(
                        request.Url,
                        request.Provider,
                        request.ApiKey ?? string.Empty,
                        cts.Token
                    ).ConfigureAwait(false);

                    return new { success, error = success ? (string)null : "Failed to connect" }.ToJson();
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error("TestConnectionAsync failed", ex);
                return new { success = false, error = ex.Message }.ToJson();
            }
        }

        public async Task<string> GetMcpConfigAsync()
        {
            try
            {
                var config = await _mcpConfigManager.GetAsync().ConfigureAwait(false);
                return config?.ToJson() ?? "{}";
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetMcpConfigAsync failed", ex);
                return "{}";
            }
        }

        public async Task<bool> UpdateMcpConfigAsync(string newMcpConfigJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(newMcpConfigJson))
                {
                    return false;
                }

                var config = newMcpConfigJson.FromJson<McpConfigFile>();
                if (config == null)
                {
                    return false;
                }

                await _mcpConfigManager.UpdateAsync(config).ConfigureAwait(false);

                try
                {
                    await _mcpToolManager.RefreshServersAsync(config, CancellationToken.None)
                        .ConfigureAwait(false);
                    InternalLogger.Info("MCP servers refreshed after configuration update");
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"Failed to refresh MCP servers after config update: {ex.Message}");
                }

                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("UpdateMcpConfigAsync failed", ex);
                return false;
            }
        }

        public async Task<string> TestMcpConnectionAsync(string payload)
        {
            var response = new McpTestConnectionResponse();

            try
            {
                if (string.IsNullOrWhiteSpace(payload))
                {
                    response.Error = "Payload is required";
                    return response.ToJson();
                }

                var config = payload.FromJson<McpConfigFile>();
                if (config == null)
                {
                    response.Error = "Invalid MCP configuration format";
                    return response.ToJson();
                }

                var serversConfig = config.GetServersConfig();
                if (serversConfig?.Servers == null || serversConfig.Servers.Count == 0)
                {
                    response.Error = "No servers configured in MCP config";
                    return response.ToJson();
                }

                var requestTimeout = _settingsManager.RequestTimeoutSeconds;

                foreach (var serverEntry in serversConfig.Servers)
                {
                    var serverName = serverEntry.Key;
                    var serverConfig = serverEntry.Value;
                    var result = new McpServerTestResult { ServerName = serverName };

                    try
                    {
                        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(requestTimeout)))
                        {
                            var tools = await _mcpToolManager.TestConnectionAsync(serverConfig, cts.Token)
                                .ConfigureAwait(false);

                            result.Tools = new List<DiscoveredTool>();
                            foreach (var t in tools)
                            {
                                result.Tools.Add(new DiscoveredTool
                                {
                                    Name = t.Name,
                                    Description = t.Description
                                });
                            }
                            response.HasSuccesses = true;
                        }
                    }
                    catch (OperationCanceledException ex)
                    {
                        InternalLogger.Error($"TestMcpConnectionAsync timed out for server '{serverName}'", ex);
                        result.Error = $"Connection timed out after {requestTimeout} seconds";
                        response.HasErrors = true;
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Error($"TestMcpConnectionAsync failed for server '{serverName}'", ex);
                        result.Error = ex.Message;
                        response.HasErrors = true;
                    }

                    response.Servers.Add(result);
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error("TestMcpConnectionAsync failed with unexpected error", ex);
                response.Error = $"Unexpected error: {ex.Message}";
            }

            return response.ToJson();
        }

        public async Task<string> GetProvidersAsync()
        {
            try
            {
                var config = await _providersConfigManager.GetAsync().ConfigureAwait(false);
                return config?.ToJson() ?? "{}";
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetProvidersAsync failed", ex);
                return "{}";
            }
        }

        public async Task<bool> UpdateProvidersAsync(string providersConfigJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(providersConfigJson))
                {
                    return false;
                }

                var config = providersConfigJson.FromJson<ProvidersConfigFile>();
                if (config == null)
                {
                    return false;
                }

                await _providersConfigManager.UpdateAsync(config).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("UpdateProvidersAsync failed", ex);
                return false;
            }
        }
    }
}
