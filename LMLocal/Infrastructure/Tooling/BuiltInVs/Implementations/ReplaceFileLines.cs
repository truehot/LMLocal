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
    internal interface IReplaceFileLines : IBuiltInTool
    {
    }

    internal class ReplaceFileLines : IReplaceFileLines
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly ISnapshotManager _snapshotManager;
        private readonly IFileSystem _fileSystem;

        public string ToolName => "replace_file_lines";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.FullAccess;

        public ReplaceFileLines(
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
                Description = "Replaces a range of lines in a file by line numbers (1-indexed, inclusive on both ends). After the replacement, line numbers shift — re-read the file if you need accurate positions for subsequent edits. If start_line or end_line exceeds the current line count, the file is automatically padded with empty lines. Set new_lines to an empty string to delete the range. When return_context is true, the response includes the old (replaced) and new blocks so you can verify correctness and calculate line shifts without re-reading. Always check the success field first; if false, read error_message to understand the failure. Example: {\"file_path\":\"src/Program.cs\",\"start_line\":5,\"end_line\":10,\"new_lines\":\"Console.WriteLine(\\\"Hello\\\");\\n\",\"return_context\":true} replaces lines 5-10 with a single line and returns both the replaced block and the new block.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Absolute or relative path to file." } },
                        { "start_line", new ToolDetails { Type = "integer", Description = "Starting line number (1-indexed, inclusive)." } },
                        { "end_line", new ToolDetails { Type = "integer", Description = "Ending line number (1-indexed, inclusive). Must be >= start_line." } },
                        { "new_lines", new ToolDetails { Type = "string", Description = "New text to replace the lines. Can contain multiple lines separated by \\n or \\r\\n. If empty string, the line range is deleted." } },
                        { "return_context", new ToolDetails { Type = "boolean", Description = "If true, returns the replaced and new blocks for verification. Use this to calculate the line shift and update your internal numbering without re-reading the whole file." } }
                    },
                    Required = new List<string> { "file_path", "start_line", "end_line", "new_lines" }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                var (filePath, startLine, endLine, newLinesText, returnContext, error) = ExtractAndValidateParameters(parameters);
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

                string[] newLines = null;
                bool hasNewLines = !string.IsNullOrEmpty(newLinesText);
                if (hasNewLines)
                    newLines = newLinesText.Replace("\r", "").Split('\n');

                var resultLines = new List<string>();
                int lineNumber = 0;
                bool blockReplaced = false;
                var rangeLines = new List<string>();

                await _fileSystem.ReadLinesAsync(absolutePath, (_, line) =>
                {
                    lineNumber++;
                    if (lineNumber >= startLine && lineNumber <= endLine)
                    {
                        if (!blockReplaced)
                        {
                            if (hasNewLines)
                                resultLines.AddRange(newLines);
                            blockReplaced = true;
                        }
                        if (returnContext)
                            rangeLines.Add(line);
                    }
                    else
                    {
                        resultLines.Add(line);
                    }
                }, cancellationToken).ConfigureAwait(false);

                while (resultLines.Count < startLine - 1)
                    resultLines.Add("");

                if (!blockReplaced)
                {
                    if (hasNewLines)
                        resultLines.AddRange(newLines);
                    blockReplaced = true;
                }

                while (resultLines.Count < endLine)
                    resultLines.Add("");

                string originalContent = await _fileSystem.ReadAllTextWithSharedReadAsync(absolutePath, cancellationToken).ConfigureAwait(false);
                string separator = originalContent.Contains("\r\n") ? "\r\n" : "\n";
                string newContent = string.Join(separator, resultLines);

                await _snapshotManager.SnapshotFileAsync(absolutePath, SnapshotChangeStatus.BeforeModify, cancellationToken).ConfigureAwait(false);
                await _fileSystem.WriteAllBytesAsync(absolutePath, Encoding.UTF8.GetBytes(newContent), cancellationToken).ConfigureAwait(false);

                _pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath);

                var response = new ReplaceLinesResponse
                {
                    Success = true,
                    FilePath = relativePath ?? absolutePath,
                    LinesReplaced = endLine - startLine + 1
                };

                if (returnContext)
                {
                    response.Replaced = rangeLines.ToArray();
                    response.New = hasNewLines ? newLines : Array.Empty<string>();
                }

                return response;
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

        private (string filePath, int startLine, int endLine, string newLines, bool returnContext, string error) ExtractAndValidateParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, 0, 0, null, false, "Parameters cannot be null.");

            if (!parameters.TryGetValue("file_path", out object filePathObj) || !(filePathObj is string filePath))
                return (null, 0, 0, null, false, "file_path parameter is required and must be a string.");

            if (!parameters.TryGetValue("start_line", out object startObj) || !TryParseInt(startObj, out int startLine))
                return (null, 0, 0, null, false, "start_line parameter is required and must be an integer.");

            if (!parameters.TryGetValue("end_line", out object endObj) || !TryParseInt(endObj, out int endLine))
                return (null, 0, 0, null, false, "end_line parameter is required and must be an integer.");

            if (!parameters.TryGetValue("new_lines", out object newLinesObj) || !(newLinesObj is string newLines))
                return (null, 0, 0, null, false, "new_lines parameter is required and must be a string.");

            bool returnContext = false;
            if (parameters.TryGetValue("return_context", out object rc) && rc != null)
                returnContext = Convert.ToBoolean(rc);

            if (startLine < 1)
                return (null, 0, 0, null, false, "start_line must be >= 1.");
            if (endLine < 1)
                return (null, 0, 0, null, false, "end_line must be >= 1.");
            if (endLine < startLine)
                return (null, 0, 0, null, false, "end_line must be >= start_line.");

            return (filePath, startLine, endLine, newLines, returnContext, null);
        }

        private static bool TryParseInt(object value, out int result)
        {
            result = 0;
            if (value is int i) { result = i; return true; }
            if (value is long l && l >= int.MinValue && l <= int.MaxValue) { result = (int)l; return true; }
            if (value is string s && int.TryParse(s, out result)) return true;
            return false;
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var filePath = parameters?.TryGetValue("file_path", out var f) == true ? f?.ToString() : "";
            var start = parameters?.TryGetValue("start_line", out var s) == true ? s?.ToString() : "?";
            var end = parameters?.TryGetValue("end_line", out var e) == true ? e?.ToString() : "?";
            return $"Replacing lines {start}-{end} in '{filePath}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is ReplaceLinesResponse response)
                return response.Success ? $"{response.LinesReplaced} line(s) replaced." : $"Replacing lines failed: {response.ErrorMessage}";
            return "Replacing lines finished.";
        }

        private static ReplaceLinesResponse Error(string message)
        {
            return new ReplaceLinesResponse { Success = false, ErrorMessage = message };
        }
    }

    internal class ReplaceLinesResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        [JsonProperty("error_message")]
        public string ErrorMessage { get; set; }

        [JsonProperty("lines_replaced")]
        public int LinesReplaced { get; set; }

        [JsonProperty("replaced", NullValueHandling = NullValueHandling.Ignore)]
        public string[] Replaced { get; set; }

        [JsonProperty("new", NullValueHandling = NullValueHandling.Ignore)]
        public string[] New { get; set; }
    }
}
