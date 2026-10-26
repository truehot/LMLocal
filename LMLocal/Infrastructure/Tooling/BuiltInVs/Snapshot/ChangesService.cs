using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Changes;
using LMLocal.Application.Changes.Models;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot
{
    /// <summary>
    /// Builds the diff of the pending (uncommitted) changes from the live snapshots.
    /// </summary>
    internal sealed class ChangesService : IChangesReader
    {
        /// <summary>Largest file (either side) that is diffed at all.</summary>
        internal const long MaxFileBytes = 2L * 1024 * 1024;

        /// <summary>Largest number of lines (either side) that is diffed at all.</summary>
        internal const int MaxLinesPerFile = 20000;

        private const int ContextLines = UnifiedDiffBuilder.DefaultContextLines;

        private const string StatusCreated = "created";
        private const string StatusDeleted = "deleted";

        private readonly ISnapshotManager _snapshotManager;
        private readonly IFileSystem _fileSystem;

        public ChangesService(ISnapshotManager snapshotManager, IFileSystem fileSystem)
        {
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public async Task<ChangesResult> GetChangesAsync(
            IReadOnlyCollection<string> files,
            bool includeDiff,
            int maxFiles,
            int maxTotalLines,
            CancellationToken cancellationToken = default)
        {
            var changed = await _snapshotManager.GetChangedFilesWithStatusAsync(cancellationToken).ConfigureAwait(false);
            if (changed == null || changed.Count == 0)
                return Empty(ChangesOutcome.NoPendingChanges);

            var selected = SelectChanges(changed, files);
            if (selected.Count == 0)
                return Empty(ChangesOutcome.NoPendingChanges);

            var built = new List<BuiltEntry>(selected.Count);
            int skipped = 0;

            foreach (var change in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();

                BuiltEntry entry;
                try
                {
                    entry = await BuildEntryAsync(change, includeDiff, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (!IsFatal(ex))
                {
                    InternalLogger.Warn(
                        $"get_changes_diff: skipping '{change?.RelativePath}', reading it failed: " +
                        $"{ex.GetType().Name}: {ex.Message}");
                    skipped++;
                    continue;
                }

                if (entry == null)
                {
                    skipped++;
                    continue;
                }

                built.Add(entry);
            }

            var result = new ChangesResult
            {
                Success = true,
                TotalFiles = built.Count,
                TotalAdded = 0,
                TotalRemoved = 0,
                Files = new List<ChangesFile>()
            };

            for (int i = 0; i < built.Count; i++)
            {
                result.TotalAdded += built[i].Added;
                result.TotalRemoved += built[i].Removed;
            }

            bool truncated = false;

            int fileLimit = maxFiles > 0 ? Math.Min(maxFiles, built.Count) : built.Count;
            if (built.Count > fileLimit)
                truncated = true;

            int lineBudget = includeDiff && maxTotalLines > 0 ? maxTotalLines : int.MaxValue;

            for (int i = 0; i < fileLimit; i++)
            {
                BuiltEntry entry = built[i];

                if (includeDiff)
                {
                    if (entry.RenderedLines > lineBudget)
                    {
                        truncated = true;
                        InternalLogger.Debug(
                            $"get_changes_diff: '{entry.FilePath}' omitted, its diff needs {entry.RenderedLines} lines " +
                            $"but only {lineBudget} remain in the max_lines budget.");
                        continue;
                    }

                    lineBudget -= entry.RenderedLines;
                }

                result.Files.Add(new ChangesFile
                {
                    FilePath = entry.FilePath,
                    Status = entry.Status,
                    Added = entry.Added,
                    Removed = entry.Removed,
                    Hunks = entry.Hunks
                });
            }

            result.HasMoreResults = truncated;
            result.Outcome = built.Count > 0
                ? ChangesOutcome.Success
                : ChangesOutcome.SnapshotHistoryUnavailable;

            if (skipped > 0)
            {
                InternalLogger.Info(
                    $"get_changes_diff: {skipped} of {selected.Count} changed file(s) skipped, {built.Count} diffed.");
            }

            if (truncated)
            {
                InternalLogger.Info(
                    $"get_changes_diff: response truncated (total_files={result.TotalFiles}, " +
                    $"returned={result.Files.Count}, has_more_results=true).");
            }

            return result;
        }

        /// <summary>
        /// Applies the optional path filter and returns the selected changes ordered by path.
        /// </summary>
        private static List<SnapshotFileChange> SelectChanges(
            IReadOnlyList<SnapshotFileChange> changed,
            IReadOnlyCollection<string> files)
        {
            var result = new List<SnapshotFileChange>(changed.Count);

            HashSet<string> requested = null;
            if (files != null && files.Count > 0)
            {
                requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    string normalized = NormalizePath(file);
                    if (normalized != null)
                        requested.Add(normalized);
                }
            }

            foreach (var change in changed)
            {
                if (change == null || string.IsNullOrEmpty(change.RelativePath))
                    continue;

                if (requested != null && !requested.Contains(NormalizePath(change.RelativePath)))
                    continue;

                result.Add(change);
            }

            result.Sort((left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
            return result;
        }

        /// <summary>
        /// Normalizes a solution-relative path for comparison.
        /// </summary>
        private static string NormalizePath(string path)
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

        /// <summary>
        /// Reads both sides of one change and diffs them.
        /// </summary>
        private async Task<BuiltEntry> BuildEntryAsync(SnapshotFileChange change, bool includeDiff, CancellationToken cancellationToken)
        {
            string relativePath = change.RelativePath;
            string status = change.Status ?? "modified";

            string[] originalLines;
            string[] modifiedLines;

            if (string.Equals(status, StatusCreated, StringComparison.OrdinalIgnoreCase))
            {
                modifiedLines = await TryReadLinesAsync(
                    _snapshotManager.GetCurrentFilePath(relativePath), relativePath, cancellationToken).ConfigureAwait(false);
                if (modifiedLines == null)
                    return null;

                originalLines = Array.Empty<string>();
            }
            else if (string.Equals(status, StatusDeleted, StringComparison.OrdinalIgnoreCase))
            {
                string backupPath = await _snapshotManager
                    .GetSnapshotFilePathAsync(relativePath, cancellationToken).ConfigureAwait(false);

                originalLines = await TryReadLinesAsync(backupPath, relativePath, cancellationToken).ConfigureAwait(false);
                if (originalLines == null)
                    return null;

                modifiedLines = Array.Empty<string>();
            }
            else
            {
                string backupPath = await _snapshotManager
                    .GetSnapshotFilePathAsync(relativePath, cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrEmpty(backupPath))
                {
                    InternalLogger.Warn(
                        $"get_changes_diff: skipping '{relativePath}', the pre-change snapshot is not available.");
                    return null;
                }

                originalLines = await TryReadLinesAsync(backupPath, relativePath, cancellationToken).ConfigureAwait(false);
                if (originalLines == null)
                    return null;

                modifiedLines = await TryReadLinesAsync(
                    _snapshotManager.GetCurrentFilePath(relativePath), relativePath, cancellationToken).ConfigureAwait(false);
                if (modifiedLines == null)
                    return null;
            }

            var hunks = UnifiedDiffBuilder.Compute(originalLines, modifiedLines, ContextLines);
            if (hunks.Count == 0)
            {
                InternalLogger.Debug($"get_changes_diff: '{relativePath}' has no line differences, skipped.");
                return null;
            }

            var entry = new BuiltEntry
            {
                FilePath = relativePath,
                Status = status,
                Added = UnifiedDiffBuilder.CountAdded(hunks),
                Removed = UnifiedDiffBuilder.CountRemoved(hunks),
                RenderedLines = UnifiedDiffBuilder.CountRenderedLines(hunks)
            };

            if (includeDiff)
            {
                var rendered = new List<string>(hunks.Count);
                for (int i = 0; i < hunks.Count; i++)
                    rendered.Add(hunks[i].ToText());

                entry.Hunks = rendered;
            }

            return entry;
        }

        private async Task<string[]> TryReadLinesAsync(string path, string relativePath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(path) || !_fileSystem.FileExists(path))
            {
                InternalLogger.Warn(
                    $"get_changes_diff: skipping '{relativePath}', file '{path ?? "<null>"}' does not exist.");
                return null;
            }

            var (fileLength, _) = _fileSystem.GetFileInfo(path);
            if (fileLength > MaxFileBytes)
            {
                InternalLogger.Warn(
                    $"get_changes_diff: skipping '{relativePath}', {fileLength} bytes exceeds the {MaxFileBytes} byte limit.");
                return null;
            }

            string content = await _fileSystem
                .ReadAllTextWithSharedReadAsync(path, cancellationToken).ConfigureAwait(false);

            if (content == null || (content.Length == 0 && fileLength > 0))
            {
                InternalLogger.Warn($"get_changes_diff: skipping '{relativePath}', '{path}' could not be read.");
                return null;
            }

            string[] lines = UnifiedDiffBuilder.SplitLines(content);
            if (lines.Length > MaxLinesPerFile)
            {
                InternalLogger.Warn(
                    $"get_changes_diff: skipping '{relativePath}', {lines.Length} lines exceeds the {MaxLinesPerFile} line limit.");
                return null;
            }

            return lines;
        }

        /// <summary>
        /// True for exceptions that mean the process itself is in trouble rather than one file being unreadable.
        /// </summary>
        private static bool IsFatal(Exception ex)
        {
            return ex is OutOfMemoryException
                || ex is StackOverflowException
                || ex is AccessViolationException;
        }

        private static ChangesResult Empty(ChangesOutcome outcome)
        {
            return new ChangesResult
            {
                Success = true,
                TotalFiles = 0,
                TotalAdded = 0,
                TotalRemoved = 0,
                Files = new List<ChangesFile>(),
                HasMoreResults = false,
                Outcome = outcome
            };
        }

        /// <summary>One file whose diff was built, before the response caps are applied.</summary>
        private sealed class BuiltEntry
        {
            public string FilePath;
            public string Status;
            public int Added;
            public int Removed;
            public int RenderedLines;
            public List<string> Hunks;
        }
    }
}
