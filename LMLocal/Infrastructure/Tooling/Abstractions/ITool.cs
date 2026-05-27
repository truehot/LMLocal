namespace LMLocal.Infrastructure.Tooling.Abstractions
{
    /// <summary>
    /// Provides a contract for implementing tools used for integration with an LLM
    /// (Large Language Model). Implementations must return the tool's metadata
    /// and expose its unique name.
    /// </summary>
    public interface ITool
    {
        /// <summary>
        /// Returns metadata required to interact with the LLM. The <see cref="ToolDefinition"/>
        /// may include a description, available commands, valid parameters and other
        /// information used when invoking the tool.
        /// </summary>
        ToolDefinition GetToolInfo();

        /// <summary>
        /// Unique identifier for the tool. This should remain stable across versions so
        /// the system can reliably associate stored configurations and logs with a
        /// specific tool.
        /// </summary>
        string ToolName { get; }
    }
}
