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
                Description = "Replaces a range of lines in a file by line numbers (1-indexed). Before this call, obtain a fresh read_file_lines result for the same file and copy old_lines verbatim from it — never retype it or use search results or an older read. The tool verifies old_lines before changing the file. Leading whitespace and character casing are significant; trailing whitespace and line-ending differences are ignored. The replaced range ends at start_line + number_of_lines_in_old_lines - 1. After a replacement, line numbers may shift; re-read before making further edits, or apply multiple edits bottom-up (largest line numbers first). If start_line is beyond the current line count, the file is padded with empty lines up to start_line - 1, then new_lines are inserted. If start_line is valid but the old_lines range extends past the end of the file, missing trailing lines are treated as empty strings for comparison only. Set new_lines to an empty string to delete the range. If syntax_errors is non-empty, the file is still saved, but the change is not complete; re-read the affected range and fix the errors before considering the task complete. If old_lines does not match at start_line, the tool searches nearby lines (±50). If exactly one exact match is found, it auto-corrects and applies the change (auto_corrected=true); re-read before making another edit. If multiple exact matches are found, candidates are returned; re-read a wider range and expand old_lines to disambiguate. If no exact match is found, the tool retries once with the first line's leading indentation ignored. If exactly one relaxed match is found, it applies the change, sets matched_ignoring_first_line_indent=true, and re-indents the first line of new_lines to match the file. Re-read before continuing. If multiple relaxed matches are found, candidates are returned. If no match is found, check leading whitespace and re-read the file.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Relative path to file." } },
                        { "start_line", new ToolDetails { Type = "integer", Description = "Starting line number (1-indexed, inclusive, positive integer (>= 1))." } },
                        { "old_lines", new ToolDetails { Type = "string", Description = "Exact text currently occupying the lines from start_line through the end of the block. Copy it verbatim from a fresh read_file_lines result for the same file — never retype it and never use search results or an older read. Leading whitespace and character casing must match exactly; trailing whitespace and line-ending style (\r\n vs \n) are ignored during comparison. If the requested range extends past the current line count, missing trailing lines are treated as empty strings for comparison. Can contain multiple lines separated by \n or \r\n. Must not be empty." } },
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
