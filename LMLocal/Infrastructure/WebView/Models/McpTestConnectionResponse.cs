using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.WebView.Models
{
    /// <summary>
    /// Response from testing MCP server connections.
    /// Contains list of per-server results (success or error).
    /// </summary>
    public class McpTestConnectionResponse
    {
        /// <summary>
        /// List of test results for each server.
        /// </summary>
        [JsonProperty("servers")]
        public List<McpServerTestResult> Servers { get; set; } = new List<McpServerTestResult>();

        /// <summary>
        /// Overall error message if testing failed at a higher level (e.g., invalid config).
        /// Null if all servers were tested (even if some failed individually).
        /// </summary>
        [JsonProperty("error")]
        public string Error { get; set; }

        /// <summary>
        /// True if at least one server was successfully tested.
        /// False if all servers failed or testing was not performed.
        /// </summary>
        [JsonProperty("hasSuccesses")]
        public bool HasSuccesses { get; set; }

        /// <summary>
        /// True if at least one server test resulted in an error.
        /// False if all servers succeeded or testing was not performed.
        /// </summary>
        [JsonProperty("hasErrors")]
        public bool HasErrors { get; set; }
    }

    /// <summary>
    /// Result of testing a single MCP server connection.
    /// </summary>
    public class McpServerTestResult
    {
        /// <summary>
        /// Name of the server that was tested.
        /// </summary>
        [JsonProperty("serverName")]
        public string ServerName { get; set; }

        /// <summary>
        /// List of tools discovered from the server (if connection succeeded).
        /// Null if connection failed.
        /// </summary>
        [JsonProperty("tools")]
        public List<DiscoveredTool> Tools { get; set; }

        /// <summary>
        /// Error message if connection failed.
        /// Null if connection succeeded.
        /// </summary>
        [JsonProperty("error")]
        public string Error { get; set; }
    }

    /// <summary>
    /// Information about a tool discovered from an MCP server.
    /// </summary>
    public class DiscoveredTool
    {
        /// <summary>
        /// Name of the tool.
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// Description of what the tool does.
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; }
    }
}
