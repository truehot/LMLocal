using System;
using LMLocal.Application.Abstractions.Ports;

namespace LMLocal.Application.ChatSessionStream
{
    /// <summary>
    /// Default <see cref="IToolCallArgumentsGuardFactory"/>: every model gets the arguments length limit; Gemma models also get
    /// the control-token check.
    /// </summary>
    internal sealed class ToolCallArgumentsGuardFactory : IToolCallArgumentsGuardFactory
    {
        private static readonly IToolCallArgumentsGuard GemmaGuard =
            new CompositeToolCallArgumentsGuard(ArgumentsLengthGuard.Instance, GemmaToolCallArgumentsGuard.Instance);

        public IToolCallArgumentsGuard Create(string modelId)
        {
            if (!string.IsNullOrEmpty(modelId) && modelId.IndexOf("gemma", StringComparison.OrdinalIgnoreCase) >= 0)
                return GemmaGuard;

            return ArgumentsLengthGuard.Instance;
        }
    }
}
