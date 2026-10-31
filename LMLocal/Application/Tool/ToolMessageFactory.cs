using LMLocal.Core.Common;
using LMLocal.Core.Models;

namespace LMLocal.Application.Tool
{
    /// <summary>
    /// Maps tool execution results to <see cref="ChatMessage"/> entries of role "tool".
    /// </summary>
    internal static class ToolMessageFactory
    {
        /// <summary>
        /// Builds a "tool" ChatMessage from a <see cref="ToolResultMessage"/>.
        /// </summary>
        public static ChatMessage CreateFromToolResult(ToolResultMessage toolResult)
        {
            return new ChatMessage("tool", ToContent(toolResult.Result), toolResult.ToolCallId.ToString());
        }

        /// <summary>
        /// The single source of truth for the text a tool result contributes to the prompt: a string as is, anything else as JSON, null as an empty string.
        /// </summary>
        public static string ToContent(object result)
        {
            if (result == null)
                return string.Empty;

            return result is string text ? text : result.ToJson();
        }
    }
}
