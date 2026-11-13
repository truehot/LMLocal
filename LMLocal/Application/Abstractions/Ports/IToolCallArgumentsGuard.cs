using System.Text;

namespace LMLocal.Application.Abstractions.Ports
{
    /// <summary>
    /// Watches tool-call arguments while they stream and tells the stream reader to stop when a call is clearly broken,
    /// so a runaway generation does not run up to the token limit.
    /// </summary>
    internal interface IToolCallArgumentsGuard
    {
        /// <summary>
        /// Called after <paramref name="fragment"/> was appended to <paramref name="arguments"/> (it is their tail).
        /// </summary>
        string Check(string functionName, StringBuilder arguments, string fragment);
    }

    /// <summary>
    /// Picks the arguments guard for the model of a streamed round.
    /// </summary>
    internal interface IToolCallArgumentsGuardFactory
    {
        /// <summary>
        /// Returns the guard for the model, or null when there are no rules for it.
        /// </summary>
        IToolCallArgumentsGuard Create(string modelId);
    }
}
