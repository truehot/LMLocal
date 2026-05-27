using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.Abstractions;

namespace LMLocal.Infrastructure.Mcp
{
    /// <summary>
    /// Dynamic wrapper for MCP server tools that implements IVsTool interface.
    /// </summary>
    public class McpDynamicTool : ITool
    {
        private readonly string _serverId;
        private readonly ToolDefinition _definition;
        private readonly Func<Dictionary<string, object>, CancellationToken, Task<object>> _executeDelegate;

        /// <summary>
        /// Gets the unique name of this tool (from ToolDefinition).
        /// </summary>
        public string ToolName => _definition?.Name ?? "unknown";

        /// <summary>
        /// Creates a new MCP dynamic tool wrapper.
        /// </summary>
        public McpDynamicTool(
            string serverId,
            ToolDefinition definition,
            Func<Dictionary<string, object>, CancellationToken, Task<object>> executeDelegate)
        {
            if (string.IsNullOrWhiteSpace(serverId))
                throw new ArgumentException("Server ID cannot be empty", nameof(serverId));

            _serverId = serverId;
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _executeDelegate = executeDelegate ?? throw new ArgumentNullException(nameof(executeDelegate));
        }

        /// <summary>
        /// Returns the tool definition for use by the LLM.
        /// </summary>
        public ToolDefinition GetToolInfo()
        {
            return _definition;
        }

        /// <summary>
        /// Executes the tool via the MCP server using the provided parameters.
        /// </summary>
        public async Task<object> ExecuteAsync(
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            return await _executeDelegate(parameters, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the server ID that provides this tool.
        /// </summary>
        public string GetServerId() => _serverId;

        public override string ToString()
        {
            return $"McpDynamicTool({ToolName}, server={_serverId})";
        }
    }
}
