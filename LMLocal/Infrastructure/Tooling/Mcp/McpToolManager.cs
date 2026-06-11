using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.HttpWrapper;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Tooling.Mcp.Abstractions;
using LMLocal.Infrastructure.Tooling.Mcp.Client;
using LMLocal.Infrastructure.Tooling.Mcp.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Infrastructure.Tooling.Mcp
{
    /// <summary>
    /// Manages MCP server connections and caches available tools.
    /// </summary>
    internal class McpToolManager : IMcpToolManager
    {
        private readonly IMcpConfigManager _mcpConfigManager;
        private readonly IFileSystem _fileSystem;
        private readonly IHttpClientWrapper _httpClientWrapper;
        private readonly ISettingsManager _settingsManager;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        private readonly ConcurrentDictionary<string, McpToolInfo> _mcpToolsCache =
            new ConcurrentDictionary<string, McpToolInfo>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, McpServerStatus> _serverStatuses =
            new ConcurrentDictionary<string, McpServerStatus>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, McpServerConfig> _activeServers =
            new Dictionary<string, McpServerConfig>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, IMcpClient> _activeClients =
            new Dictionary<string, IMcpClient>(StringComparer.OrdinalIgnoreCase);

        public McpToolManager(
            IMcpConfigManager mcpManager,
            IFileSystem fileSystem,
            IHttpClientWrapper httpClientWrapper,
            ISettingsManager settingsManager)
        {
            _mcpConfigManager = mcpManager ?? throw new ArgumentNullException(nameof(mcpManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
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

                bool enableMcp = config.EnableMcp;
                McpClientConfig serversConfig = config.GetServersConfig();

                if (!enableMcp)
                {
                    InternalLogger.Debug("MCP is disabled, disconnecting all servers");
                    var serversToRemove = _activeServers.Keys.ToList();
                    foreach (var serverName in serversToRemove)
                    {
                        await DisconnectServerAsync(serverName, cancellationToken).ConfigureAwait(false);
                    }
                    _mcpToolsCache.Clear();
                    _serverStatuses.Clear();
                    _activeServers.Clear();
                    return;
                }

                var serversToRemoveObsolete = _activeServers.Keys
                    .Where(serverName => serversConfig?.Servers == null || !serversConfig.Servers.ContainsKey(serverName))
                    .ToList();

                foreach (var serverName in serversToRemoveObsolete)
                {
                    await DisconnectServerAsync(serverName, cancellationToken).ConfigureAwait(false);
                }

                _mcpToolsCache.Clear();
                _serverStatuses.Clear();
                _activeServers.Clear();

                if (serversConfig?.Servers != null && serversConfig.Servers.Count > 0)
                {
                    var connectTasks = serversConfig.Servers
                        .Where(serverEntry => !serverEntry.Value.Disabled)
                        .Select(serverEntry => ConnectAndCacheToolsAsync(serverEntry.Key, serverEntry.Value, cancellationToken))
                        .ToList();

                    await Task.WhenAll(connectTasks).ConfigureAwait(false);
                }
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
                    await client.CloseAsync(CancellationToken.None).ConfigureAwait(false);
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
                await DisconnectServerAsync(serverName, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Internal helper: connects to a server and caches its tools.
        /// </summary>
        private async Task ConnectAndCacheToolsAsync(
            string serverName,
            McpServerConfig serverConfig,
            CancellationToken cancellationToken)
        {
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

                var client = CreateMcpClient(serverConfig);
                await client.InitializeAsync(cancellationToken).ConfigureAwait(false);

                var tools = await client.ListToolsAsync(cancellationToken).ConfigureAwait(false);


                _activeClients[serverName] = client;
                int toolCount = 0;

                var serverPermissions = serverConfig.Permissions ?? new Dictionary<string, string>();

                foreach (var tool in tools)
                {
                    if (serverPermissions.TryGetValue(tool.Name, out var permission) && permission == "disable")
                    {
                        InternalLogger.Debug($"Tool '{tool.Name}' is disabled by server permissions, skipping");
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

                    var toolInfo = new McpToolInfo
                    {
                        Tool = dynamicTool,
                        ServerName = serverName,
                        TransportType = transportType,
                        ServerId = serverName
                    };

                    _mcpToolsCache[tool.Name] = toolInfo;
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
            catch (Exception ex)
            {
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

        /// <summary>
        /// Internal helper: disconnects from a server and removes its tools.
        /// </summary>
        private async Task DisconnectServerAsync(string serverName, CancellationToken cancellationToken)
        {
            try
            {
                if (_activeClients.TryGetValue(serverName, out var client))
                {
                    try
                    {
                        await client.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Warn($"Error closing MCP client for server '{serverName}': {ex.Message}");
                    }
                    _activeClients.Remove(serverName);
                }

                _activeServers.Remove(serverName);
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
