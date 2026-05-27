using LMLocal.Infrastructure.Mcp;

namespace LMLocal.Infrastructure.Tooling.Mcp.Models
{
    /// <summary>
    /// Metadata about an MCP tool including its definition and server information.
    /// </summary>
    public class McpToolInfo
    {
        /// <summary>
        /// The wrapped MCP tool implementing IVsTool.
        /// </summary>
        public McpDynamicTool Tool { get; set; }

        /// <summary>
        /// Name of the MCP server providing this tool.
        /// </summary>
        public string ServerName { get; set; }

        /// <summary>
        /// Transport type used to communicate with the server (stdio, http, sse).
        /// </summary>
        public string TransportType { get; set; }

        /// <summary>
        /// Server ID from the configuration.
        /// </summary>
        public string ServerId { get; set; }
    }
}
