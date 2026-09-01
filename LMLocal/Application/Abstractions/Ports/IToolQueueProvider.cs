using LMLocal.Application.SubAgents;
using LMLocal.Infrastructure.Tooling;

namespace LMLocal.Application.Abstractions.Ports
{
    /// <summary>
    /// Builds the tool queues used by the LLM: the main chat queue and per-SubAgent queues.
    /// </summary>
    public interface IToolQueueProvider
    {
        /// <summary>
        /// Main chat queue: enabled built-in tools minus the ones owned by visible SubAgents, plus the visible agent tools, plus MCP tools.
        /// </summary>
        ToolQueue GetMainQueue();

        /// <summary>
        /// Queue for a single SubAgent run: allowed built-in tools only (no agents, no MCP).
        /// </summary>
        ToolQueue GetSubAgentQueue(SubAgentRunRequest request);
    }
}
