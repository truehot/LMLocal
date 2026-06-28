using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    internal interface IInsertFileLines : IBuiltInTool
    {
    }

    internal class InsertFileLines : IInsertFileLines
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly ISnapshotManager _snapshotManager;
        private readonly IFileSystem _fileSystem;

        public string ToolName => "insert_file_lines";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.FullAccess;

        public InsertFileLines(
            IVsDependencies vsDependencies,
            IPathResolver pathResolver,
            ISnapshotManager snapshotManager,
            IFileSystem fileSystem)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Inserts lines at a specific position in a file. Lines are 1-indexed: position=0 inserts before the first line, position=5 inserts after line 5. Automatically pads the file with empty lines if position exceeds the current line count. The new_lines string can contain multiple lines separated by \\n or \\r\\n. Must not be empty. Fails if the file does not exist or is outside the solution directory. Example: {\"file_path\":\"src/Program.cs\",\"position\":10,\"new_lines\":\"Console.WriteLine(\\\"Hello\\\");\\nConsole.ReadLine();\"} → {\"success\":true,\"file_path\":\"src/Program.cs\",\"lines_inserted\":2,\"error_message\":null}.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Absolute or relative path to file." } },
                        { "position", new ToolDetails { Type = "integer", Description = "Line number after which to insert (1-indexed). Use 0 to insert before the first line. Must be >= 0." } },
                        { "new_lines", new ToolDetails { Type = "string", Description = "Text to insert. Can contain multiple lines separated by \\n or \\r\\n. Must not be empty." } },
                    },
                    Required = new List<string> { "file_path", "position", "new_lines" }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                var (filePath, position, newLinesText, error) = ExtractAndValidateParameters(parameters);
                if (error != null)
                    return Error(error);

                if (!_vsDependencies.IsSolutionOpen)
                    return Error("No solution is currently open.");

                string solutionDir = _vsDependencies.GetSolutionDirectory();
                if (!_pathResolver.TryResolveFilePath(filePath, solutionDir, out string absolutePath))
                    return Error($"Failed to resolve file path: {filePath}");

                if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                    return Error($"File '{absolutePath}' is outside the solution directory.");

                try { _fileSystem.ValidateFilePath(absolutePath); }
                catch (ArgumentException ex) { return Error($"Invalid file path: {ex.Message}"); }

                if (!_fileSystem.FileExists(absolutePath))
                    return Error($"File not found: {filePath}");

                string originalContent = await _fileSystem.ReadAllTextWithSharedReadAsync(absolutePath, cancellationToken);

                string separator = originalContent.Contains("\r\n") ? "\r\n" : "\n";

                string[] lines = originalContent.Split(new[] { separator }, StringSplitOptions.None);
                var linesList = new List<string>(lines);

                bool hadTrailingNewline = linesList.Count > 0 && linesList[linesList.Count - 1] == "" && originalContent.EndsWith(separator);
                if (hadTrailingNewline)
                {
                    linesList.RemoveAt(linesList.Count - 1);
                }

                string normalizedInsert = newLinesText.Replace("\r\n", "\n").Replace("\r", "\n");
                string[] newLines = normalizedInsert.Split('\n');

                while (linesList.Count < position)
                    linesList.Add("");

                bool isAppendingToEnd = position >= linesList.Count;

                if (position == 0)
                    linesList.InsertRange(0, newLines);
                else
                    linesList.InsertRange(position, newLines);

                if (hadTrailingNewline || isAppendingToEnd)
                {
                    linesList.Add("");
                }

                string newContent = string.Join(separator, linesList);

                await _snapshotManager.SnapshotFileAsync(absolutePath, SnapshotChangeStatus.BeforeModify, cancellationToken);
                await _fileSystem.WriteAllBytesAsync(absolutePath, Encoding.UTF8.GetBytes(newContent), cancellationToken);

                _pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath);
                return new InsertLinesResponse
                {
                    Success = true,
                    FilePath = relativePath ?? absolutePath,
                    LinesInserted = newLines.Length
                };
            }
            catch (OperationCanceledException)
            {
                return Error("Operation was cancelled.");
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error in {ToolName}: {ex}");
                return Error($"Error: {ex.Message}");
            }
        }

        private (string filePath, int position, string newLines, string error) ExtractAndValidateParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, 0, null, "Parameters cannot be null.");

            if (!parameters.TryGetValue("file_path", out object filePathObj) || !(filePathObj is string filePath))
                return (null, 0, null, "file_path parameter is required and must be a string.");

            if (!parameters.TryGetValue("position", out object posObj) || !TryParseInt(posObj, out int position))
                return (null, 0, null, "position parameter is required and must be an integer.");

            if (!parameters.TryGetValue("new_lines", out object newLinesObj) || !(newLinesObj is string newLines))
                return (null, 0, null, "new_lines parameter is required and must be a string.");

            if (position < 0)
                return (null, 0, null, "position must be >= 0.");

            if (string.IsNullOrEmpty(newLines))
                return (null, 0, null, "new_lines must not be empty.");

            return (filePath, position, newLines, null);
        }

        private bool TryParseInt(object value, out int result) => int.TryParse(value?.ToString(), out result);

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var filePath = parameters?.TryGetValue("file_path", out var f) == true ? f?.ToString() : "";
            var pos = parameters?.TryGetValue("position", out var p) == true ? p?.ToString() : "?";
            return $"Inserting lines after position {pos} in '{filePath}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is InsertLinesResponse response)
                return response.Success ? $"{response.LinesInserted} line(s) inserted." : $"Inserting lines failed: {response.ErrorMessage}";
            return "Inserting lines finished.";
        }

        private static InsertLinesResponse Error(string message)
        {
            return new InsertLinesResponse { Success = false, ErrorMessage = message };
        }
    }

    internal class InsertLinesResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        [JsonProperty("error_message")]
        public string ErrorMessage { get; set; }

        [JsonProperty("lines_inserted")]
        public int LinesInserted { get; set; }
    }
}
