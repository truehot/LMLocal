using System.Text;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Tool;

namespace LMLocal.Application.ChatSessionStream
{
    /// <summary>
    /// Stops a Gemma tool call whose arguments contain chat-template control tokens. Write tools may legitimately carry these
    /// tokens in file content, so for them only a protocol junction (one token right after another) stops the call; for every
    /// other tool a single token does. A call whose name is not known yet is treated like a write tool.
    /// Stateless: one instance serves all streams.
    /// </summary>
    internal sealed class GemmaToolCallArgumentsGuard : IToolCallArgumentsGuard
    {
        public static readonly GemmaToolCallArgumentsGuard Instance = new GemmaToolCallArgumentsGuard();

        public string Check(string functionName, StringBuilder arguments, string fragment)
        {
            if (arguments == null || string.IsNullOrEmpty(fragment))
                return null;

            int gt = fragment.IndexOf('>');
            if (gt < 0)
                return null;

            bool anyToken = functionName != null && !WriteTools.IsWriteTool(functionName);
            int offset = arguments.Length - fragment.Length;

            for (; gt >= 0; gt = fragment.IndexOf('>', gt + 1))
            {
                int end = offset + gt;
                string token = GemmaControlTokens.MatchEndingAt(arguments, end);
                if (token == null)
                    continue;

                if (anyToken)
                    return $"Gemma control token {token} in the arguments";

                string previous = GemmaControlTokens.MatchEndingAt(arguments, end - token.Length);
                if (previous != null && GemmaControlTokens.IsJunction(previous, token))
                    return $"Gemma control tokens {previous}{token} in the arguments";
            }

            return null;
        }
    }
}
