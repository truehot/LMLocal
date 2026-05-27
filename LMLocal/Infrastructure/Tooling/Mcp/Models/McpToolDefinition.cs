namespace LMLocal.Infrastructure.Tooling.Mcp.Models
{
    /// <summary>
    /// MCP tool definition received from server.
    /// Maps to LMLocal.Infrastructure.Vs.ToolDefinition for LLM integration.
    /// </summary>
    public class McpToolDefinition
    {
        /// <summary>
        /// Unique tool name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Human-readable tool description.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// JSON Schema object describing tool input parameters.
        /// Converted to ToolParameters format for LLM.
        /// </summary>
        public object InputSchema { get; set; }
    }
}
