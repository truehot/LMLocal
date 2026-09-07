using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.HttpWrapper;
using LMLocal.Infrastructure.Tooling.Mcp.Abstractions;
using LMLocal.Infrastructure.Tooling.Mcp.Client;
using LMLocal.Infrastructure.Tooling.Mcp.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Tooling.Mcp
{
    /// <summary>
    /// Manages MCP server connections and caches available tools.
    /// </summary>
    internal class McpToolManager : IMcpToolManager
    {
        private readonly IHttpClientWrapper _httpClientWrapper;
        private readonly ISettingsManager _settingsManager;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

        private readonly ConcurrentDictionary<string, McpToolInfo> _mcpToolsCache =
            new ConcurrentDictionary<string, McpToolInfo>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, McpServerStatus> _serverStatuses =
            new ConcurrentDictionary<string, McpServerStatus>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, McpServerConfig> _activeServers =
            new ConcurrentDictionary<string, McpServerConfig>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, IMcpClient> _activeClients =
            new ConcurrentDictionary<string, IMcpClient>(StringComparer.OrdinalIgnoreCase);

        public McpToolManager(
            IHttpClientWrapper httpClientWrapper,
            ISettingsManager settingsManager)
        {
            _httpClientWrapper = httpClientWrapper ?? throw new ArgumentNullException(nameof(httpClientWrapper));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        }

        public async Task RefreshServersAsync(McpConfigFile config, CancellationToken cancellationToken)
        {
            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (config == null)
                    config = new McpConfigFile();

                var activeServerNames = _activeServers.Keys
                    .Concat(_activeClients.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var serverName in activeServerNames)
                {
                    await DisconnectServerAsync(serverName).ConfigureAwait(false);
                }

                _mcpToolsCache.Clear();
                _serverStatuses.Clear();

                if (!config.EnableMcp)
                    return;

                var serversConfig = config.GetServersConfig();
                if (serversConfig?.Servers == null || serversConfig.Servers.Count == 0)
                    return;

                var connectTasks = serversConfig.Servers
                    .Where(serverEntry => !serverEntry.Value.Disabled)
                    .Select(serverEntry => ConnectAndCacheToolsAsync(serverEntry.Key, serverEntry.Value, cancellationToken))
                    .ToList();

                await Task.WhenAll(connectTasks).ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<IReadOnlyList<ToolDefinition>> TestConnectionAsync(
            McpServerConfig serverConfig,
            CancellationToken cancellationToken)
        {
            if (serverConfig == null)
                throw new ArgumentNullException(nameof(serverConfig));

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var validationError = serverConfig.Validate();
                if (!string.IsNullOrEmpty(validationError))
                    throw new InvalidOperationException($"Invalid server configuration: {validationError}");

                var client = CreateMcpClient(serverConfig);
                try
                {
                    await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
                    var tools = await client.ListToolsAsync(cancellationToken).ConfigureAwait(false);

                    var definitions = new List<ToolDefinition>();
                    foreach (var t in tools)
                    {
                        definitions.Add(new ToolDefinition
                        {
                            Name = t.Name,
                            Description = t.Description,
                            Parameters = ConvertInputSchemaToToolParameters(t.InputSchema)
                        });
                    }

                    return definitions.AsReadOnly();
                }
                finally
                {
                    await CloseClientSafelyAsync(client).ConfigureAwait(false);
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public IReadOnlyList<ToolDefinition> GetMcpToolDefinitions()
        {
            var definitions = new List<ToolDefinition>();
            foreach (var toolInfo in _mcpToolsCache.Values)
            {
                definitions.Add(toolInfo.Tool.GetToolInfo());
            }
            return definitions.AsReadOnly();
        }

        public bool ToolExists(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return false;

            return _mcpToolsCache.ContainsKey(toolName);
        }

        public McpDynamicTool GetTool(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                throw new ArgumentException("Tool name cannot be empty", nameof(toolName));

            if (_mcpToolsCache.TryGetValue(toolName, out var toolInfo))
                return toolInfo.Tool;

            throw new ArgumentException($"MCP tool '{toolName}' not found in cache", nameof(toolName));
        }

        public IReadOnlyList<McpServerStatus> GetServerStatuses()
        {
            return _serverStatuses.Values.ToList().AsReadOnly();
        }

        public async Task DisconnectAsync(string serverName, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(serverName))
                throw new ArgumentException("Server name cannot be empty", nameof(serverName));

            await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await DisconnectServerAsync(serverName).ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task ConnectAndCacheToolsAsync(
            string serverName,
            McpServerConfig serverConfig,
            CancellationToken cancellationToken)
        {
            IMcpClient client = null;

            try
            {
                var validationError = serverConfig.Validate();
                if (!string.IsNullOrEmpty(validationError))
                {
                    _serverStatuses[serverName] = new McpServerStatus
                    {
                        ServerName = serverName,
                        Status = "error",
                        TransportType = serverConfig.ResolveTransportType(),
                        ToolCount = 0,
                        ErrorMessage = validationError
                    };
                    return;
                }

                _activeServers[serverName] = serverConfig;
                var transportType = serverConfig.ResolveTransportType();

                client = CreateMcpClient(serverConfig);
                await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
                var tools = await client.ListToolsAsync(cancellationToken).ConfigureAwait(false);

                _activeClients[serverName] = client;

                int toolCount = 0;
                var serverPermissions = serverConfig.Permissions ?? new Dictionary<string, string>();

                foreach (var tool in tools)
                {
                    if (serverPermissions.TryGetValue(tool.Name, out var permission) && permission == "disable")
                    {
                        continue;
                    }

                    var definition = new ToolDefinition
                    {
                        Name = tool.Name,
                        Description = tool.Description,
                        Parameters = ConvertInputSchemaToToolParameters(tool.InputSchema)
                    };

                    var dynamicTool = new McpDynamicTool(
                        serverName,
                        definition,
                        async (parameters, ct) => await client.CallToolAsync(tool.Name, parameters, ct)
                    );

                    _mcpToolsCache[tool.Name] = new McpToolInfo
                    {
                        Tool = dynamicTool,
                        ServerName = serverName,
                        TransportType = transportType,
                        ServerId = serverName
                    };

                    toolCount++;
                }

                _serverStatuses[serverName] = new McpServerStatus
                {
                    ServerName = serverName,
                    Status = "connected",
                    TransportType = transportType,
                    ToolCount = toolCount,
                    ErrorMessage = null
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await CleanupFailedServerAsync(serverName, client).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await CleanupFailedServerAsync(serverName, client).ConfigureAwait(false);

                _serverStatuses[serverName] = new McpServerStatus
                {
                    ServerName = serverName,
                    Status = "error",
                    TransportType = serverConfig.ResolveTransportType(),
                    ToolCount = 0,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task CleanupFailedServerAsync(string serverName, IMcpClient client)
        {
            if (client != null)
            {
                await CloseClientSafelyAsync(client).ConfigureAwait(false);
            }

            _activeServers.TryRemove(serverName, out _);
            _activeClients.TryRemove(serverName, out _);

            var toolsToRemove = _mcpToolsCache
                .Where(kvp => kvp.Value.ServerName == serverName)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var toolName in toolsToRemove)
            {
                _mcpToolsCache.TryRemove(toolName, out _);
            }
        }

        private async Task DisconnectServerAsync(string serverName)
        {
            try
            {
                if (_activeClients.TryRemove(serverName, out var client))
                {
                    await CloseClientSafelyAsync(client).ConfigureAwait(false);
                }

                _activeServers.TryRemove(serverName, out _);
                _serverStatuses.TryRemove(serverName, out _);

                var toolsToRemove = _mcpToolsCache
                    .Where(kvp => kvp.Value.ServerName == serverName)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var toolName in toolsToRemove)
                {
                    _mcpToolsCache.TryRemove(toolName, out _);
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error disconnecting from server '{serverName}': {ex.Message}", ex);
            }
        }

        private async Task CloseClientSafelyAsync(IMcpClient client)
        {
            if (client == null)
                return;

            using (var timeoutCts = new CancellationTokenSource(CloseTimeout))
            {
                try
                {
                    await client.CloseAsync(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    InternalLogger.Warn($"Closing MCP client timed out after {CloseTimeout.TotalSeconds} seconds.");
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"Error closing MCP client: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Creates appropriate MCP client based on server configuration.
        /// Supports stdio (subprocess), HTTP, and Streamable-HTTP transports.
        /// </summary>
        private IMcpClient CreateMcpClient(McpServerConfig config)
        {
            var transportType = config.ResolveTransportType();

            switch (transportType.ToLowerInvariant())
            {
                case "stdio":
                    return new StdioMcpClient(config.Command, config.Args, config.Env);

                case "http":
                case "streamable-http":
                    var headers = config.Headers;
                    var requestTimeout = TimeSpan.FromSeconds(_settingsManager.RequestTimeoutSeconds);
                    return new HttpMcpClient(config.Url, _httpClientWrapper, headers, config.Token, requestTimeout);

                default:
                    throw new InvalidOperationException($"Unsupported transport type: {transportType}");
            }
        }

        /// <summary>
        /// Converts MCP InputSchema (JSON object) to LMLocal ToolParameters format.
        /// </summary>
        private ToolParameters ConvertInputSchemaToToolParameters(object inputSchema)
        {
            if (inputSchema == null)
                return new ToolParameters { Type = "object" };

            try
            {
                if (inputSchema is JObject jObject)
                {
                    var toolParams = jObject.ToObject<ToolParameters>();
                    return toolParams ?? new ToolParameters { Type = "object" };
                }

                var json = JsonConvert.SerializeObject(inputSchema);
                var toolParams2 = JsonConvert.DeserializeObject<ToolParameters>(json);
                return toolParams2 ?? new ToolParameters { Type = "object" };
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"ConvertInputSchemaToToolParameters: failed to convert input schema: {ex.Message}");
                return new ToolParameters { Type = "object" };
            }
        }
    }
}
