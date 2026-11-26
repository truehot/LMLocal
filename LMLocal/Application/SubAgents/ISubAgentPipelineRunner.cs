using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Application.SubAgents
{
    /// <summary>
    /// Runs the steps of a pipeline in a fixed order on top of <see cref="ISubAgentsService"/>, so no decision of the
    /// main model is needed between the steps.
    /// </summary>
    internal interface ISubAgentPipelineRunner
    {
        /// <summary>
        /// Executes every step and returns one aggregate response.
        /// </summary>
        Task<SubAgentsRunResponse> RunAsync(SubAgentPipelineRunRequest request, CancellationToken cancellationToken);
    }
}
