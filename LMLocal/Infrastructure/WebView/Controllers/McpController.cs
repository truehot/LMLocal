using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Infrastructure.Tooling.Mcp;
using LMLocal.Infrastructure.Tooling.Mcp.Abstractions;
using LMLocal.Infrastructure.WebView.Models;
using LMLocal.Models;

namespace LMLocal.Infrastructure.WebView.Controllers
{
    /// <summary>
    /// Bridge class for communication between WebView2 and backend MCP logic.
    /// </summary>
    public interface IMcpController
    {
        Task<string> GetMcpConfigAsync();
        Task<bool> UpdateMcpConfigAsync(string newMcpConfigJson);
        Task<string> TestMcpConnectionAsync(string payload);
    }

    [System.Runtime.InteropServices.ComVisible(true)]
    public class McpController : IMcpController
    {
        private readonly IMcpConfigManager _mcpConfigManager;
        private readonly IMcpToolManager _mcpToolManager;
        private readonly ISettingsManager _settingsManager;

        internal McpController(IMcpConfigManager mcpConfigManager, IMcpToolManager mcpToolManager, ISettingsManager settingsManager)
        {
            _mcpConfigManager = mcpConfigManager ?? throw new ArgumentNullException(nameof(mcpConfigManager));
            _mcpToolManager = mcpToolManager ?? throw new ArgumentNullException(nameof(mcpToolManager));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
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
    }
}
