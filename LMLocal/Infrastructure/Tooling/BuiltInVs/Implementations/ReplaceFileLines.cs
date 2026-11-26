using LMLocal.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Syntax;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections;

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

        /// <summary>Several edits of one file in one call; Docs\multi-replace-file-lines-plan.md.</summary>
        internal const string EditsParameter = "edits";

        internal const int MaxEdits = 20;

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
                Description = "Replaces a range of lines in a file by line numbers (1-indexed). Take start_line and old_lines from a read_file_lines result of the same file: old_lines is the raw text of those lines, the second element of each [line_number, text] pair (see old_lines). The tool verifies old_lines before changing the file. The replaced range ends at start_line + number_of_lines_in_old_lines - 1. " +
                              "Set new_lines to an empty string to delete the range. " +
                              "SEVERAL EDITS IN ONE FILE: put them all in 'edits' in ONE call, every start_line and old_lines from the same read, in any order - the tool applies them bottom-up itself. If any edit does not match, nothing is written. Edits must not overlap. " +
                              "If start_line is beyond the current line count, the file is padded with empty lines up to start_line - 1 (at most 50; more is rejected with the real line count). If the old_lines range extends past the end of the file, missing trailing lines are treated as empty strings for comparison only. " +
                              "If old_lines does not match at start_line, the tool searches nearby lines (±50); a single match is applied there and reported by a flag: auto_corrected=true (exact match), matched_ignoring_first_line_indent=true (first line's indentation ignored; the first line of new_lines is re-indented to match the file), or matched_with_normalization=true (typographic spaces, quotes, dashes normalized and invisible characters - zero-width, soft hyphen, bidi marks, control characters, noncharacters such as U+FFFF - ignored; unchanged lines are kept from the file, and invisible_characters lists the invisible characters of the matched lines: kept on lines written back unchanged, removed from lines rewritten from new_lines). The flags only report how the match was found: the change is written. " +
                              "success=false means nothing was written; error_message says why, and candidates list the start lines when old_lines matches several places. Only then re-read the file and retry; after a successful edit you do not need to re-read it. For .cs and .js files the syntax is checked automatically: if syntax_errors is non-empty, the file is saved but the change is not complete - re-read the affected range and fix the errors.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails
                            {
                                Type = "string",
                                Description = "Relative path to file."
                            }
                        },
                        { "start_line", new ToolDetails
                            {
                                Type = "integer",
                                Description = "Starting line number (1-indexed, inclusive, positive integer (>= 1)). A start_line more than 50 lines past the end of the file is rejected with the real line count. Required unless 'edits' is passed."
                            }
                        },
                        { "old_lines", new ToolDetails
                            {
                                Type = "string",
                                Description = "Raw source text currently occupying the lines from start_line through the end of the block, copied from read_file_lines (not from search results). Edits lower in the file do not change it. No line-number prefix and no trailing newline. Leading whitespace and character casing must match exactly; trailing whitespace and line-ending style (\\r\\n vs \\n) are ignored during comparison. If the requested range extends past the current line count, missing trailing lines are treated as empty strings for comparison. Can contain multiple lines separated by \\n or \\r\\n. Must not be empty. Required unless 'edits' is passed."
                            }
                        },
                        { "new_lines", new ToolDetails
                            {
                                Type = "string",
                                Description = "Raw source text to replace the selected lines with. Can contain multiple lines separated by \\n or \\r\\n. If empty string, the line range is deleted. Required unless 'edits' is passed."
                            }
                        },
                        { EditsParameter, new ToolDetails
                            {
                                Type = "array",
                                Description = $"Several edits of this file in one call (up to {MaxEdits}), each like a single edit. Every start_line and old_lines comes from the same read of the file; any order - the tool applies them bottom-up. " +
                                              "Nothing is written if any edit does not match. Edits must not overlap and must lie within the file. " +
                                              "To insert lines, use the line before (or after) the insertion point as old_lines and repeat it in new_lines.",
                                Items = new ToolDetails
                                {
                                    Type = "object",
                                    Properties = new Dictionary<string, ToolDetails>
                                    {
                                        { "start_line", new ToolDetails { Type = "integer", Description = "Starting line number (1-indexed, >= 1) in the file as read." } },
                                        { "old_lines", new ToolDetails { Type = "string", Description = "Raw source text currently at start_line, copied from read_file_lines. Must not be empty." } },
                                        { "new_lines", new ToolDetails { Type = "string", Description = "Raw source text to replace those lines with; empty string deletes them." } }
                                    },
                                    Required = new List<string> { "start_line", "old_lines", "new_lines" }
                                }
                            }
                        }
                    },
                    Required = new List<string> { "file_path" }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                string pruneRejection = PrunedContentGuard.Validate(parameters);
                if (pruneRejection != null)
                    return Error(pruneRejection);

                object editsObj = null;
                parameters?.TryGetValue(EditsParameter, out editsObj);
                if (!LiteralListParameter.IsMissing(editsObj))
                    return await ReplaceManyAsync(parameters, editsObj, cancellationToken).ConfigureAwait(false);

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

                _pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath);

                string[] fileLines = LineSplitter.Split(originalContent);
                bool hadTrailingNewline = LineSplitter.HasTrailingEol(originalContent);
                var linesList = new List<string>(fileLines);

                string[] oldLinesArray = LineSplitter.Split(oldLinesText);
                int oldLinesCount = oldLinesArray.Length;
                int originalStartLine = startLine;

                long requiredTotalLines = (long)(startLine - 1) + oldLinesCount;
                long padding = requiredTotalLines - fileLines.Length;

                string reportedPath = relativePath ?? filePath;

                if (padding > LineMatcher.MaxPaddingLines)
                {
                    return MatchFailure(
                        reportedPath,
                        $"start_line {startLine} with {oldLinesCount} line(s) of old_lines needs {padding} empty line(s) " +
                        $"past the end of a file that has {fileLines.Length} line(s); at most {LineMatcher.MaxPaddingLines} " +
                        $"are allowed. Re-read the file with read_file_lines to get its real line numbers.");
                }

                while (linesList.Count < requiredTotalLines)
                    linesList.Add("");

                var edit = PrepareEdit(fileLines, linesList, startLine, oldLinesText, newLinesText, reportedPath, absolutePath, out var errorResponse);
                if (edit == null)
                    return errorResponse;

                bool autoCorrected = edit.AutoCorrected;
                bool matchedIgnoringIndent = edit.MatchedIgnoringIndent;
                bool matchedWithNormalization = edit.MatchedWithNormalization;
                startLine = edit.AppliedStartLine;
                string[] newLinesArray = edit.NewLines;
                bool hasNewLines = newLinesArray != null;
                int removeStart = edit.RemoveStart;
                int realCount = edit.RealCount;

                if (edit.IsNoOp)
                {
                    InternalLogger.Debug(
                        $"replace_file_lines: no-op on {absolutePath}, lines {startLine}-{startLine + realCount - 1} already hold the requested text; nothing written.");

                    var noOp = NoOp(
                        relativePath, absolutePath, autoCorrected, originalStartLine, startLine,
                        matchedIgnoringIndent, matchedWithNormalization);
                    noOp.Note = OtherMatchesNote(edit, Enumerable.Empty<PreparedEdit>());
                    return noOp;
                }

                linesList.RemoveRange(removeStart, oldLinesCount);

                if (hasNewLines)
                    linesList.InsertRange(removeStart, newLinesArray);

                string newContent = LineSplitter.Join(separator, linesList, hadTrailingNewline);

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

                var response = new ReplaceLinesResponse
                {
                    Success = true,
                    FilePath = relativePath ?? absolutePath,
                    LinesReplaced = hasNewLines ? newLinesArray.Length : 0,
                    LinesRemoved = hasNewLines ? (int?)null : realCount,
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

                if (matchedWithNormalization)
                    response.MatchedWithNormalization = true;

                response.InvisibleCharacters = edit.InvisibleCharacters;
                response.Note = OtherMatchesNote(edit, Enumerable.Empty<PreparedEdit>());

                return response;
            }
            catch (OperationCanceledException)
            {
                return Error("Operation was cancelled.");
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error in {ToolName}", ex);
                return Error($"Error: {ex.Message}");
            }
        }

        /// <summary>One edit resolved against the file as read; nothing is changed yet.</summary>
        private sealed class PreparedEdit
        {
            public int RequestedStartLine;
            public int AppliedStartLine;
            public int OldCount;
            public int RealCount;

            /// <summary>Lines to write, after re-indent and restore of unchanged lines; null when the range is deleted.</summary>
            public string[] NewLines;

            public bool MatchedIgnoringIndent;
            public bool MatchedWithNormalization;
            public string[] InvisibleCharacters;
            public bool IsNoOp;

            /// <summary>Other blocks matching old_lines when start_line was taken among several; null otherwise.</summary>
            public List<int> OtherMatches;

            public bool AutoCorrected => AppliedStartLine != RequestedStartLine;
            public int RemoveStart => AppliedStartLine - 1;
        }

        /// <summary>
        /// Resolves one edit against <paramref name="linesList"/> (the file as read, padded by the caller when a single
        /// call goes past the end) without changing it: where old_lines really is, the lines to write, and whether the
        /// range already holds them. Null with <paramref name="error"/> when old_lines cannot be placed.
        /// </summary>
        private PreparedEdit PrepareEdit(
            string[] fileLines, List<string> linesList, int startLine, string oldLinesText, string newLinesText,
            string reportedPath, string absolutePath, out ReplaceLinesResponse error)
        {
            error = null;
            string[] oldLinesArray = LineSplitter.Split(oldLinesText);

            var (resolvedStartLine, matchedIgnoringIndent, matchedWithNormalization, canonicalCache, errorResponse, otherMatches) = ResolveStartLine(linesList, oldLinesArray, startLine, reportedPath);
            if (errorResponse != null)
            {
                error = DiagnoseJsonEscapedOldLines(errorResponse, fileLines, oldLinesText, startLine, reportedPath, absolutePath) ?? errorResponse;
                return null;
            }

            string matchedFirstLineIndent = matchedIgnoringIndent
                ? GetLeadingRun(linesList[resolvedStartLine - 1])
                : null;

            string[] newLinesArray = null;
            string[] newLinesAsReceived = null;
            bool hasNewLines = !string.IsNullOrEmpty(newLinesText);
            if (hasNewLines)
            {
                newLinesArray = LineSplitter.Split(newLinesText);

                if (matchedWithNormalization)
                    newLinesAsReceived = (string[])newLinesArray.Clone();
            }

            if (matchedIgnoringIndent && hasNewLines && newLinesArray.Length > 0)
            {
                string firstLineContent = StripLeading(newLinesArray[0]);
                if (firstLineContent.Length > 0)
                    newLinesArray[0] = matchedFirstLineIndent + firstLineContent;
            }

            int removeStart = resolvedStartLine - 1;
            int realCount = Math.Min(oldLinesArray.Length, Math.Max(0, fileLines.Length - removeStart));

            if (matchedWithNormalization && hasNewLines)
            {
                RestoreUnchangedLines(
                    linesList, newLinesAsReceived, newLinesArray, oldLinesArray,
                    removeStart, realCount, matchedIgnoringIndent, canonicalCache);
            }

            return new PreparedEdit
            {
                RequestedStartLine = startLine,
                AppliedStartLine = resolvedStartLine,
                OldCount = oldLinesArray.Length,
                RealCount = realCount,
                NewLines = newLinesArray,
                MatchedIgnoringIndent = matchedIgnoringIndent,
                MatchedWithNormalization = matchedWithNormalization,
                InvisibleCharacters = matchedWithNormalization
                    ? DescribeInvisibleCharacters(linesList, removeStart, realCount, newLinesArray)
                    : null,
                IsNoOp = hasNewLines
                    && newLinesArray.Length == realCount
                    && RangeEquals(fileLines, removeStart, newLinesArray),
                OtherMatches = otherMatches
            };
        }

        /// <summary>
        /// For a canonical match: the invisible characters (see <see cref="CharacterMap.IsDeleted(char)"/>) on the matched
        /// file lines, which the model's old_lines did not carry.
        /// </summary>
        internal static string[] DescribeInvisibleCharacters(IList<string> fileLines, int removeStart, int realCount, string[] writtenLines)
        {
            var notes = new List<string>();
            for (int i = 0; i < realCount; i++)
            {
                string fileLine = fileLines[removeStart + i];
                var labels = CharacterMap.DescribeDeleted(fileLine);
                if (labels.Count == 0)
                    continue;

                string prefix = $"line {removeStart + i + 1}: ";
                if (writtenLines != null && Array.IndexOf(writtenLines, fileLine) >= 0)
                {
                    notes.Add(prefix + string.Join(", ", labels) + " kept (line written back unchanged)");
                    continue;
                }

                var kept = labels.Where(label => WrittenLinesContain(writtenLines, label)).ToList();
                var removed = labels.Where(label => !kept.Contains(label)).ToList();
                var parts = new List<string>();
                if (kept.Count > 0)
                    parts.Add(string.Join(", ", kept) + " kept");
                if (removed.Count > 0)
                    parts.Add(string.Join(", ", removed) + " removed (the line was rewritten from new_lines)");
                notes.Add(prefix + string.Join("; ", parts));
            }

            return notes.Count == 0 ? null : notes.ToArray();
        }

        /// <summary>Whether any written line holds the code point of a "U+XXXX" label from <see cref="CharacterMap.DescribeDeleted"/>.</summary>
        private static bool WrittenLinesContain(string[] writtenLines, string label)
        {
            if (writtenLines == null)
                return false;

            string character = char.ConvertFromUtf32(int.Parse(label.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            return writtenLines.Any(line => line != null && line.IndexOf(character, StringComparison.Ordinal) >= 0);
        }

        /// <summary>One requested edit of an 'edits' call; <see cref="Label"/> names it in messages.</summary>
        internal sealed class EditRequest
        {
            public string Label;
            public int StartLine;
            public string OldLines;
            public string NewLines;
        }

        private const string TopLevelEditLabel = "the top-level edit";

        /// <summary>
        /// Reads an 'edits' call: file_path plus 'edits' (an array of {start_line, old_lines, new_lines}, or such an array
        /// serialized into a string). Top-level fields with empty old_lines and new_lines are placeholders and are ignored;
        /// exact duplicate edits are kept once. <c>Note</c> says what was dropped.
        /// </summary>
        internal static (string FilePath, List<EditRequest> Edits, string Note, string Error) ExtractEdits(Dictionary<string, object> parameters, object editsObj)
        {
            if (parameters == null)
                return (null, null, null, "Parameters cannot be null.");

            if (!parameters.TryGetValue("file_path", out object filePathObj) || !(filePathObj is string filePath) || string.IsNullOrWhiteSpace(filePath))
                return (null, null, null, "file_path parameter is required and must be a string.");

            string shapeError = $"Parameter '{EditsParameter}' must be an array of objects {{start_line, old_lines, new_lines}}.";

            if (editsObj is JValue scalar)
                editsObj = scalar.Value;

            if (editsObj is string text)
            {
                try
                {
                    editsObj = JToken.Parse(text);
                }
                catch (JsonException)
                {
                    return (null, null, null, shapeError);
                }
            }

            if (!(editsObj is IEnumerable items) || editsObj is string || editsObj is JObject)
                return (null, null, null, shapeError);

            var entries = new List<(string Label, Dictionary<string, object> Values)>();
            var notes = new List<string>();

            bool hasTopLevel = new[] { "start_line", "old_lines", "new_lines" }
                .Any(k => parameters.TryGetValue(k, out object v) && !LiteralListParameter.IsMissing(v));
            if (hasTopLevel && IsEmptyText(parameters, "old_lines") && IsEmptyText(parameters, "new_lines"))
            {
                notes.Add($"Top-level start_line/old_lines/new_lines were ignored: old_lines and new_lines are empty there; only '{EditsParameter}' was applied.");
                hasTopLevel = false;
            }

            if (hasTopLevel)
            {
                entries.Add((TopLevelEditLabel, new Dictionary<string, object>
                {
                    ["start_line"] = parameters.TryGetValue("start_line", out object s) ? s : null,
                    ["old_lines"] = parameters.TryGetValue("old_lines", out object o) ? o : null,
                    ["new_lines"] = parameters.TryGetValue("new_lines", out object n) ? n : null
                }));
            }

            int index = 0;
            foreach (object item in items)
            {
                Dictionary<string, object> values;
                if (item is JObject obj)
                    values = obj.ToObject<Dictionary<string, object>>();
                else if (item is Dictionary<string, object> dict)
                    values = dict;
                else
                    return (null, null, null, $"Each item of '{EditsParameter}' must be an object {{start_line, old_lines, new_lines}}.");

                entries.Add(($"{EditsParameter}[{index++}]", values));
            }

            if (entries.Count == 0)
                return (null, null, null, $"Parameter '{EditsParameter}' has no edits.");
            if (entries.Count > MaxEdits)
                return (null, null, null, $"At most {MaxEdits} edits per call (got {entries.Count}). Split them into several calls.");

            var edits = new List<EditRequest>(entries.Count);
            foreach (var (label, values) in entries)
            {
                if (!values.TryGetValue("start_line", out object startObj) || !TryParseInt(startObj, out int startLine) || startLine < 1)
                    return (null, null, null, $"{label}: start_line is required and must be an integer >= 1.");

                if (!values.TryGetValue("old_lines", out object oldObj) || !(UnwrapString(oldObj) is string oldLines) || oldLines.Length == 0)
                {
                    if (values.TryGetValue("new_lines", out object insertObj) && UnwrapString(insertObj) is string insertText && insertText.Length > 0)
                        return (null, null, null, $"{label}: old_lines is empty but new_lines is set. To insert lines, put the line before (or after) the insertion point in old_lines and repeat it in new_lines.");
                    return (null, null, null, $"{label}: old_lines is required and must be a non-empty string.");
                }

                if (!values.TryGetValue("new_lines", out object newObj) || !(UnwrapString(newObj) is string newLines))
                    return (null, null, null, $"{label}: new_lines is required and must be a string (empty to delete the lines).");

                var duplicateOf = edits.FirstOrDefault(e => e.StartLine == startLine
                    && string.Equals(e.OldLines, oldLines, StringComparison.Ordinal)
                    && string.Equals(e.NewLines, newLines, StringComparison.Ordinal));
                if (duplicateOf != null)
                {
                    notes.Add($"{label} repeats {duplicateOf.Label} (same start_line, old_lines and new_lines) and was applied once.");
                    continue;
                }

                edits.Add(new EditRequest { Label = label, StartLine = startLine, OldLines = oldLines, NewLines = newLines });
            }

            return (filePath, edits, notes.Count == 0 ? null : string.Join(" ", notes), null);
        }

        private static object UnwrapString(object value) => value is JValue jv ? jv.Value : value;

        /// <summary>Absent, null or an empty string.</summary>
        private static bool IsEmptyText(Dictionary<string, object> parameters, string key) =>
            !parameters.TryGetValue(key, out object value) || UnwrapString(value) == null || (UnwrapString(value) is string text && text.Length == 0);

        /// <summary>
        /// Applies all edits of one file from the same read: every edit is resolved first, then they are written bottom-up
        /// in one write.
        /// </summary>
        private async Task<ReplaceLinesBatchResponse> ReplaceManyAsync(Dictionary<string, object> parameters, object editsObj, CancellationToken cancellationToken)
        {
            try
            {
                var (filePath, requests, requestNote, error) = ExtractEdits(parameters, editsObj);
                if (error != null)
                    return BatchError(error);

                if (!_vsDependencies.IsSolutionOpen)
                    return BatchError("No solution is currently open.");

                string solutionDir = _vsDependencies.GetSolutionDirectory();
                if (!_pathResolver.TryResolveFilePath(filePath, solutionDir, out string absolutePath))
                    return BatchError($"Failed to resolve file path: {filePath}");

                if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                    return BatchError($"File '{absolutePath}' is outside the solution directory.");

                try { _fileSystem.ValidateFilePath(absolutePath); }
                catch (ArgumentException ex) { return BatchError($"Invalid file path: {ex.Message}"); }

                if (!_fileSystem.FileExists(absolutePath))
                    return BatchError($"File not found: {filePath}");

                var (originalContent, fileEncoding, hasBom) = await _fileSystem
                    .ReadAllTextWithDetectedEncodingAsync(absolutePath, cancellationToken)
                    .ConfigureAwait(false);

                string separator = originalContent.Contains("\r\n") ? "\r\n" : "\n";
                _pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath);
                string reportedPath = relativePath ?? filePath;

                string[] fileLines = LineSplitter.Split(originalContent);
                bool hadTrailingNewline = LineSplitter.HasTrailingEol(originalContent);

                // No padding in a batch: every edit must match inside the file as read.
                var linesList = new List<string>(fileLines);

                var prepared = new List<PreparedEdit>(requests.Count);
                foreach (var request in requests)
                {
                    if (request.StartLine > fileLines.Length + LineMatcher.MaxSearchWindowLines)
                    {
                        return BatchMatchFailure(reportedPath, request,
                            MatchFailure(reportedPath, $"start_line {request.StartLine} is past the end of the file."), fileLines.Length);
                    }

                    var edit = PrepareEdit(fileLines, linesList, request.StartLine, request.OldLines, request.NewLines, reportedPath, absolutePath, out var editError);
                    if (edit == null)
                        return BatchMatchFailure(reportedPath, request, editError, fileLines.Length);
                    prepared.Add(edit);
                }

                string overlap = FindOverlap(requests, prepared);
                if (overlap != null)
                    return new ReplaceLinesBatchResponse { Success = false, FilePath = reportedPath, ErrorMessage = overlap };

                var results = BuildEditResults(prepared);

                if (prepared.All(p => p.IsNoOp))
                {
                    InternalLogger.Debug($"replace_file_lines: no-op on {absolutePath}, every edit already holds the requested text; nothing written.");
                    return new ReplaceLinesBatchResponse { Success = true, FilePath = reportedPath, Edits = results, Note = requestNote, IsNoOp = true };
                }

                foreach (var edit in prepared.Where(p => !p.IsNoOp).OrderByDescending(p => p.RemoveStart))
                {
                    linesList.RemoveRange(edit.RemoveStart, edit.OldCount);
                    if (edit.NewLines != null)
                        linesList.InsertRange(edit.RemoveStart, edit.NewLines);
                }

                string newContent = LineSplitter.Join(separator, linesList, hadTrailingNewline);

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

                return new ReplaceLinesBatchResponse
                {
                    Success = true,
                    FilePath = reportedPath,
                    Edits = results,
                    Note = requestNote,
                    SyntaxErrors = syntaxErrors
                };
            }
            catch (OperationCanceledException)
            {
                return BatchError("Operation was cancelled.");
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error in {ToolName}", ex);
                return BatchError($"Error: {ex.Message}");
            }
        }

        /// <summary>Message for the first pair of edits whose resolved ranges share a line, or null.</summary>
        private static string FindOverlap(List<EditRequest> requests, List<PreparedEdit> prepared)
        {
            var order = Enumerable.Range(0, prepared.Count).OrderBy(i => prepared[i].RemoveStart).ToList();
            for (int k = 1; k < order.Count; k++)
            {
                var a = prepared[order[k - 1]];
                var b = prepared[order[k]];
                int aEnd = a.AppliedStartLine + a.OldCount - 1;
                if (b.AppliedStartLine <= aEnd)
                {
                    return $"{requests[order[k - 1]].Label} (lines {a.AppliedStartLine}-{aEnd}) overlaps " +
                           $"{requests[order[k]].Label} (lines {b.AppliedStartLine}-{b.AppliedStartLine + b.OldCount - 1}). " +
                           "Merge them into one edit. Nothing was written.";
                }
            }
            return null;
        }

        private static List<ReplaceEditResult> BuildEditResults(List<PreparedEdit> prepared)
        {
            return prepared.Select(p => new ReplaceEditResult
            {
                StartLine = p.RequestedStartLine,
                AppliedStartLine = p.AutoCorrected ? (int?)p.AppliedStartLine : null,
                AutoCorrected = p.AutoCorrected ? (bool?)true : null,
                MatchedIgnoringFirstLineIndent = p.MatchedIgnoringIndent ? (bool?)true : null,
                MatchedWithNormalization = p.MatchedWithNormalization ? (bool?)true : null,
                InvisibleCharacters = p.IsNoOp ? null : p.InvisibleCharacters,
                Note = OtherMatchesNote(p, prepared.Where(o => o != p)),
                NoOp = p.IsNoOp ? (bool?)true : null,
                LinesReplaced = p.IsNoOp ? null : p.NewLines != null ? (int?)p.NewLines.Length : null,
                LinesRemoved = p.IsNoOp || p.NewLines != null ? null : (int?)p.RealCount
            }).ToList();
        }

        private static ReplaceLinesBatchResponse BatchError(string message)
        {
            return new ReplaceLinesBatchResponse { Success = false, ErrorMessage = message };
        }

        /// <summary>One edit of the batch could not be placed: the whole call writes nothing.</summary>
        private static ReplaceLinesBatchResponse BatchMatchFailure(string filePath, EditRequest request, ReplaceLinesResponse editError, int fileLineCount)
        {
            string message = $"{request.Label} (start_line {request.StartLine}): {editError.ErrorMessage}";

            long requestedEnd = (long)request.StartLine - 1 + LineSplitter.Split(request.OldLines).Length;
            if (requestedEnd > fileLineCount)
            {
                message += $" In '{EditsParameter}' every edit must lie within the file ({fileLineCount} line(s)); " +
                           $"to add lines past the end, send that edit as a separate call without '{EditsParameter}'.";
            }

            message += " Nothing was written: fix this edit and resend all edits.";

            return new ReplaceLinesBatchResponse
            {
                Success = false,
                FilePath = filePath,
                ErrorMessage = message,
                FailedEdit = request.Label,
                Candidates = editError.Candidates,
                OldLinesJsonEscaped = editError.OldLinesJsonEscaped,
                IsMatchFailure = editError.IsMatchFailure
            };
        }

        /// <summary>
        /// Resolves where the block really starts. When a relaxed or canonical pass finds several blocks and one of them starts
        /// at start_line, that one is taken and the others are returned in <c>otherMatches</c>; otherwise <c>otherMatches</c> is null.
        /// </summary>
        private (int resolvedStartLine, bool matchedIgnoringIndent, bool matchedWithNormalization, CanonicalWindowCache canonicalCache, ReplaceLinesResponse errorResponse, List<int> otherMatches)
            ResolveStartLine(List<string> linesList, string[] oldLinesArray, int startLine, string filePath)
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
                return (startLine, false, false, null, null, null);

            var blockMatches = FindBlock(linesList, oldLinesArray, startLine);

            if (blockMatches.Count == 1)
                return (blockMatches[0], false, false, null, null, null);

            if (blockMatches.Count > 1)
            {
                return (0, false, false, null, MatchFailure(
                    filePath,
                    $"old_lines matches {blockMatches.Count} locations. Use a larger old_lines block to disambiguate, or re-read the file.",
                    ToCandidates(linesList, blockMatches, oldLinesCount)), null);
            }

            var relaxedMatches = FindBlock(linesList, oldLinesArray, startLine, LineComparison.StrictIgnoreLeadingIndent);

            if (relaxedMatches.Count == 1)
                return (relaxedMatches[0], true, false, null, null, null);

            if (relaxedMatches.Count > 1)
            {
                var others = OthersIfOneIsAtStartLine(relaxedMatches, startLine);
                if (others != null)
                    return (startLine, true, false, null, null, others);

                return (0, false, false, null, MatchFailure(
                    filePath,
                    $"old_lines matches {relaxedMatches.Count} locations when leading indentation of the first line is ignored. Use a larger old_lines block to disambiguate, or re-read the file.",
                    ToCandidates(linesList, relaxedMatches, oldLinesCount)), null);
            }

            var cache = new CanonicalWindowCache(linesList, startLine, oldLinesCount);
            string[] canonicalOldTrimmed = Array.ConvertAll(oldLinesArray, s => CharacterMap.Canonicalize(s).TrimEnd());

            var canonicalMatches = FindBlock(
                linesList, oldLinesArray, startLine, LineComparison.Canonical, cache, canonicalOldTrimmed);

            if (canonicalMatches.Count == 1)
                return (canonicalMatches[0], false, true, cache, null, null);

            if (canonicalMatches.Count > 1)
            {
                var others = OthersIfOneIsAtStartLine(canonicalMatches, startLine);
                if (others != null)
                    return (startLine, false, true, cache, null, others);

                return (0, false, false, null, MatchFailure(
                    filePath,
                    $"old_lines matches {canonicalMatches.Count} locations after character normalization. Use a larger old_lines block to disambiguate, or re-read the file.",
                    ToCandidates(linesList, canonicalMatches, oldLinesCount)), null);
            }

            var canonicalRelaxed = FindBlock(
                linesList, oldLinesArray, startLine, LineComparison.CanonicalIgnoreLeadingIndent, cache, canonicalOldTrimmed);

            if (canonicalRelaxed.Count == 1)
                return (canonicalRelaxed[0], true, true, cache, null, null);

            if (canonicalRelaxed.Count > 1)
            {
                var others = OthersIfOneIsAtStartLine(canonicalRelaxed, startLine);
                if (others != null)
                    return (startLine, true, true, cache, null, others);

                return (0, false, false, null, MatchFailure(
                    filePath,
                    $"old_lines matches {canonicalRelaxed.Count} locations after character normalization when leading indentation of the first line is ignored. Use a larger old_lines block to disambiguate, or re-read the file.",
                    ToCandidates(linesList, canonicalRelaxed, oldLinesCount)), null);
            }

            var firstLineMatches = LineMatcher.FindMatches(linesList, oldLinesArray[0], startLine);
            string normalizationNote = "";
            if (firstLineMatches.Count == 0)
            {
                firstLineMatches = LineMatcher.FindMatchesCanonical(linesList, oldLinesArray[0], startLine);
                if (firstLineMatches.Count > 0)
                    normalizationNote = " after character normalization";
            }

            if (firstLineMatches.Count > 0)
            {
                var (best, mismatch) = LineMatcher.DescribeClosestMismatch(linesList, oldLinesArray, firstLineMatches, startLine);
                var ordered = new List<int> { best };
                ordered.AddRange(firstLineMatches.Where(p => p != best));
                var partial = MatchFailure(
                    filePath,
                    $"First line of old_lines found at {firstLineMatches.Count} location(s){normalizationNote}, but the full block didn't match. " +
                    (mismatch != null ? mismatch + " " : "") +
                    "Re-read the file or retry with the suggested start_line.",
                    ToCandidates(linesList, ordered, oldLinesCount));
                partial.IsNotFound = true;
                return (0, false, false, null, partial, null);
            }

            var notFound = MatchFailure(
                filePath,
                "Old content not found in file. Check leading whitespace and indentation, copy old_lines verbatim from read_file_lines, or re-read the file.");
            notFound.IsNotFound = true;
            return (0, false, false, null, notFound, null);
        }

        /// <summary>
        /// Diagnoses the known "JSON escape confusion" failure: old_lines was passed with one extra level of JSON string escaping (e.g. <c>\"</c> for <c>"</c>), so it does not match the file. 
        /// </summary>
        private ReplaceLinesResponse DiagnoseJsonEscapedOldLines(
            ReplaceLinesResponse notFound, string[] fileLines, string oldLinesText, int startLine, string filePath, string absolutePath)
        {
            if (notFound == null || !notFound.IsNotFound)
                return null;
            if (!JsonEscapes.TryUnescape(oldLinesText, out string unescaped))
                return null;

            string[] unescapedLines = LineSplitter.Split(unescaped);
            long requiredTotalLines = (long)(startLine - 1) + unescapedLines.Length;
            if (requiredTotalLines - fileLines.Length > LineMatcher.MaxPaddingLines)
                return null;

            var probeLines = new List<string>(fileLines);
            while (probeLines.Count < requiredTotalLines)
                probeLines.Add("");

            var (foundLine, _, _, _, probeError, _) = ResolveStartLine(probeLines, unescapedLines, startLine, filePath);
            if (probeError != null)
                return null;

            InternalLogger.Warn(
                $"replace_file_lines: old_lines for '{absolutePath}' looks JSON-escaped; it matches line {foundLine} only after unescaping. Nothing written.");

            var escaped = MatchFailure(
                filePath,
                $"old_lines looks JSON-escaped: it matches the file at line {foundLine} only after removing one level of escaping " +
                "(\\\" -> \", \\\\ -> \\, \\n -> newline). Nothing was written. Resend old_lines AND new_lines as raw source text " +
                "without JSON escaping; the candidate below is the real file text at that location.",
                ToCandidates(probeLines, new List<int> { foundLine }, unescapedLines.Length));
            escaped.OldLinesJsonEscaped = true;
            return escaped;
        }

        /// <summary>
        /// Comparison mode of <see cref="FindBlock"/>.
        /// </summary>
        private enum LineComparison
        {
            Strict,
            StrictIgnoreLeadingIndent,
            Canonical,
            CanonicalIgnoreLeadingIndent
        }

        /// <summary>
        /// Searches for a full block match within ±SearchWindow of aroundLine.
        /// </summary>
        private static List<int> FindBlock(
            List<string> lines, string[] block, int aroundLine,
            LineComparison mode = LineComparison.Strict,
            CanonicalWindowCache cache = null,
            string[] canonicalBlockTrimmed = null)
        {
            Debug.Assert(
                mode == LineComparison.Strict
                || mode == LineComparison.StrictIgnoreLeadingIndent
                || (cache != null && canonicalBlockTrimmed != null),
                "Canonical comparison requires a window cache and the precomputed canonical forms of the block.");

            int windowLines = LineMatcher.MaxSearchWindowLines;
            int lower = Math.Max(0, aroundLine - 1 - windowLines);
            int upper = Math.Min(lines.Count - block.Length, aroundLine - 1 + windowLines);
            var result = new List<int>();
            for (int i = lower; i <= upper; i++)
            {
                bool match = true;
                for (int j = 0; j < block.Length; j++)
                {
                    bool lineMatches;
                    if (mode == LineComparison.Canonical)
                    {
                        lineMatches = CanonicalTrimmed(lines, cache, i + j) == canonicalBlockTrimmed[j];
                    }
                    else if (mode == LineComparison.CanonicalIgnoreLeadingIndent)
                    {
                        lineMatches = j == 0
                            ? CanonicalTrimmed(lines, cache, i + j).TrimStart() == canonicalBlockTrimmed[j].TrimStart()
                            : CanonicalTrimmed(lines, cache, i + j) == canonicalBlockTrimmed[j];
                    }
                    else if (mode == LineComparison.StrictIgnoreLeadingIndent && j == 0)
                    {
                        lineMatches = LineMatcher.LinesEqualIgnoringLeadingWhitespace(lines[i + j], block[j]);
                    }
                    else
                    {
                        lineMatches = LineMatcher.LinesEqual(lines[i + j], block[j]);
                    }

                    if (!lineMatches)
                    {
                        match = false; break;
                    }
                }
                if (match) result.Add(i + 1);
            }
            return result;
        }

        /// <summary>
        /// Canonical form of a file line with trailing whitespace removed — the canonical analogue of the TrimEnd that <see cref="LineMatcher.LinesEqual"/> performs, so the canonical pass cannot be stricter than strict.
        /// </summary>
        private static string CanonicalTrimmed(List<string> lines, CanonicalWindowCache cache, int lineIndex)
        {
            string exact = cache != null
                ? cache.GetExact(lineIndex)
                : CharacterMap.Canonicalize(lines[lineIndex]);

            return exact?.TrimEnd();
        }

        /// <summary>
        /// Restore policy of a canonical match.
        /// </summary>
        private static void RestoreUnchangedLines(
            List<string> linesList, string[] newLinesAsReceived, string[] newLinesArray, string[] oldLinesArray,
            int removeStart, int realCount, bool matchedIgnoringIndent, CanonicalWindowCache cache)
        {
            int newCount = newLinesArray.Length;
            if (realCount <= 0 || newCount == 0)
                return;

            Debug.Assert(cache != null, "Restore requires the canonical window cache built by the canonical pass.");

            int paired = Math.Min(realCount, newCount);
            var canonicalNew = new string[newCount];

            int prefix = 0;
            while (prefix < paired)
            {
                if (!ShouldRestore(newLinesAsReceived, newLinesArray, canonicalNew, oldLinesArray,
                        removeStart, prefix, prefix, matchedIgnoringIndent, cache))
                    break;

                newLinesArray[prefix] = linesList[removeStart + prefix];
                prefix++;
            }

            int maxSuffix = paired - prefix;
            int suffix = 0;
            while (suffix < maxSuffix)
            {
                int oldIdx = realCount - 1 - suffix;
                int newIdx = newCount - 1 - suffix;

                if (!ShouldRestore(newLinesAsReceived, newLinesArray, canonicalNew, oldLinesArray,
                        removeStart, oldIdx, newIdx, matchedIgnoringIndent, cache))
                    break;

                newLinesArray[newIdx] = linesList[removeStart + oldIdx];
                suffix++;
            }
        }

        private static bool ShouldRestore(
            string[] newLinesAsReceived, string[] newLinesArray, string[] canonicalNew, string[] oldLinesArray,
            int removeStart, int oldIdx, int newIdx, bool matchedIgnoringIndent, CanonicalWindowCache cache)
        {
            if (ModelDidNotChange(newLinesAsReceived, oldLinesArray, oldIdx, newIdx, matchedIgnoringIndent))
                return true;

            if (oldIdx != newIdx)
                return false;

            string fileCanonical = cache.GetExact(removeStart + oldIdx);
            if (fileCanonical == null)
                return false;

            if (canonicalNew[newIdx] == null)
                canonicalNew[newIdx] = CharacterMap.Canonicalize(newLinesArray[newIdx]);

            return string.Equals(fileCanonical, canonicalNew[newIdx], StringComparison.Ordinal);
        }

        /// <summary>
        /// First disjunct: the model did not change this line in its own output (<c>new[newIdx]</c> is ordinally equal to <c>old[oldIdx]</c>).
        /// </summary>
        private static bool ModelDidNotChange(
            string[] newLinesAsReceived, string[] oldLinesArray, int oldIdx, int newIdx, bool matchedIgnoringIndent)
        {
            string received = newLinesAsReceived[newIdx];
            string old = oldLinesArray[oldIdx];

            if (oldIdx == 0 && newIdx == 0 && matchedIgnoringIndent)
                return string.Equals(StripLeading(received), StripLeading(old), StringComparison.Ordinal);

            return string.Equals(received, old, StringComparison.Ordinal);
        }

        private static List<LineCandidate> ToCandidates(
            List<string> linesList, List<int> positions, int blockLength = 1)
        {
            return LineMatcher.BuildCandidates(linesList, positions, blockLength);
        }

        /// <summary>The matches other than start_line when start_line is one of them, otherwise null.</summary>
        private static List<int> OthersIfOneIsAtStartLine(List<int> matches, int startLine)
            => matches.Contains(startLine) ? matches.Where(m => m != startLine).ToList() : null;

        /// <summary>
        /// Note for an edit taken at start_line among several matching blocks: the other matches that no edit in <paramref name="otherEdits"/> covers, or null when none is left.
        /// </summary>
        private static string OtherMatchesNote(PreparedEdit edit, IEnumerable<PreparedEdit> otherEdits)
        {
            if (edit.OtherMatches == null)
                return null;

            var left = edit.OtherMatches
                .Where(m => !otherEdits.Any(o => m >= o.AppliedStartLine && m < o.AppliedStartLine + o.OldCount))
                .ToList();
            if (left.Count == 0)
                return null;

            string how = edit.MatchedIgnoringIndent && edit.MatchedWithNormalization
                ? "after character normalization, first-line indentation ignored"
                : edit.MatchedIgnoringIndent ? "first-line indentation ignored" : "after character normalization";
            return $"old_lines also matches line(s) {string.Join(", ", left)} ({how}); start_line {edit.AppliedStartLine} was used, the others were left unchanged.";
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

        private static bool TryParseInt(object value, out int result) => int.TryParse(value?.ToString(), out result);

        /// <summary>
        /// Ordinal comparison of the file range the call would rewrite with the lines the model sent.
        /// </summary>
        private static bool RangeEquals(string[] fileLines, int removeStart, string[] newLinesArray)
        {
            if (removeStart < 0 || removeStart + newLinesArray.Length > fileLines.Length)
                return false;

            for (int i = 0; i < newLinesArray.Length; i++)
            {
                if (!string.Equals(fileLines[removeStart + i], newLinesArray[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Response of a call whose range already holds exactly the requested text: success with <c>lines_replaced: 0</c>, no snapshot, no write and no syntax check.
        /// </summary>
        private static ReplaceLinesResponse NoOp(
            string relativePath, string absolutePath, bool autoCorrected,
            int originalStartLine, int appliedStartLine,
            bool matchedIgnoringIndent, bool matchedWithNormalization)
        {
            var response = new ReplaceLinesResponse
            {
                Success = true,
                FilePath = relativePath ?? absolutePath,
                LinesReplaced = 0,
                IsNoOp = true
            };

            if (autoCorrected)
            {
                response.AutoCorrected = true;
                response.OriginalStartLine = originalStartLine;
                response.AppliedStartLine = appliedStartLine;
            }

            if (matchedIgnoringIndent)
                response.MatchedIgnoringFirstLineIndent = true;

            if (matchedWithNormalization)
                response.MatchedWithNormalization = true;

            return response;
        }

        /// <summary>
        /// Returns the leading run of a line: whitespace AND characters that will vanish during canonicalization (ZWSP, U+FEFF).
        /// </summary>
        private static string GetLeadingRun(string line)
        {
            return line.Substring(0, LeadingRunLength(line));
        }

        /// <summary>
        /// Removes the leading run (whitespace and ZWSP/U+FEFF) of a line.
        /// </summary>
        private static string StripLeading(string line)
        {
            return line.Substring(LeadingRunLength(line));
        }

        private static int LeadingRunLength(string line)
        {
            int i = 0;
            while (i < line.Length && (char.IsWhiteSpace(line[i]) || CharacterMap.IsDeleted(line[i])))
                i++;
            return i;
        }


        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var filePath = parameters?.TryGetValue("file_path", out var f) == true ? f?.ToString() : "";

            object editsObj = null;
            parameters?.TryGetValue(EditsParameter, out editsObj);
            if (!LiteralListParameter.IsMissing(editsObj))
            {
                var (_, edits, _, error) = ExtractEdits(parameters, editsObj);
                return error == null
                    ? $"Replacing {edits.Count} {Pluralizer.Pluralize(edits.Count, "range", "ranges")} in '{filePath}'... "
                    : $"Replacing lines in '{filePath}'... ";
            }

            var start = parameters?.TryGetValue("start_line", out var s) == true ? s?.ToString() : "?";
            return $"Replacing lines starting at line {start} in '{filePath}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is ReplaceLinesBatchResponse batch)
                return DescribeBatch(batch);

            if (result is ReplaceLinesResponse response)
            {
                if (!response.Success)
                {
                    if (response.OldLinesJsonEscaped == true)
                        return "Lines not replaced: old_lines looks JSON-escaped.";
                    return response.IsMatchFailure
                        ? "Lines not replaced: old_lines did not match the file."
                        : "Replacing lines failed.";
                }

                if (response.IsNoOp)
                    return "No changes needed.";

                string msg = DescribeLineCount(response);
                var notes = new List<string>();

                if (response.AutoCorrected == true)
                    notes.Add($"auto-corrected from line {response.OriginalStartLine} to {response.AppliedStartLine}");
                if (response.MatchedIgnoringFirstLineIndent == true)
                    notes.Add("matched ignoring first-line indentation");
                if (response.MatchedWithNormalization == true)
                    notes.Add("matched after character normalization");
                if (response.InvisibleCharacters != null)
                    notes.Add("invisible characters on " + response.InvisibleCharacters.Length + " " +
                        Pluralizer.Pluralize(response.InvisibleCharacters.Length, "line", "lines"));
                if (notes.Count > 0)
                    msg += " (" + string.Join(", ", notes) + ")";
                if (response.SyntaxErrors != null && response.SyntaxErrors.Length > 0)
                    msg += $" with {response.SyntaxErrors.Length} syntax {Pluralizer.Pluralize(response.SyntaxErrors.Length, "error", "errors")}";
                msg += ".";
                return msg;
            }
            return "Replacing lines finished.";
        }

        private static string DescribeBatch(ReplaceLinesBatchResponse batch)
        {
            if (!batch.Success)
            {
                if (batch.OldLinesJsonEscaped == true)
                    return $"Edits not applied: old_lines of {batch.FailedEdit} looks JSON-escaped.";
                if (batch.IsMatchFailure)
                    return $"Edits not applied: old_lines of {batch.FailedEdit} did not match the file.";
                return $"Edits not applied: {batch.ErrorMessage}";
            }

            if (batch.IsNoOp)
                return "No changes needed.";

            var edits = batch.Edits ?? new List<ReplaceEditResult>();
            int applied = edits.Count(e => e.NoOp != true);
            string msg = $"{applied} {Pluralizer.Pluralize(applied, "edit", "edits")} applied";

            var notes = new List<string>();
            int corrected = edits.Count(e => e.AutoCorrected == true);
            if (corrected > 0)
                notes.Add($"{corrected} auto-corrected");
            int unchanged = edits.Count - applied;
            if (unchanged > 0)
                notes.Add($"{unchanged} already up to date");
            if (notes.Count > 0)
                msg += " (" + string.Join(", ", notes) + ")";
            if (batch.SyntaxErrors != null && batch.SyntaxErrors.Length > 0)
                msg += $" with {batch.SyntaxErrors.Length} syntax {Pluralizer.Pluralize(batch.SyntaxErrors.Length, "error", "errors")}";
            return msg + ".";
        }

        /// <summary>
        /// Builds the leading part of the completion message.
        /// </summary>
        private static string DescribeLineCount(ReplaceLinesResponse response)
        {
            if (response.LinesRemoved.HasValue)
            {
                int removed = response.LinesRemoved.Value;
                return $"{removed} {Pluralizer.Pluralize(removed, "line", "lines")} removed";
            }

            int replaced = response.LinesReplaced;
            return $"{replaced} {Pluralizer.Pluralize(replaced, "line", "lines")} replaced";
        }

        private static ReplaceLinesResponse Error(string message)
        {
            return new ReplaceLinesResponse { Success = false, ErrorMessage = message };
        }

        /// <summary>
        /// old_lines could not be placed.
        /// </summary>
        private static ReplaceLinesResponse MatchFailure(string filePath, string message, List<LineCandidate> candidates = null)
        {
            return new ReplaceLinesResponse
            {
                Success = false,
                FilePath = filePath,
                ErrorMessage = message,
                Candidates = candidates,
                IsMatchFailure = true
            };
        }
    }

    internal class ReplaceLinesResponse : IToolFileOpenResult
    {
        string IToolFileOpenResult.FileToOpen => FilePath;

        bool IToolFileOpenResult.WroteFile => Success && !IsNoOp;

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Number of lines written by the call (<c>new_lines</c> length).
        /// </summary>
        [JsonProperty("lines_replaced")]
        public int LinesReplaced { get; set; }

        /// <summary>
        /// Number of lines removed by a range deletion (<c>new_lines</c> empty).
        /// </summary>
        [JsonProperty("lines_removed", NullValueHandling = NullValueHandling.Ignore)]
        public int? LinesRemoved { get; set; }

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

        [JsonProperty("matched_with_normalization", NullValueHandling = NullValueHandling.Ignore)]
        public bool? MatchedWithNormalization { get; set; }

        /// <summary>
        /// Invisible characters on the matched lines that old_lines did not carry, per line: kept or removed.
        /// </summary>
        [JsonProperty("invisible_characters", NullValueHandling = NullValueHandling.Ignore)]
        public string[] InvisibleCharacters { get; set; }

        /// <summary>
        /// Set when old_lines did not match but matched exactly one location after removing one level of JSON escaping.
        /// </summary>
        [JsonProperty("old_lines_json_escaped", NullValueHandling = NullValueHandling.Ignore)]
        public bool? OldLinesJsonEscaped { get; set; }

        /// <summary>Set when old_lines matched several blocks and the one at start_line was used: lists the others, left unchanged.</summary>
        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note { get; set; }

        /// <summary>
        /// Set when the call would have written exactly the text the file already holds.
        /// </summary>
        [JsonIgnore]
        internal bool IsNoOp { get; set; }

        /// <summary>
        /// Set on the final "not found" outcomes of matching (as opposed to ambiguity or validation errors).
        /// </summary>
        [JsonIgnore]
        internal bool IsNotFound { get; set; }

        /// <summary>
        /// Set when the call failed only because old_lines could not be placed in the file (the tool itself worked); used for the UI message.
        /// </summary>
        [JsonIgnore]
        internal bool IsMatchFailure { get; set; }

        [JsonProperty("candidates", NullValueHandling = NullValueHandling.Ignore)]
        public List<LineCandidate> Candidates { get; set; }
    }

    /// <summary>
    /// Response of a replace_file_lines call with 'edits': one result per edit in request order, or the edit that
    /// stopped the call (then nothing was written).
    /// </summary>
    internal class ReplaceLinesBatchResponse : IToolFileOpenResult
    {
        string IToolFileOpenResult.FileToOpen => FilePath;

        bool IToolFileOpenResult.WroteFile => Success && !IsNoOp;

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("file_path", NullValueHandling = NullValueHandling.Ignore)]
        public string FilePath { get; set; }

        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }

        /// <summary>The edit that did not match ("edits[2]" or "the top-level edit").</summary>
        [JsonProperty("failed_edit", NullValueHandling = NullValueHandling.Ignore)]
        public string FailedEdit { get; set; }

        [JsonProperty("edits", NullValueHandling = NullValueHandling.Ignore)]
        public List<ReplaceEditResult> Edits { get; set; }

        /// <summary>What the call ignored: placeholder top-level fields or repeated edits.</summary>
        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note { get; set; }

        [JsonProperty("syntax_errors", NullValueHandling = NullValueHandling.Ignore)]
        public string[] SyntaxErrors { get; set; }

        [JsonProperty("old_lines_json_escaped", NullValueHandling = NullValueHandling.Ignore)]
        public bool? OldLinesJsonEscaped { get; set; }

        [JsonProperty("candidates", NullValueHandling = NullValueHandling.Ignore)]
        public List<LineCandidate> Candidates { get; set; }

        /// <summary>Set when every edit already held the requested text: nothing was written.</summary>
        [JsonIgnore]
        internal bool IsNoOp { get; set; }

        /// <summary>Set when the call failed only because old_lines of one edit could not be placed; used for the UI message.</summary>
        [JsonIgnore]
        internal bool IsMatchFailure { get; set; }
    }

    /// <summary>Result of one edit of a batch, with the same flags as a single call.</summary>
    internal class ReplaceEditResult
    {
        /// <summary>start_line as sent.</summary>
        [JsonProperty("start_line")]
        public int StartLine { get; set; }

        [JsonProperty("applied_start_line", NullValueHandling = NullValueHandling.Ignore)]
        public int? AppliedStartLine { get; set; }

        [JsonProperty("auto_corrected", NullValueHandling = NullValueHandling.Ignore)]
        public bool? AutoCorrected { get; set; }

        [JsonProperty("matched_ignoring_first_line_indent", NullValueHandling = NullValueHandling.Ignore)]
        public bool? MatchedIgnoringFirstLineIndent { get; set; }

        [JsonProperty("matched_with_normalization", NullValueHandling = NullValueHandling.Ignore)]
        public bool? MatchedWithNormalization { get; set; }

        [JsonProperty("invisible_characters", NullValueHandling = NullValueHandling.Ignore)]
        public string[] InvisibleCharacters { get; set; }

        /// <summary>Set when old_lines matched several blocks and the one at start_line was used: lists the others no edit of this call covers.</summary>
        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note { get; set; }

        [JsonProperty("lines_replaced", NullValueHandling = NullValueHandling.Ignore)]
        public int? LinesReplaced { get; set; }

        [JsonProperty("lines_removed", NullValueHandling = NullValueHandling.Ignore)]
        public int? LinesRemoved { get; set; }

        /// <summary>The range already held the requested text; this edit wrote nothing.</summary>
        [JsonProperty("no_op", NullValueHandling = NullValueHandling.Ignore)]
        public bool? NoOp { get; set; }
    }
}
