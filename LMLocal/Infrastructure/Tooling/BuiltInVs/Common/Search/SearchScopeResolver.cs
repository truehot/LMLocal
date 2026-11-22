using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// What a <c>path</c> value of a search tool resolved to.
    /// </summary>
    internal enum SearchScopeKind
    {
        /// <summary>One file: it is read directly, nothing is enumerated.</summary>
        File,

        /// <summary>One or more folders: the files under them are enumerated on disk.</summary>
        Folder
    }

    /// <summary>
    /// A resolved search scope: one file or a set of folders, all inside the solution directory.
    /// </summary>
    internal sealed class SearchScope
    {
        public SearchScopeKind Kind { get; private set; }

        /// <summary>Absolute path of the file (only for <see cref="SearchScopeKind.File"/>).</summary>
        public string FilePath { get; private set; }

        /// <summary>Absolute folder paths without a trailing separator (only for <see cref="SearchScopeKind.Folder"/>).</summary>
        public IReadOnlyList<string> Directories { get; private set; }

        /// <summary>Stable identity of the scope, for cache keys.</summary>
        public string Key { get; private set; }

        public static SearchScope ForFile(string absolutePath)
        {
            return new SearchScope
            {
                Kind = SearchScopeKind.File,
                FilePath = absolutePath,
                Directories = new string[0],
                Key = "file:" + absolutePath.ToLowerInvariant()
            };
        }

        public static SearchScope ForFolders(IReadOnlyList<string> absoluteDirectories)
        {
            return new SearchScope
            {
                Kind = SearchScopeKind.Folder,
                Directories = absoluteDirectories,
                Key = "dir:" + string.Join("|", absoluteDirectories.Select(d => d.ToLowerInvariant()).OrderBy(d => d, StringComparer.Ordinal))
            };
        }
    }

    /// <summary>
    /// Resolves the <c>path</c> parameter of <c>search_file_content</c> (existing file → existing folder → exact project name)
    /// and enumerates the files of a folder scope on disk.
    /// </summary>
    internal sealed class SearchScopeResolver
    {
        /// <summary>Upper bound for the solution walk that looks up a missing file by name.</summary>
        internal const int NameLookupTimeoutMs = 10000;

        /// <summary>Files with the same name listed in the error when the path is wrong.</summary>
        internal const int MaxNameCandidates = 10;

        private readonly IPathResolver _pathResolver;
        private readonly IFileSystem _fileSystem;

        public SearchScopeResolver(IPathResolver pathResolver, IFileSystem fileSystem)
        {
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        /// <summary>
        /// Resolves <paramref name="path"/>. An existing path always wins over a project name; project names match exactly, never as a substring.
        /// </summary>
        /// <param name="findProjectDirectoriesAsync">Returns the folders of the solution projects whose name exactly matches the value (empty when none).</param>
        public async Task<(SearchScope Scope, string Error)> ResolveAsync(
            string path,
            string solutionDir,
            Func<string, Task<IReadOnlyList<string>>> findProjectDirectoriesAsync,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(path))
                return (null, "Parameter 'path' is empty.");
            if (string.IsNullOrEmpty(solutionDir))
                return (null, "The solution directory is unknown.");

            string trimmed = path.Trim().Trim('"', '\'').Trim();

            if (_pathResolver.TryResolveFilePath(trimmed, solutionDir, out string absolute) && !string.IsNullOrEmpty(absolute))
            {
                absolute = TrimTrailingSeparator(absolute);
                bool isFile = _fileSystem.FileExists(absolute);
                bool isDirectory = !isFile && _fileSystem.DirectoryExists(absolute);

                if (isFile || isDirectory)
                {
                    if (!_pathResolver.IsPathInsideDirectory(absolute, solutionDir))
                        return (null, $"Path '{absolute}' is outside the solution directory '{solutionDir}'. Only files inside the open solution can be searched.");

                    SearchScope scope = isFile ? SearchScope.ForFile(absolute) : SearchScope.ForFolders(new[] { absolute });
                    return (scope, null);
                }
            }

            if (findProjectDirectoriesAsync != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var projectDirectories = await findProjectDirectoriesAsync(trimmed).ConfigureAwait(false) ?? new string[0];

                var inside = projectDirectories
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Select(TrimTrailingSeparator)
                    .Where(d => _fileSystem.DirectoryExists(d) && _pathResolver.IsPathInsideDirectory(d, solutionDir))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (inside.Count > 0)
                    return (SearchScope.ForFolders(inside), null);
            }

            return (null, await BuildNotFoundErrorAsync(path, trimmed, solutionDir, cancellationToken).ConfigureAwait(false));
        }

        /// <summary>
        /// Error for a path that resolved to nothing.
        /// </summary>
        private async Task<string> BuildNotFoundErrorAsync(string path, string trimmed, string solutionDir, CancellationToken cancellationToken)
        {
            string generic = $"Path not found: '{path}'. path must be an existing file or folder inside the solution directory, or a project name. A bare file name is not a path: resolve it with find_files first.";

            var (fileName, candidates) = await FindFilesByNameAsync(trimmed, solutionDir, cancellationToken).ConfigureAwait(false);
            if (fileName == null)
                return generic;

            if (candidates.Count == 0)
                return $"File not found: '{path}'. No file named '{fileName}' exists anywhere in the solution.";

            if (candidates.Count == 1)
                return $"Path not found: '{path}'. The file '{fileName}' exists at '{candidates[0]}': call again with that path.";

            var shown = candidates.Take(MaxNameCandidates).Select(c => "'" + c + "'");
            string more = candidates.Count > MaxNameCandidates ? $" and {candidates.Count - MaxNameCandidates} more" : string.Empty;
            return $"Path not found: '{path}'. Files named '{fileName}' exist at: {string.Join(", ", shown)}{more}. Call again with the right one.";
        }

        /// <summary>
        /// Looks a file up by its name anywhere in the solution: a wrong folder is the usual reason a path does not exist.
        /// </summary>
        public async Task<(string FileName, IReadOnlyList<string> Candidates)> FindFilesByNameAsync(
            string path, string solutionDir, CancellationToken cancellationToken = default)
        {
            var none = ((string)null, (IReadOnlyList<string>)new string[0]);
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrEmpty(solutionDir))
                return none;

            string trimmed = path.Trim().Trim('"', '\'').Trim();
            string link = FileLinkResolver.NormalizeLink(trimmed);
            string fileName = link == null ? null : Path.GetFileName(link);
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(Path.GetExtension(fileName)))
                return none;

            List<string> files;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(NameLookupTimeoutMs);
                try
                {
                    files = await EnumerateFolderFilesAsync(new[] { solutionDir }, Path.GetExtension(fileName), 0, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return none;
                }
            }

            string baseDir = solutionDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidates = files
                .Where(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase))
                .Select(f => _pathResolver.TryGetRelativeNormalizedPath(f, baseDir, out string relative) ? relative : null)
                .Where(r => !string.IsNullOrEmpty(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r.Length)
                .ThenBy(r => r, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return (fileName, candidates);
        }

        /// <summary>
        /// Enumerates the files under <paramref name="directories"/> recursively, skipping generated/dependency folders and binary/media files
        /// (<see cref="VsFileFilter"/>).
        /// </summary>
        public async Task<List<string>> EnumerateFolderFilesAsync(
            IEnumerable<string> directories, string extensionFilter, int limit, CancellationToken cancellationToken = default)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var extensions = VsSolutionFilesScanner.ParseExtensions(extensionFilter);

            foreach (var directory in directories ?? Enumerable.Empty<string>())
            {
                if (limit > 0 && result.Count >= limit)
                    break;

                await WalkAsync(directory, extensions, limit, result, seen, cancellationToken).ConfigureAwait(false);
            }

            return result;
        }

        private async Task WalkAsync(
            string directory, HashSet<string> extensions, int limit, List<string> result, HashSet<string> seen, CancellationToken cancellationToken)
        {
            var pending = new Stack<string>();
            pending.Push(directory);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string current = pending.Pop();

                var entries = await _fileSystem.EnumerateDirectoryAsync(current, VsFileFilter.ExcludedDirectories, cancellationToken).ConfigureAwait(false);

                foreach (var entry in entries.Where(e => !e.IsDirectory))
                {
                    if (!IsIncluded(entry.FullPath, entry.Name, extensions))
                        continue;

                    if (seen.Add(entry.FullPath))
                    {
                        result.Add(entry.FullPath);
                        if (limit > 0 && result.Count >= limit)
                            return;
                    }
                }

                var subdirectories = entries.Where(e => e.IsDirectory).ToList();
                for (int i = subdirectories.Count - 1; i >= 0; i--)
                    pending.Push(subdirectories[i].FullPath);
            }
        }

        private static bool IsIncluded(string fullPath, string fileName, HashSet<string> extensions)
        {
            if (extensions != null)
                return extensions.Contains(Path.GetExtension(fullPath));

            return !VsFileFilter.IsExcludedFile(fileName);
        }

        private static string TrimTrailingSeparator(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return trimmed.EndsWith(":", StringComparison.Ordinal) ? trimmed + Path.DirectorySeparatorChar : trimmed;
        }
    }
}
