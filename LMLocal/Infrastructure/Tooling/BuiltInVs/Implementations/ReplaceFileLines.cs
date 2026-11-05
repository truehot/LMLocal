using LMLocal.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                Description = "Replaces a range of lines in a file by line numbers (1-indexed). Take start_line and old_lines from a read_file_lines result of the same file: old_lines is the raw 'text' of those lines (see old_lines). The tool verifies old_lines before changing the file. The replaced range ends at start_line + number_of_lines_in_old_lines - 1. " +
                              "Set new_lines to an empty string to delete the range. " +
                              "SEVERAL EDITS IN ONE FILE: send them all in ONE turn as several calls, ordered bottom-up (LARGEST start_line first), all taken from the same read - do not re-read between them. Calls of one turn run in the order sent, and an edit never shifts the lines above it. " +
                              "If start_line is beyond the current line count, the file is padded with empty lines up to start_line - 1 (at most 50; more is rejected with the real line count). If the old_lines range extends past the end of the file, missing trailing lines are treated as empty strings for comparison only. " +
                              "If old_lines does not match at start_line, the tool searches nearby lines (±50); a single match is applied there and reported by a flag: auto_corrected=true (exact match), matched_ignoring_first_line_indent=true (first line's indentation ignored; the first line of new_lines is re-indented to match the file), or matched_with_normalization=true (typographic spaces, quotes, dashes and zero-width characters normalized; unchanged lines are kept from the file). The flags only report how the match was found: the change is written. " +
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
                                Description = "Starting line number (1-indexed, inclusive, positive integer (>= 1)). A start_line more than 50 lines past the end of the file is rejected with the real line count."
                            }
                        },
                        { "old_lines", new ToolDetails
                            {
                                Type = "string",
                                Description = "Raw source text currently occupying the lines from start_line through the end of the block, copied from read_file_lines (not from search results). Edits lower in the file do not change it. No line-number prefix and no trailing newline. Leading whitespace and character casing must match exactly; trailing whitespace and line-ending style (\\r\\n vs \\n) are ignored during comparison. If the requested range extends past the current line count, missing trailing lines are treated as empty strings for comparison. Can contain multiple lines separated by \\n or \\r\\n. Must not be empty."
                            }
                        },
                        { "new_lines", new ToolDetails
                            {
                                Type = "string",
                                Description = "Raw source text to replace the selected lines with. Can contain multiple lines separated by \\n or \\r\\n. If empty string, the line range is deleted."
                            }
                        }
                    },
                    Required = new List<string> { "file_path", "start_line", "old_lines", "new_lines" }
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

                var (resolvedStartLine, matchedIgnoringIndent, matchedWithNormalization, canonicalCache, errorResponse) = ResolveStartLine(linesList, oldLinesArray, startLine, reportedPath);
                if (errorResponse != null)
                    return DiagnoseJsonEscapedOldLines(errorResponse, fileLines, oldLinesText, startLine, reportedPath, absolutePath) ?? errorResponse;

                bool autoCorrected = resolvedStartLine != startLine;
                startLine = resolvedStartLine;

                string matchedFirstLineIndent = matchedIgnoringIndent
                    ? GetLeadingRun(linesList[startLine - 1])
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

                int removeStart = startLine - 1;

                int realCount = Math.Min(oldLinesCount, Math.Max(0, fileLines.Length - removeStart));

                if (matchedWithNormalization && hasNewLines)
                {
                    RestoreUnchangedLines(
                        linesList, newLinesAsReceived, newLinesArray, oldLinesArray,
                        removeStart, realCount, matchedIgnoringIndent, canonicalCache);
                }

                if (hasNewLines
                    && newLinesArray.Length == realCount
                    && RangeEquals(fileLines, removeStart, newLinesArray))
                {
                    InternalLogger.Debug(
                        $"replace_file_lines: no-op on {absolutePath}, lines {startLine}-{startLine + realCount - 1} already hold the requested text; nothing written.");

                    return NoOp(
                        relativePath, absolutePath, autoCorrected, originalStartLine, startLine,
                        matchedIgnoringIndent, matchedWithNormalization);
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

        /// <summary>
        /// Resolves where the block really starts.
        /// </summary>
        private (int resolvedStartLine, bool matchedIgnoringIndent, bool matchedWithNormalization, CanonicalWindowCache canonicalCache, ReplaceLinesResponse errorResponse)
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
                return (startLine, false, false, null, null);

            var blockMatches = FindBlock(linesList, oldLinesArray, startLine);

            if (blockMatches.Count == 1)
                return (blockMatches[0], false, false, null, null);

            if (blockMatches.Count > 1)
            {
                return (0, false, false, null, MatchFailure(
                    filePath,
                    $"old_lines matches {blockMatches.Count} locations. Use a larger old_lines block to disambiguate, or re-read the file.",
                    ToCandidates(linesList, blockMatches, oldLinesCount)));
            }

            var relaxedMatches = FindBlock(linesList, oldLinesArray, startLine, LineComparison.StrictIgnoreLeadingIndent);

            if (relaxedMatches.Count == 1)
                return (relaxedMatches[0], true, false, null, null);

            if (relaxedMatches.Count > 1)
            {
                return (0, false, false, null, MatchFailure(
                    filePath,
                    $"old_lines matches {relaxedMatches.Count} locations when leading indentation of the first line is ignored. Use a larger old_lines block to disambiguate, or re-read the file.",
                    ToCandidates(linesList, relaxedMatches, oldLinesCount)));
            }

            var cache = new CanonicalWindowCache(linesList, startLine, oldLinesCount);
            string[] canonicalOldTrimmed = Array.ConvertAll(oldLinesArray, s => CharacterMap.Canonicalize(s).TrimEnd());

            var canonicalMatches = FindBlock(
                linesList, oldLinesArray, startLine, LineComparison.Canonical, cache, canonicalOldTrimmed);

            if (canonicalMatches.Count == 1)
                return (canonicalMatches[0], false, true, cache, null);

            if (canonicalMatches.Count > 1)
            {
                return (0, false, false, null, MatchFailure(
                    filePath,
                    $"old_lines matches {canonicalMatches.Count} locations after character normalization. Use a larger old_lines block to disambiguate, or re-read the file.",
                    ToCandidates(linesList, canonicalMatches, oldLinesCount)));
            }

            var canonicalRelaxed = FindBlock(
                linesList, oldLinesArray, startLine, LineComparison.CanonicalIgnoreLeadingIndent, cache, canonicalOldTrimmed);

            if (canonicalRelaxed.Count == 1)
                return (canonicalRelaxed[0], true, true, cache, null);

            if (canonicalRelaxed.Count > 1)
            {
                return (0, false, false, null, MatchFailure(
                    filePath,
                    $"old_lines matches {canonicalRelaxed.Count} locations after character normalization when leading indentation of the first line is ignored. Use a larger old_lines block to disambiguate, or re-read the file.",
                    ToCandidates(linesList, canonicalRelaxed, oldLinesCount)));
            }

            var firstLineMatches = LineMatcher.FindMatches(linesList, oldLinesArray[0], startLine);

            if (firstLineMatches.Count > 0)
            {
                var partial = MatchFailure(
                    filePath,
                    $"First line of old_lines found at {firstLineMatches.Count} location(s), but the full block didn't match. Re-read the file or retry with the suggested start_line.",
                    ToCandidates(linesList, firstLineMatches, oldLinesCount));
                partial.IsNotFound = true;
                return (0, false, false, null, partial);
            }

            var notFound = MatchFailure(
                filePath,
                "Old content not found in file. Check leading whitespace and indentation, copy old_lines verbatim from read_file_lines, or re-read the file.");
            notFound.IsNotFound = true;
            return (0, false, false, null, notFound);
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

            var (foundLine, _, _, _, probeError) = ResolveStartLine(probeLines, unescapedLines, startLine, filePath);
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
            var start = parameters?.TryGetValue("start_line", out var s) == true ? s?.ToString() : "?";
            return $"Replacing lines starting at line {start} in '{filePath}'... ";
        }

        public string GetCompletionMessage(object result)
        {
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
                if (notes.Count > 0)
                    msg += " (" + string.Join(", ", notes) + ")";
                if (response.SyntaxErrors != null && response.SyntaxErrors.Length > 0)
                    msg += $" with {response.SyntaxErrors.Length} syntax {Pluralizer.Pluralize(response.SyntaxErrors.Length, "error", "errors")}";
                msg += ".";
                return msg;
            }
            return "Replacing lines finished.";
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
        /// Set when old_lines did not match but matched exactly one location after removing one level of JSON escaping. 
        /// </summary>
        [JsonProperty("old_lines_json_escaped", NullValueHandling = NullValueHandling.Ignore)]
        public bool? OldLinesJsonEscaped { get; set; }

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
}
