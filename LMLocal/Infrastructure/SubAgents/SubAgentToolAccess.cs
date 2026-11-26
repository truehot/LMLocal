using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs;

namespace LMLocal.Infrastructure.SubAgents
{
    /// <summary>
    /// Answers whether an agent may run next to other agents at the same time. Three places need the same answer and
    /// must never disagree: the config validation, the queue/dialog availability and the pipeline request factory.
    /// </summary>
    internal static class SubAgentToolAccess
    {
        /// <summary>
        /// True when the agent uses no write tools (and no unknown tools), so it may run in a group whose branches
        /// execute at the same time: concurrent edits of the same files plus one snapshot batch are not safe.
        /// </summary>
        internal static bool HasOnlyReadOnlyTools(SubAgentDefinition agent, IBuiltInVsToolProvider builtInTools)
        {
            if (agent == null)
                return true;

            var allowed = agent.AllowedTools;
            if (allowed == null || allowed.Count == 0)
                return true; // reasoning-only agent: no tools -> safe.

            if (builtInTools == null)
                return true;

            foreach (var name in allowed)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var trimmed = name.Trim();

                // Only built-in tools can run inside a SubAgent; unknown/MCP names are not executed there.
                if (!builtInTools.ToolExists(trimmed))
                    continue;

                if (builtInTools.GetToolAccessLevel(trimmed) != ToolAccessLevel.ReadOnly)
                    return false;
            }

            return true;
        }
    }
}
