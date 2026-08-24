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
    internal interface IReplaceFileLines : IBuiltInTool
    {
    }

    internal class ReplaceFileLines : IReplaceFileLines
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly ISnapshotManager _snapshotManager;
        private readonly IFileSystem _fileSystem;
        private readonly ISyntaxCheckerFactory _syntaxFactory;

        public string ToolName => "replace_file_lines";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.FullAccess;

        public ReplaceFileLines(
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
                Description = "Replaces a range of lines in a file by line numbers (1-indexed). "
                    + "The old_lines parameter must exactly match the existing content at the "
                    + "target location — the tool verifies this before making any changes. "
                    + "old_lines must be copied verbatim from the output of read_file_lines, "
                    + "including ALL leading spaces/tabs and character casing. Do NOT retype, "
                    + "reformat, or manually type the lines. Only line endings (\\r\\n vs \\n) "
                    + "and trailing whitespace are ignored during comparison; leading whitespace "
                    + "and casing are significant. The range ends at "
                    + "start_line + number_of_lines_in_old_lines - 1. After replacement, line "
                    + "numbers shift — re-read the file if you need accurate positions for "
                    + "subsequent edits. If start_line exceeds the current line count, the file "
                    + "is padded with empty lines up to start_line - 1, then new_lines are "
                    + "inserted. Set new_lines to an empty string to delete the range. If syntax "
                    + "errors are detected after replacement, they are reported in syntax_errors "
                    + "field, but the file is still saved. Apply multiple edits bottom‑up "
                    + "(largest line numbers first) to prevent line‑shift errors. If old_lines "
                    + "does not match at start_line, the tool searches nearby lines (±50) for "
                    + "the block. If exactly one exact match is found, it auto-corrects and "
                    + "applies the change (auto_corrected=true in response). If multiple exact "
                    + "matches are found, candidates with start_line and text are returned — "
                    + "use a larger old_lines block or re-read the file to disambiguate (no "
                    + "retry). If no exact block match is found, the tool retries once as a "
                    + "fallback, comparing the first line with its leading indentation ignored "
                    + "(this covers models that type the first line without its initial "
                    + "spaces); all other lines must still match exactly. If that relaxed "
                    + "search finds exactly one location, it applies the change, auto-corrects "
                    + "the position if needed, and sets matched_ignoring_first_line_indent=true "
                    + "in the response, and the first line of new_lines is re-indented to "
                    + "match the file's indentation. If multiple relaxed matches are found, "
                    + "candidates are returned. If no match is found at all, the error advises "
                    + "checking leading whitespace and re-reading the file.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Relative path to file." } },
                        { "start_line", new ToolDetails { Type = "integer", Description = "Starting line number (1-indexed, inclusive, positive integer (>= 1))." } },
                        { "old_lines", new ToolDetails { Type = "string", Description = "The exact text currently occupying the lines from start_line through the end of the block. The tool verifies this text matches (ignoring line ending differences) before replacing. If the file has fewer lines than needed, missing lines are treated as empty strings. Can contain multiple lines separated by \\n or \\r\\n. Must not be empty." } },
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

                var (originalContent, fileEncoding, hasBom) = await _fileSystem
                    .ReadAllTextWithDetectedEncodingAsync(absolutePath, cancellationToken)
                    .ConfigureAwait(false);

                string separator = originalContent.Contains("\r\n") ? "\r\n" : "\n";

                string[] fileLines = originalContent.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                var linesList = new List<string>(fileLines);

                string[] oldLinesArray = SplitLines(oldLinesText);
                int oldLinesCount = oldLinesArray.Length;
                int originalStartLine = startLine;

                int requiredTotalLines = startLine - 1 + oldLinesCount;
                while (linesList.Count < requiredTotalLines)
                    linesList.Add("");

                var (resolvedStartLine, matchedIgnoringIndent, errorResponse) = ResolveStartLine(linesList, oldLinesArray, startLine, filePath);
                if (errorResponse != null)
                    return errorResponse;

                bool autoCorrected = resolvedStartLine != startLine;
                startLine = resolvedStartLine;

                string matchedFirstLineIndent = matchedIgnoringIndent
                    ? GetLeadingWhitespace(linesList[startLine - 1])
                    : null;


                string[] newLinesArray = null;
                bool hasNewLines = !string.IsNullOrEmpty(newLinesText);
                if (hasNewLines)
                    newLinesArray = SplitLines(newLinesText);

                if (matchedIgnoringIndent && hasNewLines && newLinesArray.Length > 0)
                {
                    string firstLineContent = newLinesArray[0].TrimStart();
                    if (firstLineContent.Length > 0)
                        newLinesArray[0] = matchedFirstLineIndent + firstLineContent;
                }


                int removeStart = startLine - 1;
                linesList.RemoveRange(removeStart, oldLinesCount);

                if (hasNewLines)
                    linesList.InsertRange(removeStart, newLinesArray);

                string newContent = string.Join(separator, linesList);

                await _snapshotManager
                    .SnapshotFileAsync(absolutePath, SnapshotChangeStatus.BeforeModify, cancellationToken)
                    .ConfigureAwait(false);

                await _fileSystem
                    .WriteAllBytesWithEncodingAsync(absolutePath, newContent, fileEncoding, hasBom, cancellationToken)
                    .ConfigureAwait(false);

                string[] syntaxErrors = null;
                var checker = _syntaxFactory.GetChecker(absolutePath);
                if (checker != null && !checker.IsSyntaxValid(newContent, out var errors))
                {
                    syntaxErrors = errors.Select(e => $"{e.Id}: {e.GetMessage()}").ToArray();
                    InternalLogger.Info($"Syntax errors detected after replacement in {absolutePath}:\n" + string.Join("\n", syntaxErrors));
                }

                _pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath);

                var response = new ReplaceLinesResponse
                {
                    Success = true,
                    FilePath = relativePath ?? absolutePath,
                    SyntaxErrors = syntaxErrors
                };

                if (autoCorrected)
                {
                    response.AutoCorrected = true;
                    response.OriginalStartLine = originalStartLine;
                    response.AppliedStartLine = startLine;
                }

                if (matchedIgnoringIndent)
                    response.MatchedIgnoringFirstLineIndent = true;

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

        private (int resolvedStartLine, bool matchedIgnoringIndent, ReplaceLinesResponse errorResponse) ResolveStartLine(List<string> linesList, string[] oldLinesArray, int startLine, string filePath)
        {
            int oldLinesCount = oldLinesArray.Length;

            bool exactMatch = true;
            for (int i = 0; i < oldLinesCount; i++)
            {
                int lineIdx = startLine - 1 + i;
                if (lineIdx >= linesList.Count || !LineMatcher.LinesEqual(linesList[lineIdx], oldLinesArray[i]))
                {
                    exactMatch = false;
                    break;
                }
            }

            if (exactMatch)
                return (startLine, false, null);

            var blockMatches = FindBlock(linesList, oldLinesArray, startLine);

            if (blockMatches.Count == 1)
                return (blockMatches[0], false, null);

            if (blockMatches.Count > 1)
            {
                return (0, false, Error(new ReplaceLinesResponse
                {
                    Success = false,
                    FilePath = filePath,
                    ErrorMessage = $"old_lines matches {blockMatches.Count} locations. Use a larger old_lines block to disambiguate, or re-read the file.",
                    Candidates = ToCandidates(linesList, blockMatches, oldLinesCount)
                }));
            }

            // Retry with the first line compared ignoring leading whitespace; the remaining lines must still match exactly.
            var relaxedMatches = FindBlock(linesList, oldLinesArray, startLine, ignoreLeadingWhitespaceFirstLine: true);

            if (relaxedMatches.Count == 1)
                return (relaxedMatches[0], true, null);

            if (relaxedMatches.Count > 1)
            {
                return (0, false, Error(new ReplaceLinesResponse
                {
                    Success = false,
                    FilePath = filePath,
                    ErrorMessage = $"old_lines matches {relaxedMatches.Count} locations when leading indentation of the first line is ignored. Use a larger old_lines block to disambiguate, or re-read the file.",
                    Candidates = ToCandidates(linesList, relaxedMatches, oldLinesCount)
                }));
            }

            var firstLineMatches = LineMatcher.FindMatches(linesList, oldLinesArray[0], startLine);

            if (firstLineMatches.Count > 0)
            {
                return (0, false, Error(new ReplaceLinesResponse
                {
                    Success = false,
                    FilePath = filePath,
                    ErrorMessage = $"First line of old_lines found at {firstLineMatches.Count} location(s), but the full block didn't match. Re-read the file or retry with the suggested start_line.",
                    Candidates = ToCandidates(linesList, firstLineMatches, oldLinesCount)
                }));
            }

            return (0, false, Error("Old content not found in file. Check leading whitespace and indentation, copy old_lines verbatim from read_file_lines, or re-read the file."));
        }

        /// <summary>
        /// Searches for a full block match within ±SearchWindow of aroundLine.
        /// When ignoreLeadingWhitespaceFirstLine is true, the first line of the block is
        /// compared with leading whitespace ignored; all other lines are compared exactly.
        /// </summary>
        private static List<int> FindBlock(
            List<string> lines, string[] block, int aroundLine, bool ignoreLeadingWhitespaceFirstLine = false)
        {
            int windowLines = LineMatcher.MaxSearchWindowLines;
            int lower = Math.Max(0, aroundLine - 1 - windowLines);
            int upper = Math.Min(lines.Count - block.Length, aroundLine - 1 + windowLines);
            var result = new List<int>();
            for (int i = lower; i <= upper; i++)
            {
                bool match = true;
                for (int j = 0; j < block.Length; j++)
                {
                    bool lineMatches = j == 0 && ignoreLeadingWhitespaceFirstLine
                        ? LineMatcher.LinesEqualIgnoringLeadingWhitespace(lines[i + j], block[j])
                        : LineMatcher.LinesEqual(lines[i + j], block[j]);

                    if (!lineMatches)
                    {
                        match = false; break;
                    }
                }
                if (match) result.Add(i + 1);
            }
            return result;
        }

        private static List<LineCandidate> ToCandidates(
            List<string> linesList, List<int> positions, int blockLength = 1)
        {
            return LineMatcher.BuildCandidates(linesList, positions, blockLength);
        }

        private (string filePath, int startLine, string oldLines, string newLines, string error)
            ExtractAndValidateParameters(Dictionary<string, object> parameters)
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

        /// <summary>
        /// Splits text into lines. If the text ends with \n or \r\n, the trailing empty element is not included as a separate line.
        /// </summary>
        private static string[] SplitLines(string text)
        {
            string[] parts = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            if (parts.Length > 0
                && parts[parts.Length - 1] == ""
                && (text.EndsWith("\r\n") || text.EndsWith("\n")))
            {
                var trimmed = new string[parts.Length - 1];
                Array.Copy(parts, trimmed, trimmed.Length);
                return trimmed;
            }

            return parts;
        }

        /// <summary>
        /// Returns the leading whitespace (spaces and tabs) of a line.
        /// </summary>
        private static string GetLeadingWhitespace(string line)
        {
            int i = 0;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
                i++;
            return line.Substring(0, i);
        }


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

                string msg = "Lines replaced";
                var notes = new List<string>();
                if (response.AutoCorrected == true)
                    notes.Add($"auto-corrected from line {response.OriginalStartLine} to {response.AppliedStartLine}");
                if (response.MatchedIgnoringFirstLineIndent == true)
                    notes.Add("matched ignoring first-line indentation");
                if (notes.Count > 0)
                    msg += " (" + string.Join(", ", notes) + ")";
                if (response.SyntaxErrors != null && response.SyntaxErrors.Length > 0)
                    msg += $" with {response.SyntaxErrors.Length} syntax {Pluralizer.Pluralize(response.SyntaxErrors.Length, "error", "errors")}";
                msg += ".";
                return msg;
            }
            return "Replacing lines finished.";
        }

        private static ReplaceLinesResponse Error(string message)
        {
            return new ReplaceLinesResponse { Success = false, ErrorMessage = message };
        }

        private static ReplaceLinesResponse Error(ReplaceLinesResponse response)
        {
            return response;
        }
    }

    internal class ReplaceLinesResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }

        [JsonProperty("syntax_errors", NullValueHandling = NullValueHandling.Ignore)]
        public string[] SyntaxErrors { get; set; }

        [JsonProperty("auto_corrected", NullValueHandling = NullValueHandling.Ignore)]
        public bool? AutoCorrected { get; set; }

        [JsonProperty("original_start_line", NullValueHandling = NullValueHandling.Ignore)]
        public int? OriginalStartLine { get; set; }

        [JsonProperty("applied_start_line", NullValueHandling = NullValueHandling.Ignore)]
        public int? AppliedStartLine { get; set; }

        [JsonProperty("matched_ignoring_first_line_indent", NullValueHandling = NullValueHandling.Ignore)]
        public bool? MatchedIgnoringFirstLineIndent { get; set; }

        [JsonProperty("candidates", NullValueHandling = NullValueHandling.Ignore)]
        public List<LineCandidate> Candidates { get; set; }
    }
}
