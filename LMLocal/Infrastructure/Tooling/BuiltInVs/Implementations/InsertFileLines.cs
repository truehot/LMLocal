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
        private readonly ISyntaxCheckerFactory _syntaxFactory;

        public string ToolName => "insert_file_lines";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.FullAccess;

        public InsertFileLines(
            IVsDependencies vsDependencies,
            IPathResolver pathResolver,
            ISnapshotManager snapshotManager,
            IFileSystem fileSystem,
            ISyntaxCheckerFactory syntaxFactory)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _syntaxFactory = syntaxFactory ?? throw new ArgumentNullException(nameof(syntaxFactory));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Inserts lines at a specific position in a file. "
                    + "Lines are 1-indexed: position=0 inserts before the first line, "
                    + "position=5 inserts after line 5. Automatically pads the file "
                    + "with empty lines if position exceeds the current line count. "
                    + "The new_lines string can contain multiple lines separated by "
                    + "\\n or \\r\\n. Must not be empty. Fails if the file does not "
                    + "exist or is outside the solution directory. "
                    + "If expected_line is provided and the line at 'position' doesn't "
                    + "match, the tool searches nearby lines (±50) and auto-corrects "
                    + "the position.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails
                            { Type = "string", Description = "Absolute or relative path to file." }
                        },
                        { "position", new ToolDetails
                            { Type = "integer",Description = "Line number after which to insert (1-indexed). Use 0 to insert before the first line. Must be >= 0." }
                        },
                        { "new_lines", new ToolDetails
                            { Type = "string",Description = "Text to insert. Can contain multiple lines separated by \\n or \\r\\n. Must not be empty." }
                        },
                        { "expected_line", new ToolDetails
                            { Type = "string",Description = "Optional. The exact text of the line AFTER which to insert. If provided and the line has shifted, the tool searches nearby and auto-corrects position. Ignored when position=0." }
                        }
                    },
                    Required = new List<string> { "file_path", "position", "new_lines" }
                }
            };
        }

        public async Task<object> ExecuteAsync(
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (filePath, position, newLinesText, expectedLine, error) = ExtractAndValidateParameters(parameters);
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
                catch (ArgumentException ex)
                {
                    return Error($"Invalid file path: {ex.Message}");
                }

                if (!_fileSystem.FileExists(absolutePath))
                    return Error($"File not found: {filePath}");

                var (originalContent, fileEncoding, hasBom) = await _fileSystem.ReadAllTextWithDetectedEncodingAsync(absolutePath, cancellationToken);

                string separator = originalContent.Contains("\r\n") ? "\r\n" : "\n";

                string[] lines = originalContent.Split(new[] { separator }, StringSplitOptions.None);
                var linesList = new List<string>(lines);

                bool hadTrailingNewline = linesList.Count > 0 && linesList[linesList.Count - 1] == "" && originalContent.EndsWith(separator);
                if (hadTrailingNewline)
                    linesList.RemoveAt(linesList.Count - 1);

                string normalizedInsert = newLinesText.Replace("\r\n", "\n").Replace("\r", "\n");

                string[] newLines = normalizedInsert.Split('\n');

                int originalPosition = position;
                bool autoCorrected = false;

                if (!string.IsNullOrEmpty(expectedLine)
                    && position > 0
                    && position <= linesList.Count)
                {
                    int checkIdx = position - 1;
                    if (checkIdx >= linesList.Count || !LineMatcher.LinesEqual(linesList[checkIdx], expectedLine))
                    {
                        var matches = LineMatcher.FindMatches(linesList, expectedLine, position);

                        if (matches.Count == 1)
                        {
                            position = matches[0];
                            autoCorrected = true;
                        }
                        else if (matches.Count > 1)
                        {
                            return Error(new InsertLinesResponse
                            {
                                Success = false,
                                FilePath = filePath,
                                ErrorMessage = $"expected_line matches {matches.Count} locations. Re-read the file or use a more specific expected_line.",
                                Candidates = LineMatcher.BuildCandidates(linesList, matches)
                            });
                        }
                        else
                        {
                            return Error("expected_line not found. The file may have changed. Re-read the file with read_file_lines.");
                        }
                    }
                }

                while (linesList.Count < position)
                    linesList.Add("");

                bool isAppendingToEnd = position >= linesList.Count;

                if (position == 0)
                {
                    linesList.InsertRange(0, newLines);
                }
                else
                {
                    linesList.InsertRange(position, newLines);
                }

                if (hadTrailingNewline || isAppendingToEnd)
                    linesList.Add("");

                string newContent = string.Join(separator, linesList);

                await _snapshotManager.SnapshotFileAsync(absolutePath, SnapshotChangeStatus.BeforeModify, cancellationToken);

                await _fileSystem.WriteAllBytesWithEncodingAsync(absolutePath, newContent, fileEncoding, hasBom, cancellationToken);

                string[] syntaxErrors = null;
                var checker = _syntaxFactory.GetChecker(absolutePath);
                if (checker != null && !checker.IsSyntaxValid(newContent, out var errors))
                {
                    syntaxErrors = errors.Select(e => $"{e.Id}: {e.GetMessage()}").ToArray();
                    InternalLogger.Info($"Syntax errors detected after insertion in {absolutePath}:\n" + string.Join("\n", syntaxErrors));
                }

                _pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath);

                var response = new InsertLinesResponse
                {
                    Success = true,
                    FilePath = relativePath ?? absolutePath,
                    LinesInserted = newLines.Length,
                    SyntaxErrors = syntaxErrors
                };

                if (autoCorrected)
                {
                    response.AutoCorrected = true;
                    response.OriginalPosition = originalPosition;
                    response.AppliedPosition = position;
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

        private (string filePath, int position, string newLines, string expectedLine,
            string error) ExtractAndValidateParameters(
                Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, 0, null, null, "Parameters cannot be null.");

            if (!parameters.TryGetValue("file_path", out object filePathObj) || !(filePathObj is string filePath))
                return (null, 0, null, null, "file_path parameter is required and must be a string.");

            if (!parameters.TryGetValue("position", out object posObj) || !TryParseInt(posObj, out int position))
                return (null, 0, null, null, "position parameter is required and must be an integer.");

            if (!parameters.TryGetValue("new_lines", out object newLinesObj) || !(newLinesObj is string newLines))
                return (null, 0, null, null, "new_lines parameter is required and must be a string.");

            if (position < 0)
                return (null, 0, null, null, "position must be >= 0.");

            if (string.IsNullOrEmpty(newLines))
                return (null, 0, null, null, "new_lines must not be empty.");

            string expectedLine = null;
            if (parameters.TryGetValue("expected_line", out object expectedObj) && expectedObj is string el && !string.IsNullOrEmpty(el))
            {
                expectedLine = el;
            }

            return (filePath, position, newLines, expectedLine, null);
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
            {
                if (!response.Success)
                    return $"Inserting lines failed: {response.ErrorMessage}";

                string msg = $"{response.LinesInserted} line(s) inserted";
                if (response.AutoCorrected == true)
                    msg += $" (auto-corrected from position {response.OriginalPosition} to {response.AppliedPosition})";
                if (response.SyntaxErrors != null && response.SyntaxErrors.Length > 0)
                    msg += $" with {response.SyntaxErrors.Length} syntax error(s)";

                msg += ".";
                return msg;
            }
            return "Inserting lines finished.";
        }

        private static InsertLinesResponse Error(string message)
        {
            return new InsertLinesResponse
            { Success = false, ErrorMessage = message };
        }

        private static InsertLinesResponse Error(InsertLinesResponse response)
        {
            return response;
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

        [JsonProperty("syntax_errors", NullValueHandling = NullValueHandling.Ignore)]
        public string[] SyntaxErrors { get; set; }

        [JsonProperty("auto_corrected", NullValueHandling = NullValueHandling.Ignore)]
        public bool? AutoCorrected { get; set; }

        [JsonProperty("original_position", NullValueHandling = NullValueHandling.Ignore)]
        public int? OriginalPosition { get; set; }

        [JsonProperty("applied_position", NullValueHandling = NullValueHandling.Ignore)]
        public int? AppliedPosition { get; set; }

        [JsonProperty("candidates", NullValueHandling = NullValueHandling.Ignore)]
        public List<LineCandidate> Candidates { get; set; }
    }
}
