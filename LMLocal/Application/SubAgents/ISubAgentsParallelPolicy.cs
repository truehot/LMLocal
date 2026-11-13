namespace LMLocal.Application.SubAgents
{
    /// <summary>
    /// Decides which SubAgent tool calls may run inside a parallel group and how wide that group may be.
    /// </summary>
    internal interface ISubAgentsParallelPolicy
    {
        /// <summary>
        /// True when <paramref name="toolName"/> is a currently-enabled SubAgent that is opted into
        /// parallel execution: its <c>parallel</c> flag is set and it only uses read-only tools.
        /// </summary>
        bool IsParallelEnabled(string toolName);

        /// <summary>
        /// Effective max concurrent runs allowed for the agent behind <paramref name="toolName"/>.
        /// </summary>
        int ResolveMaxParallel(string toolName);
    }
}
