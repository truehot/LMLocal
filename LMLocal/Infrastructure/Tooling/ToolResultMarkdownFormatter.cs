using System.Collections.Generic;
using LMLocal.Core.Common;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Tooling
{
    /// <summary>
    /// Converts raw tool result JSON (from tool messages in chat history) into human-readable Markdown.
    /// </summary>
    public interface IToolResultMarkdownFormatter
    {
        /// <summary>
        /// Formats a collection of tool results (function name + JSON content) into a single Markdown section. Only results from known tools (read_file_lines, get_solution_overview, get_active_document) are included;
        /// </summary>
        string FormatToolResults(IEnumerable<(string FunctionName, string JsonContent)> toolResults);
    }

    internal class ToolResultMarkdownFormatter : IToolResultMarkdownFormatter
    {
        public string FormatToolResults(IEnumerable<(string FunctionName, string JsonContent)> toolResults)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## Tool Results");
            sb.AppendLine();

            foreach (var (functionName, json) in toolResults)
            {
                if (string.IsNullOrEmpty(json))
                    continue;

                try
                {
                    var obj = JObject.Parse(json);

                    var success = obj["success"]?.Value<bool>();
                    if (success == false)
                        continue;

                    switch (functionName)
                    {
                        case "read_file_lines":
                            FormatFileReadResult(sb, obj);
                            break;
                        case "get_solution_overview":
                            FormatSolutionOverviewResult(sb, obj);
                            break;
                        case "get_active_document":
                            FormatActiveDocumentResult(sb, obj);
                            break;
                            // Unknown tools are silently skipped
                    }
                }
                catch (Newtonsoft.Json.JsonException)
                {
                    // Malformed JSON — skip
                }
            }

            return sb.ToString().TrimEnd();
        }

        private static void FormatFileReadResult(System.Text.StringBuilder sb, JObject obj)
        {
            var filePath = obj["file_path"]?.Value<string>() ?? "unknown";
            var text = obj["text"]?.Value<string>() ?? "";
            var startLine = obj["start_line"]?.Value<int>() ?? 0;
            var endLine = obj["end_line"]?.Value<int>() ?? 0;
            var hasMore = obj["has_more"]?.Value<bool>() == true;

            var lang = MarkdownLanguageHelper.GetLanguageFromExtension(filePath);
            var lineInfo = startLine > 0 && endLine > 0
                ? $" (lines {startLine}-{endLine})"
                : "";

            sb.Append($"**`{filePath}`**{lineInfo}");
            if (hasMore)
                sb.Append(" *(truncated, more lines available)*");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine($"```{lang}");
            sb.AppendLine(text);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        private static void FormatActiveDocumentResult(System.Text.StringBuilder sb, JObject obj)
        {
            var filePath = obj["file_path"]?.Value<string>() ?? "unknown";
            var content = obj["content"]?.Value<string>() ?? "";
            var lang = MarkdownLanguageHelper.GetLanguageFromExtension(filePath);

            sb.AppendLine($"**Active Document: `{filePath}`**");
            sb.AppendLine();
            sb.AppendLine($"```{lang}");
            sb.AppendLine(content);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        private static void FormatSolutionOverviewResult(System.Text.StringBuilder sb, JObject obj)
        {
            var solutionName = obj["solution_name"]?.Value<string>() ?? "Unknown";
            var totalProjects = obj["total_projects"]?.Value<int>() ?? 0;
            var totalFiles = obj["total_files"]?.Value<int>() ?? 0;
            var hasMore = obj["has_more_results"]?.Value<bool>() == true;

            sb.AppendLine($"**Solution:** {solutionName} ({totalProjects} projects, {totalFiles} files)");
            sb.AppendLine();

            var projects = obj["projects"] as JArray;
            if (projects != null)
            {
                foreach (var project in projects)
                {
                    var name = project["name"]?.Value<string>() ?? "";
                    var lang = project["language"]?.Value<string>() ?? "";
                    var fileCount = project["file_count"]?.Value<int>() ?? 0;
                    var isTest = project["is_test_project"]?.Value<bool>() == true;

                    var testTag = isTest ? " *(test)*" : "";
                    sb.AppendLine($"- **{name}** — {lang}, {fileCount} files{testTag}");
                }
            }

            if (hasMore)
                sb.AppendLine("- *...and more (list truncated)*");

            sb.AppendLine();
        }
    }
}
