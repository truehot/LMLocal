using System.Collections.Generic;
using LMLocal.Core.Models;

namespace LMLocal.Infrastructure.SubAgents
{
    /// <summary>
    /// Read-only view over the SubAgents configuration used by the tool queue policy.
    /// </summary>
    public interface ISubAgentsCatalog
    {
        /// <summary>
        /// Current in-memory snapshot, or an empty config when none has been built yet.
        /// </summary>
        SubAgentsConfig TryGetSnapshot();

        /// <summary>
        /// Enabled agents with a non-empty name, in file order. Names are not mutated.
        /// </summary>
        IReadOnlyList<SubAgentDefinition> GetEnabledAgents();
    }
}
