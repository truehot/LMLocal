using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Application.SubAgents
{
    /// <summary>
    /// Runs isolated "SubAgent" prompts. 
    /// </summary>
    internal interface ISubAgentsService
    {
        /// <summary>
        /// Runs a fully-specified agent without touching json.
        /// </summary>
        Task<SubAgentsRunResponse> ExecutePromptAsync(SubAgentRunRequest request, CancellationToken cancellationToken);
    }
}
