using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
                Description = "Reads a specific line range from a file and returns the raw text content. Lines are 1-indexed. If the requested end_line exceeds the total number of lines, the tool returns all available lines from start_line to the end of the file. The response includes the actual start_line and end_line read, plus a 'has_more' flag indicating whether there are more lines beyond the returned range. Use this tool to inspect a fragment of a file without loading the entire content. When you later use replace_file_lines, copy the text from this tool's output directly into the old_lines parameter — do not retype it, as whitespace is significant.",
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

                if (!_fileSystem.FileExists(absolutePath))
                    return Error($"File not found: {absolutePath}", filePath);

                if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                    return Error($"File '{absolutePath}' is outside the solution directory '{solutionDir}'.", filePath);

                if (!_pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath))
                    relativePath = absolutePath;

                var lines = await _fileSystem.ReadLinesRangeAsync(absolutePath, startLine, endLine + 1, cancellationToken).ConfigureAwait(false);
                bool hasMore = lines.Count > (endLine - startLine + 1);
                if (hasMore && lines.Count > 0)
                    lines.RemoveAt(lines.Count - 1);

                string text = string.Join(Environment.NewLine, lines);
                int actualEndLine = startLine + lines.Count - 1;

                return new FileLinesResponse
                {
                    Success = true,
                    FilePath = relativePath,
                    StartLine = startLine,
                    EndLine = actualEndLine,
                    Text = text,
                    HasMore = hasMore
                };
            }
            catch (Exception ex)
            {
                return new FileLinesResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    FilePath = parameters?.TryGetValue("file_path", out var fp) == true ? fp?.ToString() : "",
                    Text = null,
                    StartLine = 0,
                    EndLine = 0,
                    HasMore = false
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
                var total = fileResult.EndLine - fileResult.StartLine;
                return fileResult.Success
                    ? $"Read {total} lines."
                    : $"Read lines failed: {fileResult.ErrorMessage}";
            }

            return "Reading lines finished.";
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
                Text = null,
                StartLine = 0,
                EndLine = 0,
                HasMore = false
            };
        }

        public class FileLinesResponse
        {
            [JsonProperty("file_path")]
            public string FilePath { get; set; }

            [JsonProperty("start_line")]
            public int StartLine { get; set; }

            [JsonProperty("end_line")]
            public int EndLine { get; set; }

            [JsonProperty("text")]
            public string Text { get; set; }

            [JsonProperty("has_more")]
            public bool HasMore { get; set; }

            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("error_message")]
            public string ErrorMessage { get; set; }
        }
    }
}
