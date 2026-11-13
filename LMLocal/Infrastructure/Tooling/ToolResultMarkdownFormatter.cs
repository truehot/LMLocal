using System.Collections.Generic;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.SubAgents.ReadRefs;
using LMLocal.Core.Common;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Tooling
{

    internal class ToolResultMarkdownFormatter : IToolResultMarkdownFormatter
    {
        private const string CodeReaderSubagentFunctionName = "code_reader_subagent";

        public string FormatToolResults(IEnumerable<(string FunctionName, string JsonContent)> toolResults)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## Tool Results");
            sb.AppendLine();

            foreach (var (functionName, json) in toolResults)
            {
                if (string.IsNullOrWhiteSpace(json))
                    continue;

                if (functionName == CodeReaderSubagentFunctionName)
                {
                    sb.AppendLine($"**{CodeReaderSubagentFunctionName}**");
                    sb.AppendLine();
                    sb.AppendLine(json);
                    sb.AppendLine();
                    continue;
                }

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
            var lines = ExtractNumberedLines(obj["content"]);
            var hasMore = obj["has_more_results"]?.Value<bool>() == true;

            FileContentRenderer.AppendFileBlock(sb, filePath, lines, hasMore);
        }

        /// <summary>
        /// Normalizes the raw content token into <see cref="NumberedLine"/> items so it can be rendered by the shared <see cref="FileContentRenderer"/>.
        /// </summary>
        private static List<NumberedLine> ExtractNumberedLines(JToken contentToken)
        {
            var result = new List<NumberedLine>();

            if (!(contentToken is JArray lines))
            {
                result.Add(new NumberedLine(0, contentToken?.Value<string>() ?? ""));
                return result;
            }

            foreach (var line in lines)
            {
                if (line is JObject entry)
                {
                    var number = entry["line_number"]?.Value<int>() ?? 0;
                    var text = entry["text"]?.Value<string>() ?? "";
                    result.Add(new NumberedLine(number, text));
                }
                else
                {
                    result.Add(new NumberedLine(0, line?.ToString() ?? ""));
                }
            }

            return result;
        }

        private static void FormatActiveDocumentResult(System.Text.StringBuilder sb, JObject obj)
        {
            var filePath = obj["file_path"]?.Value<string>() ?? "unknown";
            var content = obj["content"]?.Value<string>() ?? "";
            var lang = MarkdownLanguageHelper.GetLanguageFromExtension(filePath);

            sb.AppendLine($"**Active Document: `{filePath}`**");
            sb.AppendLine();
            sb.AppendLine($"````{lang}");
            sb.AppendLine(content);
            sb.AppendLine("````");
            sb.AppendLine();
        }

        private static void FormatSolutionOverviewResult(System.Text.StringBuilder sb, JObject obj)
        {
            var solutionName = obj["solution_name"]?.Value<string>() ?? "Unknown";
            var totalProjects = obj["total_projects"]?.Value<int>() ?? 0;
            var totalFiles = obj["total_files"]?.Value<int>() ?? 0;
            var hasMore = obj["has_more_results"]?.Value<bool>() == true;

            sb.AppendLine($"**Solution:** {solutionName} ({totalProjects} {Pluralizer.Pluralize(totalProjects, "project", "projects")}, {totalFiles} {Pluralizer.Pluralize(totalFiles, "file", "files")})");
            sb.AppendLine();

            if (obj["projects"] is JArray projects)
            {
                foreach (var project in projects)
                {
                    var name = project["name"]?.Value<string>() ?? "";
                    var lang = project["language"]?.Value<string>() ?? "";
                    var fileCount = project["file_count"]?.Value<int>() ?? 0;
                    var isTest = project["is_test_project"]?.Value<bool>() == true;

                    var testTag = isTest ? " *(test)*" : "";
                    sb.AppendLine($"- **{name}** — {lang}, {fileCount} {Pluralizer.Pluralize(fileCount, "file", "files")}{testTag}");
                }
            }

            if (hasMore)
                sb.AppendLine("- *...and more (list truncated)*");

            sb.AppendLine();
        }
    }
}
