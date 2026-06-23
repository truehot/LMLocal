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
    /// <summary>
    /// Tool to read a range of lines from a file inside the current Visual Studio solution.
    /// </summary>
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
                Description = "Reads a specific line range from a file. Lines are 1-indexed and returned exactly as they appear, with no limit on how many lines can be read in one request. Both start_line and end_line must be >= 1, and end_line must be >= start_line. Fails if the file does not exist or is outside the solution directory. Use to read part of a file without loading the entire content. Example: {\"file_path\":\"src/Program.cs\",\"start_line\":1,\"end_line\":25} → {\"success\":true,\"file_path\":\"src/Program.cs\",\"lines\":[{\"line_number\":1,\"text\":\"using System;\"},{\"line_number\":2,\"text\":\"\"},{\"line_number\":3,\"text\":\"namespace App {\"}],\"error_message\":null}.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Path to the source file (absolute or relative to solution root)." } },
                        { "start_line", new ToolDetails { Type = "integer", Description = "The starting line number (1-indexed, inclusive). Must be a positive integer (>= 1)." } },
                        { "end_line", new ToolDetails { Type = "integer", Description = "The ending line number (inclusive). Must be a positive integer (>= 1) and must be >= start_line." } }
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

                var lines = await _fileSystem.ReadLinesRangeAsync(absolutePath, startLine, endLine, cancellationToken).ConfigureAwait(false);

                var resultLines = new List<FileLineInfo>();
                for (int i = 0; i < lines.Count; i++)
                {
                    resultLines.Add(new FileLineInfo
                    {
                        LineNumber = startLine + i,
                        Text = lines[i]
                    });
                }

                return new FileLinesResponse
                {
                    Success = true,
                    FilePath = relativePath,
                    Lines = resultLines
                };
            }
            catch (Exception ex)
            {
                return new FileLinesResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    FilePath = parameters?.TryGetValue("file_path", out var fp) == true ? fp?.ToString() : "",
                    Lines = new List<FileLineInfo>()
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
                return fileResult.Success ? $"Read {fileResult.Lines.Count} lines." : $"Reading lines failed: {fileResult.ErrorMessage}";
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
                Lines = new List<FileLineInfo>()
            };
        }

        public class FileLineInfo
        {
            [JsonProperty("line_number")]
            public int LineNumber { get; set; }

            [JsonProperty("text")]
            public string Text { get; set; }
        }

        public class FileLinesResponse
        {
            [JsonProperty("file_path")]
            public string FilePath { get; set; }

            [JsonProperty("lines")]
            public List<FileLineInfo> Lines { get; set; }

            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("error_message")]
            public string ErrorMessage { get; set; }
        }
    }
}