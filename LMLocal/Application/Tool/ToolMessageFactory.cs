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
            string toolContent;
            if (toolResult.Result == null)
            {
                toolContent = "";
            }
            else if (toolResult.Result is string str)
            {
                toolContent = str;
            }
            else
            {
                toolContent = toolResult.Result.ToJson();
            }

            return new ChatMessage("tool", toolContent, toolResult.ToolCallId.ToString());
        }
    }
}
