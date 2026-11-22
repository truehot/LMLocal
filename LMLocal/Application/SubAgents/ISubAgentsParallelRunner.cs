using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;

namespace LMLocal.Application.SubAgents
{
    /// <summary>
    /// Executes a single tool call that belongs to a parallel SubAgent group and returns its outcome.
    /// </summary>
    internal delegate Task<ToolExecutionResult> SubAgentsParallelWork(
        ToolCallRecord call,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs a group of SubAgent tool calls concurrently, bounded to at most <c>maxParallel</c> in flight, and returns their outcomes in input order.
    /// </summary>
    internal interface ISubAgentsParallelRunner
    {
        Task<SubAgentsParallelRunResult> RunAsync(
            IReadOnlyList<ToolCallRecord> calls,
            SubAgentsParallelWork work,
            int maxParallel,
            CancellationToken cancellationToken);
    }
}
