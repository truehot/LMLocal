using System.Text;

namespace LMLocal.Application.ChatSessionStream
{
    /// <summary>
    /// Gemma chat-template control tokens as they leak into tool-call arguments when LM Studio fails to parse a Gemma tool call
    /// (an unclosed string value). The arguments are JSON text, so the string delimiter shows up escaped.
    /// Every token ends with '>', so a token can only complete in a fragment that contains '>'.
    /// </summary>
    internal static class GemmaControlTokens
    {
        internal const string ToolCallOpen = "<|tool_call>";
        internal const string ToolCallClose = "<tool_call|>";
        internal const string ChannelOpen = "<|channel>";
        internal const string ChannelClose = "<channel|>";
        internal const string ToolResponseOpen = "<|tool_response>";
        internal const string StringDelimiter = "<|\"|>";
        internal const string EscapedStringDelimiter = "<|\\\"|>";

        internal static readonly string[] All =
        {
            ToolCallOpen, ToolCallClose, ChannelOpen, ChannelClose, ToolResponseOpen, StringDelimiter, EscapedStringDelimiter
        };

        /// <summary>
        /// Returns the token whose last character is at <paramref name="end"/>, or null.
        /// </summary>
        internal static string MatchEndingAt(StringBuilder text, int end)
        {
            if (end < 0 || end >= text.Length || text[end] != '>')
                return null;

            foreach (var token in All)
            {
                int start = end - token.Length + 1;
                if (start < 0)
                    continue;

                int k = token.Length - 2;
                while (k >= 0 && text[start + k] == token[k])
                    k--;

                if (k < 0)
                    return token;
            }

            return null;
        }

        /// <summary>
        /// True when <paramref name="previous"/> immediately followed by <paramref name="next"/> is a protocol junction:
        /// a tool call or a thought channel closes and the next call or channel opens.
        /// </summary>
        internal static bool IsJunction(string previous, string next)
        {
            if (previous == ToolCallClose)
                return next == ToolCallOpen || next == ChannelOpen;

            return previous == ChannelClose && next == ToolCallOpen;
        }
    }
}
