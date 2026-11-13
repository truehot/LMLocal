using System;
using System.Collections.Generic;

namespace LMLocal.Application.Tool
{
    /// <summary>
    /// Tools that write content into a file and therefore carry arbitrary file text in their arguments.
    /// </summary>
    internal static class WriteTools
    {
        /// <summary>
        /// Arguments that hold file content, per write tool. Everything else (file path, line numbers) is metadata.
        /// </summary>
        private static readonly Dictionary<string, string[]> ContentArguments =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "create_file", new[] { "content" } },
                { "replace_file_content", new[] { "content" } },
                { "replace_file_lines", new[] { "old_lines", "new_lines" } },
                { "insert_file_lines", new[] { "new_lines" } }
            };

        /// <summary>
        /// True when the tool writes content into a file and therefore carries its payload in the arguments.
        /// </summary>
        public static bool IsWriteTool(string toolName)
        {
            return !string.IsNullOrEmpty(toolName) && ContentArguments.ContainsKey(toolName);
        }

        /// <summary>
        /// The content arguments of a write tool; empty for any other tool.
        /// </summary>
        public static IReadOnlyList<string> GetContentArguments(string toolName)
        {
            return !string.IsNullOrEmpty(toolName) && ContentArguments.TryGetValue(toolName, out var names) ? names : Array.Empty<string>();
        }
    }
}
