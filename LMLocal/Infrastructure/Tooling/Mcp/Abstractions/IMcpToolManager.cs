using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.Mcp.Models;

namespace LMLocal.Infrastructure.Tooling.Mcp.Abstractions
{
    /// <summary>
    /// Manages MCP (Model Context Protocol) server connections and tool discovery.
    /// </summary>
    public interface IMcpToolManager
    {
        /// <summary>
        /// Initializes or refreshes MCP server connections based on configuration.
        /// </summary>
        Task RefreshServersAsync(McpConfigFile config, CancellationToken cancellationToken);

        /// <summary>
        /// Tests connection to a specific MCP server and returns its available tools.
        /// </summary>
        Task<IReadOnlyList<ToolDefinition>> TestConnectionAsync(
            McpServerConfig serverConfig,
            CancellationToken cancellationToken);

        /// <summary>
        /// Gets all cached tool definitions from active MCP servers.
        /// </summary>
        IReadOnlyList<ToolDefinition> GetMcpToolDefinitions();

        /// <summary>
        /// Checks if a tool with the given name exists in the cache.
        /// </summary>
        bool ToolExists(string toolName);

        /// <summary>
        /// Retrieves a cached MCP tool by name.
        /// </summary>
        McpDynamicTool GetTool(string toolName);

        /// <summary>
        /// Gets information about active MCP servers.
        /// </summary>
        IReadOnlyList<McpServerStatus> GetServerStatuses();

        /// <summary>
        /// Disconnects from a specific MCP server and removes its tools from cache.
        /// </summary>
        Task DisconnectAsync(string serverName, CancellationToken cancellationToken);
    }
}
