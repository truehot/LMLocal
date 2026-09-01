using System;
using System.Collections.Generic;

namespace LMLocal.Infrastructure.Tooling
{
    /// <summary>
    /// Immutable snapshot of a tool queue for a concrete LLM context.
    /// </summary>
    public sealed class ToolQueue
    {
        /// <summary>
        /// Creates a queue for the main chat.
        /// </summary>
        public static ToolQueue Main(IReadOnlyList<ToolDefinition> definitions)
        {
            return new ToolQueue(null, definitions);
        }

        /// <summary>
        /// Creates a queue for a SubAgent run.
        /// </summary>
        public static ToolQueue ForSubAgent(string subAgentName, IReadOnlyList<ToolDefinition> definitions)
        {
            if (string.IsNullOrWhiteSpace(subAgentName))
                throw new ArgumentException("SubAgent name is required.", nameof(subAgentName));

            return new ToolQueue(subAgentName, definitions);
        }

        /// <summary>
        /// True when this queue belongs to a SubAgent run, false for the main chat.
        /// </summary>
        public bool IsSubAgent { get; }

        /// <summary>
        /// SubAgent name for sub-agent queues; null for the main chat.
        /// </summary>
        public string SubAgentName { get; }

        /// <summary>
        /// Tool definitions visible in this queue.
        /// </summary>
        public IReadOnlyList<ToolDefinition> Definitions { get; }

        private ToolQueue(string subAgentName, IReadOnlyList<ToolDefinition> definitions)
        {
            SubAgentName = subAgentName;
            IsSubAgent = !string.IsNullOrEmpty(subAgentName);
            Definitions = definitions ?? Array.Empty<ToolDefinition>();
        }

        /// <summary>
        /// True when the tool is present in this queue (case-insensitive).
        /// </summary>
        public bool Allows(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return false;

            foreach (var def in Definitions)
            {
                if (def != null &&
                    string.Equals(def.Name, toolName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
