using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Syntax;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using Microsoft.CodeAnalysis;
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
        private readonly ISyntaxChecker _syntaxChecker;

        public string ToolName => "replace_file_lines";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.FullAccess;

        public ReplaceFileLines(
            IVsDependencies vsDependencies,
            IPathResolver pathResolver,
            ISnapshotManager snapshotManager,
            IFileSystem fileSystem,
            ISyntaxChecker syntaxChecker)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _syntaxChecker = syntaxChecker ?? throw new ArgumentNullException(nameof(syntaxChecker));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Replaces a range of lines in a file by line numbers (1-indexed, inclusive on both ends). After the replacement, line numbers shift — re-read the file if you need accurate positions for subsequent edits. If start_line or end_line exceeds the current line count, the file is automatically padded with empty lines up to start_line - 1, then new_lines are inserted. Set new_lines to an empty string to delete the range. Always returns the replaced and new blocks for verification (regardless of return_context). If syntax errors are detected after replacement, they are reported in syntax_errors field but the file is still saved. Example: {\"file_path\":\"src/Program.cs\",\"start_line\":5,\"end_line\":10,\"new_lines\":\"Console.WriteLine(\\\"Hello\\\");\\n\"} replaces lines 5-10 with a single line.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Relative path to file." } },
                        { "start_line", new ToolDetails { Type = "integer", Description = "Starting line number (1-indexed, inclusive)." } },
                        { "end_line", new ToolDetails { Type = "integer", Description = "Ending line number (1-indexed, inclusive). Must be >= start_line." } },
                        { "new_lines", new ToolDetails { Type = "string", Description = "New text to replace the lines. Can contain multiple lines separated by \\n or \\r\\n. If empty string, the line range is deleted." } }
                    },
                    Required = new List<string> { "file_path", "start_line", "end_line", "new_lines" }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                var (filePath, startLine, endLine, newLinesText, error) = ExtractAndValidateParameters(parameters);
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

                string originalContent = await _fileSystem.ReadAllTextWithSharedReadAsync(absolutePath, cancellationToken).ConfigureAwait(false);
                string separator = originalContent.Contains("\r\n") ? "\r\n" : "\n";

                string[] lines = originalContent.Split(new[] { separator }, StringSplitOptions.None);
                var linesList = new List<string>(lines);

                bool hadTrailingNewline = linesList.Count > 0 && linesList[linesList.Count - 1] == "" && originalContent.EndsWith(separator);
                if (hadTrailingNewline)
                    linesList.RemoveAt(linesList.Count - 1);

                string[] newLines = null;
                bool hasNewLines = !string.IsNullOrEmpty(newLinesText);
                if (hasNewLines)
                    newLines = newLinesText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

                List<string> replacedBlock = new List<string>();
                int startIdx = Math.Min(startLine - 1, linesList.Count);
                int endIdx = Math.Min(endLine, linesList.Count);
                for (int i = startIdx; i < endIdx; i++)
                    replacedBlock.Add(linesList[i]);

                while (linesList.Count < startLine - 1)
                    linesList.Add("");

                int removeStart = startLine - 1;
                int removeCount = Math.Min(endLine - startLine + 1, linesList.Count - removeStart);
                if (removeCount > 0)
                    linesList.RemoveRange(removeStart, removeCount);

                if (hasNewLines)
                    linesList.InsertRange(removeStart, newLines);

                if (hadTrailingNewline)
                {
                    if (linesList.Count == 0 || !string.IsNullOrEmpty(linesList[linesList.Count - 1]))
                        linesList.Add("");
                }

                string newContent = string.Join(separator, linesList);

                await _snapshotManager.SnapshotFileAsync(absolutePath, SnapshotChangeStatus.BeforeModify, cancellationToken).ConfigureAwait(false);
                await _fileSystem.WriteAllBytesAsync(absolutePath, Encoding.UTF8.GetBytes(newContent), cancellationToken).ConfigureAwait(false);

                string[] syntaxErrors = null;
                if (_syntaxChecker.IsSupported(absolutePath))
                {
                    if (!_syntaxChecker.IsSyntaxValid(newContent, out var errors))
                    {
                        syntaxErrors = errors.Select(e => $"{e.Id}: {e.GetMessage()}").ToArray();
                        InternalLogger.Info($"Syntax errors detected after replacement in {absolutePath}:\n{string.Join("\n", syntaxErrors)}");
                    }
                }

                _pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath);

                var response = new ReplaceLinesResponse
                {
                    Success = true,
                    FilePath = relativePath ?? absolutePath,
                    LinesReplaced = endLine - startLine + 1,
                    Replaced = replacedBlock.ToArray(),
                    New = hasNewLines ? newLines : Array.Empty<string>(),
                    SyntaxErrors = syntaxErrors
                };

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

        private (string filePath, int startLine, int endLine, string newLines, string error) ExtractAndValidateParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, 0, 0, null, "Parameters cannot be null.");

            if (!parameters.TryGetValue("file_path", out object filePathObj) || !(filePathObj is string filePath))
                return (null, 0, 0, null, "file_path parameter is required and must be a string.");

            if (!parameters.TryGetValue("start_line", out object startObj) || !TryParseInt(startObj, out int startLine))
                return (null, 0, 0, null, "start_line parameter is required and must be an integer.");

            if (!parameters.TryGetValue("end_line", out object endObj) || !TryParseInt(endObj, out int endLine))
                return (null, 0, 0, null, "end_line parameter is required and must be an integer.");

            if (!parameters.TryGetValue("new_lines", out object newLinesObj) || !(newLinesObj is string newLines))
                return (null, 0, 0, null, "new_lines parameter is required and must be a string.");

            if (startLine < 1)
                return (null, 0, 0, null, "start_line must be >= 1.");
            if (endLine < 1)
                return (null, 0, 0, null, "end_line must be >= 1.");
            if (endLine < startLine)
                return (null, 0, 0, null, "end_line must be >= start_line.");

            return (filePath, startLine, endLine, newLines, null);
        }

        private bool TryParseInt(object value, out int result) => int.TryParse(value?.ToString(), out result);

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
            {
                if (!response.Success)
                    return $"Replacing lines failed: {response.ErrorMessage}";
                if (response.SyntaxErrors != null && response.SyntaxErrors.Length > 0)
                    return $"{response.LinesReplaced} line(s) replaced with {response.SyntaxErrors.Length} syntax error(s).";
                return $"{response.LinesReplaced} line(s) replaced successfully.";
            }
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

        [JsonProperty("replaced")]
        public string[] Replaced { get; set; }

        [JsonProperty("new")]
        public string[] New { get; set; }

        [JsonProperty("syntax_errors", NullValueHandling = NullValueHandling.Ignore)]
        public string[] SyntaxErrors { get; set; }
    }
}
