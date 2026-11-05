using LMLocal.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    internal interface IReadFileLines : IBuiltInTool
    {
    }

    internal class ReadFileLines : IReadFileLines
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IFileSystem _fileSystem;

        public string ToolName => "read_file_lines";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

        public ReadFileLines(IVsDependencies vsDependencies, IPathResolver pathResolver, IFileSystem fileSystem)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Reads a specific line range from a file and returns the lines as an array of objects, one per line. Each object has 'line_number' (the 1-based line number) and 'text' (the exact raw source text of that line, with no line-number prefix and no trailing newline). A blank line is returned as an object with its line_number set and text equal to an empty string. If the requested end_line exceeds the total number of lines, the tool returns all available lines from start_line to the end of the file. has_more_results=true means additional lines exist after the returned content; to continue, call the tool again for the same file starting from the next line number after the last line shown in the returned content. When has_more_results=false, total_lines is the file's line count (empty content: start_line is past the end). Only works inside the open solution directory — a path that resolves outside the solution is rejected with an explicit error. Use this tool to inspect a known file and line range without loading the entire content, or to visually confirm a fact after another tool has located it. Prefer search_file_content or symbol tools to locate unknown text, identifiers, or line numbers before using this tool. Safe to call several times in parallel.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Relative path to the file." } },
                        { "start_line", new ToolDetails { Type = "integer", Description = "Starting line number (1-indexed, inclusive, >= 1)." } },
                        { "end_line", new ToolDetails { Type = "integer", Description = "Ending line number (inclusive, >= start_line)." } }
                    },
                    Required = new List<string> { "file_path", "start_line", "end_line" }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                var (filePath, startLine, endLine, error) = ExtractAndValidateParameters(parameters);

                if (!string.IsNullOrEmpty(error))
                    return Error(error, parameters?.TryGetValue("file_path", out var fp) == true ? fp?.ToString() : "");

                if (!_vsDependencies.IsSolutionOpen)
                    return Error("No solution is currently open.", filePath);

                string solutionDir = _vsDependencies.GetSolutionDirectory();
                if (!_pathResolver.TryResolveFilePath(filePath, solutionDir, out string absolutePath) || string.IsNullOrEmpty(absolutePath))
                    return Error($"File not found: {filePath}", filePath);

                if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                    return Error($"File '{absolutePath}' is outside the solution directory '{solutionDir}'. Only files inside the open solution can be read.", filePath);

                if (!_fileSystem.FileExists(absolutePath))
                    return Error($"File not found: {absolutePath}", filePath);

                if (!_pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath))
                    relativePath = absolutePath;

                int readEnd = endLine == int.MaxValue ? int.MaxValue : endLine + 1;

                var (lines, totalLines) = await _fileSystem
                    .ReadLinesRangeWithTotalAsync(absolutePath, startLine, readEnd, cancellationToken)
                    .ConfigureAwait(false);

                bool hasMore = lines.Count > (endLine - startLine + 1);
                if (hasMore && lines.Count > 0)
                    lines.RemoveAt(lines.Count - 1);
                if (hasMore)
                    totalLines = null;

                return new FileLinesResponse
                {
                    Success = true,
                    FilePath = relativePath,
                    AbsolutePath = absolutePath,
                    Content = NumberLines(lines, startLine),
                    HasMoreResults = hasMore,
                    TotalLines = totalLines
                };
            }
            catch (Exception ex)
            {
                return new FileLinesResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    FilePath = parameters?.TryGetValue("file_path", out var fp) == true ? fp?.ToString() : "",
                    Content = null,
                    HasMoreResults = false
                };
            }
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            if (parameters == null) return "Reading lines 1-1 of ''... ";

            var file = parameters.TryGetValue("file_path", out var f) ? f?.ToString() : "";
            var start = parameters.TryGetValue("start_line", out var s) && int.TryParse(s?.ToString(), out var si) ? si : 1;
            var end = parameters.TryGetValue("end_line", out var en) && int.TryParse(en?.ToString(), out var ei) ? ei : 1;
            return $"Reading lines {start}-{end} of '{file}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is FileLinesResponse fileResult)
            {
                if (!fileResult.Success)
                    return $"Read lines failed: {fileResult.ErrorMessage}";

                var total = fileResult.Content?.Count ?? 0;
                return total > 0
                    ? $"Read {total} {Pluralizer.Pluralize(total, "line", "lines")}."
                    : "No lines found.";
            }

            return "Reading lines finished.";
        }

        /// <summary>
        /// Maps each raw line to a <see cref="FileLineEntry"/> carrying its 1-based line number and the exact raw line text (no prefix, no trailing newline).
        /// </summary>
        private static List<FileLineEntry> NumberLines(List<string> lines, int startLine)
        {
            var result = new List<FileLineEntry>(lines?.Count ?? 0);
            if (lines == null)
                return result;

            for (int i = 0; i < lines.Count; i++)
            {
                result.Add(new FileLineEntry
                {
                    LineNumber = startLine + i,
                    Text = lines[i] ?? string.Empty
                });
            }
            return result;
        }

        private (string filePath, int startLine, int endLine, string error) ExtractAndValidateParameters(
            Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, 0, 0, "Parameters cannot be null.");

            if (!parameters.TryGetValue("file_path", out object filePathObj) || !(filePathObj is string))
                return (null, 0, 0, "file_path parameter is required and must be a string.");

            if (!parameters.TryGetValue("start_line", out object startLineObj) || !TryParseInt(startLineObj, out int startLine))
                return (null, 0, 0, "start_line parameter is required and must be an integer.");

            if (!parameters.TryGetValue("end_line", out object endLineObj) || !TryParseInt(endLineObj, out int endLine))
                return (null, 0, 0, "end_line parameter is required and must be an integer.");

            if (startLine < 1)
                return (null, 0, 0, "start_line must be a positive integer (>= 1).");

            if (endLine < 1)
                return (null, 0, 0, "end_line must be a positive integer (>= 1).");

            if (endLine < startLine)
                return (null, 0, 0, "end_line must be greater than or equal to start_line.");

            return ((string)filePathObj, startLine, endLine, null);
        }

        private bool TryParseInt(object value, out int result) => int.TryParse(value?.ToString(), out result);

        private static FileLinesResponse Error(string message, string filePath = "")
        {
            return new FileLinesResponse
            {
                Success = false,
                ErrorMessage = message,
                FilePath = filePath,
                Content = null,
                HasMoreResults = false
            };
        }

        public class FileLinesResponse
        {
            [JsonProperty("file_path")]
            public string FilePath { get; set; }

            [JsonProperty("content")]
            public List<FileLineEntry> Content { get; set; }

            [JsonProperty("has_more_results")]
            public bool HasMoreResults { get; set; }

            /// <summary>
            /// Number of lines in the file; reported only when the read reached the end of the file (has_more_results=false).
            /// </summary>
            [JsonProperty("total_lines", NullValueHandling = NullValueHandling.Ignore)]
            public int? TotalLines { get; set; }

            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
            public string ErrorMessage { get; set; }

            /// <summary>
            /// Absolute on-disk path of the file that was read.
            /// </summary>
            [JsonIgnore]
            public string AbsolutePath { get; set; }
        }

        public class FileLineEntry
        {
            [JsonProperty("line_number")]
            public int LineNumber { get; set; }

            [JsonProperty("text")]
            public string Text { get; set; }
        }
    }
}
