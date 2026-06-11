using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.Mcp.Models;

namespace LMLocal.Infrastructure.Tooling.Mcp.Abstractions
{
    /// <summary>
    /// Base interface for MCP (Model Context Protocol) client implementations.
    /// Handles JSON-RPC communication with MCP servers over various transports.
    /// 
    /// IMPORTANT: This interface does NOT implement IDisposable or IAsyncDisposable.
    /// Clients must explicitly call CloseAsync() to properly clean up server-side resources.
    /// 
    /// Usage pattern:
    /// <code>
    /// var client = new HttpMcpClient(...);
    /// try
    /// {
    ///     await client.InitializeAsync(cancellationToken);
    ///     // ... perform operations
    /// }
    /// finally
    /// {
    ///     await client.CloseAsync(CancellationToken.None);
    /// }
    /// </code>
    /// </summary>
    public interface IMcpClient
    {
        /// <summary>
        /// Initializes connection to the MCP server.
        /// Must be called before any other operations.
        /// </summary>
        Task InitializeAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Lists all available tools from the server.
        /// </summary>
        Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Calls a tool on the server with the given input parameters.
        /// </summary>
        Task<object> CallToolAsync(string toolName, Dictionary<string, object> parameters, CancellationToken cancellationToken);

        /// <summary>
        /// Closes the connection to the server and cleans up server-side resources.
        /// 
        /// MUST be called in a finally block to ensure proper cleanup, even if an exception occurs.
        /// Passing CancellationToken.None ensures the shutdown request is always sent.
        /// </summary>
        Task CloseAsync(CancellationToken cancellationToken);
    }
}
