using System;
using System.Text;
using LMLocal.Application.Abstractions.Ports;

namespace LMLocal.Application.ChatSessionStream
{
    /// <summary>
    /// Runs several guards in order and returns the first reason to stop. Stateless when its guards are.
    /// </summary>
    internal sealed class CompositeToolCallArgumentsGuard : IToolCallArgumentsGuard
    {
        private readonly IToolCallArgumentsGuard[] _guards;

        public CompositeToolCallArgumentsGuard(params IToolCallArgumentsGuard[] guards)
        {
            _guards = guards ?? throw new ArgumentNullException(nameof(guards));
        }

        public string Check(string functionName, StringBuilder arguments, string fragment)
        {
            foreach (var guard in _guards)
            {
                string reason = guard.Check(functionName, arguments, fragment);
                if (reason != null)
                    return reason;
            }

            return null;
        }
    }
}
