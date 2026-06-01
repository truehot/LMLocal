using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations.FileLinesReader;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Tool interface to read a range of lines from a file inside the current Visual Studio solution.
    /// </summary>
    internal interface IFileLinesReader : IBuiltInTool
    {
        Task<FileLinesResponse> ExecuteAsync(
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken = default);
    }

    internal class FileLinesReader : IFileLinesReader
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;

        public string ToolName => "Read_Solution_File_Lines";

        public FileLinesReader(IVsDependencies vsDependencies, IPathResolver pathResolver)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Reads a specific line range from a file within the current Visual Studio solution. Returns a FileLinesResponse with the requested lines (or fewer if end_line exceeds file length). Throws FileNotFoundException if the file does not exist or is outside the solution directory. Throws ArgumentException if start_line exceeds the file's total line count or if end_line < start_line or if either value is less than 1. Lines are returned exactly as they appear in the file, and there is no limit on the maximum number of lines per request.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Path to the source file (absolute or relative to solution root)." } },
                        { "start_line", new ToolDetails { Type = "integer", Description = "The starting line number (1-indexed). Must be a positive integer (>= 1)." } },
                        { "end_line", new ToolDetails { Type = "integer", Description = "The ending line number (inclusive). Must be a positive integer (>= 1) and must be >= start_line." } }
                    },
                    Required = new List<string> { "file_path", "start_line", "end_line" }
                }
            };
        }

        public async Task<FileLinesResponse> ExecuteAsync(
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken = default)
        {
            var (filePath, startLine, endLine) = ExtractAndValidateParameters(parameters);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            await _vsDependencies.InitializeAsync();

            string solutionDir = _vsDependencies.GetSolutionDirectory();
            if (!_pathResolver.TryResolveFilePath(filePath, solutionDir, out string absolutePath) || string.IsNullOrEmpty(absolutePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            if (!File.Exists(absolutePath))
                throw new FileNotFoundException($"File not found: {absolutePath}");

            if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                throw new ArgumentException($"File '{absolutePath}' is outside the solution directory '{solutionDir}'.");

            if (!_pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath))
                relativePath = absolutePath;

            var result = await Task.Run(() => ReadFileLines(absolutePath, startLine, endLine, cancellationToken), cancellationToken);

            result.FilePath = relativePath;
            return result;
        }

        private FileLinesResponse ReadFileLines(string absolutePath, int startLine, int endLine, CancellationToken cancellationToken)
        {
            var result = new FileLinesResponse
            {
                FilePath = "", 
                Lines = new List<FileLineInfo>()
            };

            int currentLine = 0;
            using (var reader = new StreamReader(absolutePath))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    currentLine++;

                    if (currentLine < startLine)
                        continue;

                    if (currentLine > endLine)
                        break;

                    result.Lines.Add(new FileLineInfo
                    {
                        LineNumber = currentLine,
                        Text = line
                    });
                }
            }

            if (startLine > currentLine)
                throw new ArgumentException($"Start line {startLine} exceeds file line count {currentLine}.", nameof(startLine));

            return result;
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
            var fileResult = (FileLinesResponse)result;
            return $"Read {fileResult.Lines.Count} lines.";
        }

        private (string filePath, int startLine, int endLine) ExtractAndValidateParameters(
            Dictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("file_path", out object filePathObj) || !(filePathObj is string))
                throw new ArgumentException("Parameter 'file_path' is required and must be a string.", nameof(parameters));

            if (!parameters.TryGetValue("start_line", out object startLineObj) || !TryParseInt(startLineObj, out int startLine))
                throw new ArgumentException("Parameter 'start_line' is required and must be an integer.", nameof(parameters));

            if (!parameters.TryGetValue("end_line", out object endLineObj) || !TryParseInt(endLineObj, out int endLine))
                throw new ArgumentException("Parameter 'end_line' is required and must be an integer.", nameof(parameters));

            string filePath = (string)filePathObj;

            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be empty.", nameof(filePath));
            if (startLine < 1)
                throw new ArgumentException("Start line must be 1 or greater.", nameof(startLine));
            if (endLine < startLine)
                throw new ArgumentException("End line must be greater than or equal to start line.", nameof(endLine));

            return (filePath, startLine, endLine);
        }

        private bool TryParseInt(object value, out int result)
        {
            result = 0;
            if (value is int intVal)
            {
                result = intVal;
                return true;
            }
            if (value is long longVal)
            {
                if (longVal >= int.MinValue && longVal <= int.MaxValue)
                {
                    result = (int)longVal;
                    return true;
                }
                return false;
            }
            if (value is string strVal && int.TryParse(strVal, out int parsed))
            {
                result = parsed;
                return true;
            }
            return false;
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
            [JsonProperty("file")]
            public string FilePath { get; set; }

            [JsonProperty("lines")]
            public List<FileLineInfo> Lines { get; set; }
        }
    }
}
