using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling;

namespace LMLocal.Application.Abstractions.Ports
{
    /// <summary>
    /// Execution router over the tool sources (built-in VS tools, MCP, SubAgents).
    /// </summary>
    public interface IToolRouter
    {
        /// <summary>
        /// Checks whether a tool with the specified name is registered.
        /// </summary>
        bool ToolExists(string toolName);

        /// <summary>
        /// Executes a tool with the given parameters from LLM response.
        /// </summary>
        Task<object> ExecuteAsync(
            string toolName,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken);

        /// <summary>
        /// Gets processing message for a tool based on its parameters.
        /// </summary>
        string GetProcessingMessage(string toolName, Dictionary<string, object> parameters);

        /// <summary>
        /// Gets completion message for a tool based on its execution result.
        /// </summary>
        string GetCompletionMessage(string toolName, object result);

        /// <summary>
        /// Returns the maximum execution time for a tool, or null when the caller should use the default timeout.
        /// </summary>
        TimeSpan? GetToolTimeout(string toolName);
    }
}
