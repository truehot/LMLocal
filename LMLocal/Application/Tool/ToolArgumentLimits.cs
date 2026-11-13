using System;
using System.Collections.Generic;

namespace LMLocal.Application.Tool
{
    /// <summary>
    /// Maximum length (chars of the arguments JSON) of a tool call while it streams.
    /// </summary>
    internal static class ToolArgumentLimits
    {
        internal const int SimpleToolMaxLength = 4000;
        internal const int ShortTaskSubAgentMaxLength = 8000;

        private static readonly Dictionary<string, int> MaxLengths = Build();

        /// <summary>
        /// The maximum arguments length for the tool, or null when the tool has no limit.
        /// </summary>
        public static int? GetMaxLength(string toolName)
        {
            return !string.IsNullOrEmpty(toolName) && MaxLengths.TryGetValue(toolName, out int max) ? max : (int?)null;
        }

        private static Dictionary<string, int> Build()
        {
            var limits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in new[]
            {
                "build_solution", "delete_file", "find_files", "format_document", "get_active_document", "get_changes_diff",
                "get_solution_overview", "get_symbol_info", "get_symbol_info_js", "inspect_type", "list_directory",
                "read_file_lines", "run_tests", "search_chat_history", "search_file_content", "search_solution_knowledge",
                "set_file_project_status"
            })
            {
                limits.Add(name, SimpleToolMaxLength);
            }

            foreach (var name in new[]
            {
                "code_explorer_subagent", "code_reader_subagent", "symbol_analyzer_subagent", "build_subagent",
                "context_retriever_subagent"
            })
            {
                limits.Add(name, ShortTaskSubAgentMaxLength);
            }

            return limits;
        }
    }
}
