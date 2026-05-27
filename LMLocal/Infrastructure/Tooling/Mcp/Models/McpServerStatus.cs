namespace LMLocal.Infrastructure.Tooling.Mcp.Models
{
    /// <summary>
    /// Status information about an MCP server connection.
    /// </summary>
    public class McpServerStatus
    {
        /// <summary>
        /// Name of the server (from configuration).
        /// </summary>
        public string ServerName { get; set; }

        /// <summary>
        /// Current connection status: "connected", "disconnected", "connecting", "error".
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Transport type: stdio, http, sse.
        /// </summary>
        public string TransportType { get; set; }

        /// <summary>
        /// Number of tools available from this server.
        /// </summary>
        public int ToolCount { get; set; }

        /// <summary>
        /// Error message if status is "error".
        /// </summary>
        public string ErrorMessage { get; set; }
    }
}
