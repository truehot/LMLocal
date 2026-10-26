using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Changes.Models;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Threading;
using static LMLocal.Core.Common.Pluralizer;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    internal interface IGetChangesDiff : IBuiltInTool
    {
    }

    /// <summary>
    /// Read-only tool returning the diff of the pending (uncommitted) file changes made through the built-in write tools.
    /// </summary>
    internal class GetChangesDiff : IGetChangesDiff
    {
        internal const int DefaultMaxFiles = 50;
        internal const int MaxMaxFiles = 100;

        internal const int DefaultMaxLines = 2000;
        internal const int MinMaxLines = 100;
        internal const int MaxMaxLines = 20000;

        /// <summary>Largest number of entries accepted in the "files" filter.</summary>
        internal const int MaxFilesFilterEntries = 200;

        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IChangesReader _changesReader;

        public string ToolName => "get_changes_diff";

        public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

        public GetChangesDiff(IVsDependencies vsDependencies, IPathResolver pathResolver, IChangesReader changesReader)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _changesReader = changesReader ?? throw new ArgumentNullException(nameof(changesReader));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description =
                    "Returns a read-only summary and diff of the pending file changes recorded by the built-in write tools " +
                    "in the current solution (uncommitted, live snapshots). Read-only: NOT gated by the write-tools switch — " +
                    "available whenever a solution is open, even when write tools are disabled; changes accumulated earlier " +
                    "are still returned.\n\n" +
                    "By default this returns ONLY the list of changed files with per-file added/removed counts — no diff bodies. " +
                    "Pass include_diff: true to get the actual unified diff hunks. That two-step use is the intended cheap path: " +
                    "call once with no parameters to see what changed, then call again with files: [...] and include_diff: true " +
                    "to read only the files you need.\n\n" +
                    "Parameters:\n" +
                    "- include_diff (optional bool, default false): when false the \"hunks\" key is omitted entirely and only file " +
                    "metadata is returned. When true each file carries its unified diff hunks, capped by max_lines.\n" +
                    "- files (optional array of strings, default empty): restricts the response to these paths (relative to the " +
                    "solution or absolute). Omitted, null, or [] all mean \"every changed file\". Paths without pending changes are " +
                    "ignored. If none of them match, the result is success: true with total_files: 0 and files: [] — that means " +
                    "those files have no pending changes, not that the tool failed. Matching is exact-path (no wildcards, no " +
                    "substring/fuzzy matching); path separators and letter case are normalized. Maximum 200 entries.\n" +
                    "- max_files (optional int, default 50, 1..100): cap on the number of files in the response.\n" +
                    "- max_lines (optional int, default 2000, 100..20000): cap on the total diff lines across all response files " +
                    "(token guard). Ignored when include_diff is false.\n\n" +
                    "Response (include_diff: true):\n" +
                    "{ \"success\": true, \"total_files\": 1, \"total_added\": 12, \"total_removed\": 3, \"has_more_results\": false,\n" +
                    "  \"files\": [ { \"file_path\": \"src/Foo.cs\", \"status\": \"modified|created|deleted\", \"added\": 12, " +
                    "\"removed\": 3, \"hunks\": [ \"@@ -14,6 +14,9 @@\\n         public void Foo()\\n+            // new line\\n" +
                    "-            // old line\\n\" ] } ] }\n" +
                    "With include_diff: false the shape is identical but the \"hunks\" key is absent.\n\n" +
                    "Notes:\n" +
                    "- Each element of \"hunks\" is one whole unified diff hunk: the first line is the \"@@\" header, then one line " +
                    "per source line. The FIRST CHARACTER is the marker: ' ' = context, '+' = added, '-' = removed; everything " +
                    "after it is the line text verbatim (indentation preserved, never trimmed). A blank context line is a single " +
                    "space. Line numbers are recoverable from the \"@@ -a,b +c,d @@\" header plus the line order; for exact current " +
                    "line numbers before an edit call read_file_lines.\n" +
                    "- Empty change set: success: true, total_files: 0, files: [] — that means \"no pending changes\".\n" +
                    "- If a file's pre-change backup is missing or unreadable, that file is skipped (not in \"files\"); the call " +
                    "still succeeds. This is not a cap, so it does not set has_more_results.\n" +
                    "- has_more_results: true means the response was capped. There is no page token — increase max_files/max_lines " +
                    "(upper bounds 100 / 20000) to see more, or narrow the call with \"files\". Hunks are cut at line boundaries " +
                    "only: a hunk is included whole or not at all, and with include_diff: true a file whose diff does not fit the " +
                    "budget is omitted. With include_diff: false only max_files can cap the response.\n" +
                    "- total_files counts only the files that actually have pending changes and, when \"files\" is given, are a " +
                    "subset of the requested paths. \"files\" is a filter and never adds files, so requested paths without pending " +
                    "changes contribute nothing to total_files/total_added/total_removed. The caps are applied after the totals, " +
                    "so has_more_results: false guarantees files.length == total_files.\n" +
                    "- The tool never edits files. Paths are returned relative to the solution. If a solution is not open, success " +
                    "is false and error_message is set.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        {
                            "include_diff", new ToolDetails
                            {
                                Type = "boolean",
                                Description = "When true, include the unified diff hunks of every returned file. Default false: only " +
                                              "file metadata (file_path, status, added, removed) is returned and the \"hunks\" key is absent."
                            }
                        },
                        {
                            "files", new ToolDetails
                            {
                                Type = "array",
                                Description = "Restrict the response to these paths (relative to the solution or absolute). Omitted, " +
                                              "null or [] means every changed file. Paths without pending changes are ignored. " +
                                              "Maximum 200 entries.",
                                Items = new ToolDetails { Type = "string" }
                            }
                        },
                        {
                            "max_files", new ToolDetails
                            {
                                Type = "integer",
                                Description = "Maximum number of files in the response. Default 50, maximum 100."
                            }
                        },
                        {
                            "max_lines", new ToolDetails
                            {
                                Type = "integer",
                                Description = "Maximum total diff lines across the response (hunk headers included). Default 2000, " +
                                              "minimum 100, maximum 20000. Ignored when include_diff is false."
                            }
                        }
                    }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            var (includeDiff, requestedPaths, maxFiles, maxLines, error) = ExtractAndValidateParameters(parameters);
            if (error != null)
                return Failure(error);

            if (!_vsDependencies.IsSolutionOpen)
                return Failure("No solution is currently open.");

            try
            {
                List<string> filter = null;

                if (requestedPaths != null && requestedPaths.Count > 0)
                {
                    filter = ResolveRequestedPaths(requestedPaths, _vsDependencies.GetSolutionDirectory());

                    if (filter.Count == 0)
                    {
                        return new ChangesResult
                        {
                            Success = true,
                            TotalFiles = 0,
                            TotalAdded = 0,
                            TotalRemoved = 0,
                            Files = new List<ChangesFile>(),
                            HasMoreResults = false,
                            Outcome = ChangesOutcome.NoPendingChanges
                        };
                    }
                }

                await TaskScheduler.Default;

                return await _changesReader
                    .GetChangesAsync(filter, includeDiff, maxFiles, maxLines, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Failure("Operation was cancelled.");
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"get_changes_diff: unexpected error: {ex.Message}");
                return Failure(ex.Message);
            }
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            bool includeDiff = parameters != null
                && parameters.TryGetValue("include_diff", out object value)
                && value is bool flag
                && flag;

            return includeDiff ? "Collecting pending changes with diff... " : "Collecting pending changes... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is ChangesResult changes)
            {
                if (!changes.Success)
                    return $"Collecting changes failed: {changes.ErrorMessage}";

                switch (changes.Outcome)
                {
                    case ChangesOutcome.NoPendingChanges:
                        return "No pending file changes.";
                    case ChangesOutcome.SnapshotHistoryUnavailable:
                        return "Pending changes exist, but their pre-change snapshots are unavailable, so no diff could be built.";
                    case ChangesOutcome.Success:
                        int returned = changes.Files?.Count ?? 0;
                        string message = $"{returned} of {changes.TotalFiles} " +
                                         $"{Pluralize(changes.TotalFiles, "changed file", "changed files")} " +
                                         $"(+{changes.TotalAdded} / -{changes.TotalRemoved}).";
                        if (changes.HasMoreResults)
                            message += " The response was truncated by the configured limits.";

                        return message;
                    default:
                        return "Changes collected.";
                }
            }

            return "Changes collected.";
        }

        /// <summary>
        /// Normalizes the requested paths to solution-relative form and drops the ones that resolve outside the solution.
        /// </summary>
        private List<string> ResolveRequestedPaths(IEnumerable<string> requestedPaths, string solutionDir)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string raw in requestedPaths)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                string candidate = raw.Trim();
                string relative;

                if (Path.IsPathRooted(candidate))
                {
                    if (string.IsNullOrEmpty(solutionDir) || !_pathResolver.IsPathInsideDirectory(candidate, solutionDir))
                    {
                        InternalLogger.Debug($"get_changes_diff: ignoring '{candidate}', it is outside the solution.");
                        continue;
                    }

                    if (!_pathResolver.TryGetRelativePath(candidate, solutionDir, out relative))
                    {
                        InternalLogger.Debug($"get_changes_diff: ignoring '{candidate}', it cannot be made solution-relative.");
                        continue;
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(solutionDir) &&
                        _pathResolver.TryResolveFilePath(candidate, solutionDir, out string absolute) &&
                        !_pathResolver.IsPathInsideDirectory(absolute, solutionDir))
                    {
                        InternalLogger.Debug($"get_changes_diff: ignoring '{candidate}', it resolves outside the solution.");
                        continue;
                    }

                    relative = candidate;
                }

                string normalized = NormalizeRelative(relative);
                if (normalized == null)
                    continue;

                if (seen.Add(normalized))
                    result.Add(normalized);
            }

            return result;
        }

        /// <summary>
        /// Normalizes a solution-relative path to forward slashes without a leading "./" or "/", so that equivalent spellings of the same path collapse into one filter entry.
        /// </summary>
        private static string NormalizeRelative(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            string normalized = path.Trim().Replace('\\', '/');
            while (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized.Substring(2);
            while (normalized.IndexOf("//", StringComparison.Ordinal) >= 0)
                normalized = normalized.Replace("//", "/");

            normalized = normalized.TrimStart('/');
            return normalized.Length == 0 ? null : normalized;
        }

        private (bool includeDiff, List<string> files, int maxFiles, int maxLines, string error) ExtractAndValidateParameters(
            Dictionary<string, object> parameters)
        {
            bool includeDiff = false;
            List<string> files = null;
            int maxFiles = DefaultMaxFiles;
            int maxLines = DefaultMaxLines;

            if (parameters == null)
                return (includeDiff, files, maxFiles, maxLines, null);

            if (parameters.TryGetValue("include_diff", out object includeObj) && includeObj != null)
            {
                if (!(includeObj is bool includeFlag))
                    return (false, null, DefaultMaxFiles, DefaultMaxLines, "Parameter 'include_diff' must be a boolean (true or false).");

                includeDiff = includeFlag;
            }

            if (parameters.TryGetValue("files", out object filesObj) && filesObj != null)
            {
                if (!TryParseStringArray(filesObj, out files, out string filesError))
                    return (false, null, DefaultMaxFiles, DefaultMaxLines, filesError);

                if (files != null && files.Count > MaxFilesFilterEntries)
                {
                    return (false, null, DefaultMaxFiles, DefaultMaxLines,
                        $"Parameter 'files' must contain at most {MaxFilesFilterEntries} entries.");
                }

                if (files != null && files.Count == 0)
                    files = null;
            }

            if (TryGetInt(parameters, "max_files", out int requestedMaxFiles))
                maxFiles = Math.Min(Math.Max(requestedMaxFiles, 1), MaxMaxFiles);

            if (TryGetInt(parameters, "max_lines", out int requestedMaxLines))
                maxLines = Math.Min(Math.Max(requestedMaxLines, MinMaxLines), MaxMaxLines);

            return (includeDiff, files, maxFiles, maxLines, null);
        }

        private static bool TryParseStringArray(object value, out List<string> result, out string error)
        {
            result = null;
            error = null;

            if (value is string)
            {
                error = "Parameter 'files' must be an array of strings, not a single string.";
                return false;
            }

            if (!(value is IEnumerable enumerable))
            {
                error = "Parameter 'files' must be an array of strings.";
                return false;
            }

            var items = new List<string>();
            foreach (object item in enumerable)
            {
                if (item == null)
                    continue;

                if (!(item is string text))
                {
                    error = "Parameter 'files' must be an array of strings.";
                    return false;
                }

                text = text.Trim();
                if (text.Length > 0)
                    items.Add(text);
            }

            result = items;
            return true;
        }

        private static bool TryGetInt(Dictionary<string, object> parameters, string name, out int value)
        {
            value = 0;
            return parameters.TryGetValue(name, out object raw)
                   && raw != null
                   && int.TryParse(raw.ToString(), out value);
        }

        private static ChangesResult Failure(string message)
        {
            return new ChangesResult
            {
                Success = false,
                ErrorMessage = message,
                TotalFiles = 0,
                TotalAdded = 0,
                TotalRemoved = 0,
                Files = new List<ChangesFile>(),
                HasMoreResults = false,
                Outcome = ChangesOutcome.None
            };
        }
    }
}
