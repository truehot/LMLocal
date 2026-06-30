using System;
using System.Collections.Generic;
using System.Linq;
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
                Description = "Replaces a range of lines in a file by line numbers (1-indexed). The old_lines parameter must exactly match the existing content at the target location — the tool verifies this before making any changes. The range ends at start_line + number_of_lines_in_old_lines - 1. After the replacement, line numbers shift — re-read the file if you need accurate positions for subsequent edits. If start_line exceeds the current line count, the file is automatically padded with empty lines up to start_line - 1, then new_lines are inserted. Set new_lines to an empty string to delete the range. If syntax errors are detected after replacement, they are reported in syntax_errors field but the file is still saved. Example: {\"file_path\":\"src/Program.cs\",\"start_line\":5,\"old_lines\":\"Console.WriteLine(\\\"Hello\\\");\\nConsole.ReadLine();\",\"new_lines\":\"Console.WriteLine(\\\"Hi\\\");\\n\"} replaces lines 5-6 with a single line.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Relative path to file." } },
                        { "start_line", new ToolDetails { Type = "integer", Description = "Starting line number (1-indexed, inclusive, positive integer (>= 1))." } },
                        { "old_lines", new ToolDetails { Type = "string", Description = "The exact text currently occupying the lines from start_line through the end of the block. The tool verifies this text matches before replacing. Can contain multiple lines separated by \\n or \\r\\n. Must not be empty." } },
                        { "new_lines", new ToolDetails { Type = "string", Description = "New text to replace the lines. Can contain multiple lines separated by \\n or \\r\\n. If empty string, the line range is deleted." } }
                    },
                    Required = new List<string> { "file_path", "start_line", "old_lines", "new_lines" }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                var (filePath, startLine, oldLinesText, newLinesText, error) = ExtractAndValidateParameters(parameters);
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

                var (originalContent, fileEncoding, hasBom) = await _fileSystem.ReadAllTextWithDetectedEncodingAsync(absolutePath, cancellationToken).ConfigureAwait(false);
                string separator = originalContent.Contains("\r\n") ? "\r\n" : "\n";

                string[] lines = originalContent.Split(new[] { separator }, StringSplitOptions.None);
                var linesList = new List<string>(lines);

                string[] oldLinesNormalized = oldLinesText.Split(
                    new[] { "\r\n", "\n", "\r" },
                    StringSplitOptions.None
                );

                if (oldLinesNormalized.Length > 0 && oldLinesNormalized[oldLinesNormalized.Length - 1] == "")
                {
                    oldLinesNormalized = oldLinesNormalized.Take(oldLinesNormalized.Length - 1).ToArray();
                }
                int oldLinesCount = oldLinesNormalized.Length;

                int resolvedEndLine = startLine + oldLinesCount - 1;

                string[] existingBlock = new string[oldLinesCount];
                for (int i = 0; i < oldLinesCount; i++)
                {
                    int lineIdx = startLine - 1 + i;
                    existingBlock[i] = lineIdx < linesList.Count ? linesList[lineIdx] : "";
                }

                for (int i = 0; i < oldLinesCount; i++)
                {
                    if (existingBlock[i] != oldLinesNormalized[i])
                    {
                        string expectedLine = oldLinesNormalized[i];
                        string actualLine = existingBlock[i];
                        return Error(
                            $"Old content mismatch at line {startLine + i}: " +
                            $"expected \"{TruncateForError(expectedLine)}\", " +
                            $"but found \"{TruncateForError(actualLine)}\". " +
                            $"Re-read the file to get current line numbers and content.");
                    }
                }


                string[] newLines = null;
                bool hasNewLines = !string.IsNullOrEmpty(newLinesText);
                if (hasNewLines)
                {
                    newLines = newLinesText.Split(
                        new[] { "\r\n", "\n", "\r" },
                        StringSplitOptions.None
                    );

                    if (newLines.Length > 0 && newLines[newLines.Length - 1] == "")
                    {
                        newLines = newLines.Take(newLines.Length - 1).ToArray();
                    }
                }

                while (linesList.Count < startLine - 1)
                    linesList.Add("");

                int removeStart = startLine - 1;
                int removeCount = Math.Min(resolvedEndLine - startLine + 1, linesList.Count - removeStart);
                if (removeCount > 0)
                    linesList.RemoveRange(removeStart, removeCount);

                if (hasNewLines)
                    linesList.InsertRange(removeStart, newLines);

                string newContent = string.Join(separator, linesList);

                await _snapshotManager.SnapshotFileAsync(absolutePath, SnapshotChangeStatus.BeforeModify, cancellationToken).ConfigureAwait(false);
                await _fileSystem.WriteAllBytesWithEncodingAsync(absolutePath, newContent, fileEncoding, hasBom, cancellationToken).ConfigureAwait(false);

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

        private (string filePath, int startLine, string oldLines, string newLines, string error) ExtractAndValidateParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, 0, null, null, "Parameters cannot be null.");

            if (!parameters.TryGetValue("file_path", out object filePathObj) || !(filePathObj is string filePath))
                return (null, 0, null, null, "file_path parameter is required and must be a string.");

            if (!parameters.TryGetValue("start_line", out object startObj) || !TryParseInt(startObj, out int startLine))
                return (null, 0, null, null, "start_line parameter is required and must be an integer.");

            if (!parameters.TryGetValue("old_lines", out object oldLinesObj) || !(oldLinesObj is string oldLines))
                return (null, 0, null, null, "old_lines parameter is required and must be a string.");

            if (string.IsNullOrEmpty(oldLines))
                return (null, 0, null, null, "old_lines must not be empty.");

            if (!parameters.TryGetValue("new_lines", out object newLinesObj) || !(newLinesObj is string newLines))
                return (null, 0, null, null, "new_lines parameter is required and must be a string.");

            if (startLine < 1)
                return (null, 0, null, null, "start_line must be >= 1.");

            return (filePath, startLine, oldLines, newLines, null);
        }

        private bool TryParseInt(object value, out int result) => int.TryParse(value?.ToString(), out result);

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var filePath = parameters?.TryGetValue("file_path", out var f) == true ? f?.ToString() : "";
            var start = parameters?.TryGetValue("start_line", out var s) == true ? s?.ToString() : "?";
            return $"Replacing lines starting at line {start} in '{filePath}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is ReplaceLinesResponse response)
            {
                if (!response.Success)
                    return "Replacing lines failed.";
                if (response.SyntaxErrors != null && response.SyntaxErrors.Length > 0)
                    return $"Lines replaced with {response.SyntaxErrors.Length} syntax error(s).";
                return "Lines replaced successfully.";
            }
            return "Replacing lines finished.";
        }

        private static string TruncateForError(string value, int maxLength = 80)
        {
            if (value == null) return "<null>";
            if (value.Length <= maxLength) return value;
            return value.Substring(0, maxLength) + "...";
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

        [JsonProperty("syntax_errors", NullValueHandling = NullValueHandling.Ignore)]
        public string[] SyntaxErrors { get; set; }
    }
}
