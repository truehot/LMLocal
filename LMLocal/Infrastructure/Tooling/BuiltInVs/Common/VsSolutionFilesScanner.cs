using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Common.VsSolutionFilesScanner;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    internal interface IVsSolutionFilesScanner
    {
        Task<IList<string>> EnumerateSolutionFilesAsync(EnumerateSolutionFilesFilter filter, CancellationToken cancellationToken = default);
    }

    internal class VsSolutionFilesScanner : IVsSolutionFilesScanner
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IUiThreadGuard _uiThreadGuard;
        private readonly IPathResolver _pathResolver;
        private readonly ISolutionFileProvider _solutionFileProvider;


        public VsSolutionFilesScanner(IVsDependencies vsDependencies, IUiThreadGuard uiThreadGuard, IPathResolver pathResolver, ISolutionFileProvider solutionFileProvider)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _uiThreadGuard = uiThreadGuard ?? throw new ArgumentNullException(nameof(uiThreadGuard));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _solutionFileProvider = solutionFileProvider ?? throw new ArgumentNullException(nameof(solutionFileProvider));
        }

        public async Task<IList<string>> EnumerateSolutionFilesAsync(EnumerateSolutionFilesFilter filter, CancellationToken cancellationToken = default)
        {
            if (filter == null)
                throw new ArgumentNullException(nameof(filter));

            _uiThreadGuard.EnsureOnUIThread();

            var ivSolution = _vsDependencies.GetSolution() ?? throw new InvalidOperationException("No solution is currently open.");
            var filesList = _solutionFileProvider.GetFiles(ivSolution, filter.IncludeProjects).ToList();

            var normalizedSolutionDir = NormalizeDir(_vsDependencies.GetSolutionDirectory());
            var extensions = ParseExtensions(filter.ExtensionFilter);
            var fileNamePattern = filter.FileName;
            var projectFilter = filter.ProjectFilter;
            var limit = filter.Limit;

            return await Task.Run(() =>
            {
                var result = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                int yielded = 0;

                foreach (var file in filesList)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string normalizedFilePath = Path.IsPathRooted(file)
                                        ? file.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                                        : Path.GetFullPath(file).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

                    if (!IsMatch(normalizedFilePath, extensions, fileNamePattern, projectFilter, normalizedSolutionDir))
                        continue;

                    if (seen.Add(normalizedFilePath))
                    {
                        string output = normalizedFilePath;

                        if (filter.ReturnRelative && !string.IsNullOrEmpty(normalizedSolutionDir))
                        {
                            if (_pathResolver.TryGetRelativeNormalizedPath(normalizedFilePath, normalizedSolutionDir.TrimEnd(Path.DirectorySeparatorChar), out var rel))
                                output = rel;
                        }

                        result.Add(output);
                        yielded++;
                        if (limit > 0 && yielded >= limit)
                            break;
                    }
                }

                return (IList<string>)result;
            }, cancellationToken).ConfigureAwait(false);
        }

        private static string NormalizeDir(string path)
        {
            string normalizedSolutionDir = null;
            if (!string.IsNullOrEmpty(path))
            {
                normalizedSolutionDir = Path.GetFullPath(path);
                normalizedSolutionDir = normalizedSolutionDir.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                if (!normalizedSolutionDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    normalizedSolutionDir += Path.DirectorySeparatorChar;
            }
            return normalizedSolutionDir;
        }

        private static bool IsMatch(string normalizedFilePath, HashSet<string> extensions, string fileNamePattern, string projectFilter, string normalizedSolutionDir)
        {
            if (string.IsNullOrEmpty(normalizedFilePath))
                return false;

            if (VsFileFilter.ShouldExcludePath(normalizedFilePath))
                return false;

            string fname = Path.GetFileName(normalizedFilePath);

            if (extensions != null && extensions.Count > 0)
            {
                var ext = Path.GetExtension(normalizedFilePath);
                if (!extensions.Contains(ext))
                    return false;
            }
            else
            {
                if (VsFileFilter.IsExcludedFile(fname))
                    return false;
            }

            if (!string.IsNullOrEmpty(fileNamePattern))
            {
                if (!MatchesPattern(fname, fileNamePattern))
                    return false;
            }

            if (!string.IsNullOrEmpty(projectFilter) && !string.IsNullOrEmpty(normalizedSolutionDir))
            {
                if (normalizedFilePath.StartsWith(normalizedSolutionDir, StringComparison.OrdinalIgnoreCase))
                {
                    string relativePath = normalizedFilePath.Substring(normalizedSolutionDir.Length).TrimStart(Path.DirectorySeparatorChar);
                    string[] pathComponents = relativePath.Split(Path.DirectorySeparatorChar);
                    bool projectFound = false;
                    foreach (var component in pathComponents)
                    {
                        if (component.IndexOf(projectFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            projectFound = true;
                            break;
                        }
                    }
                    if (!projectFound)
                        return false;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Matches a file name against a pattern that may contain '*' wildcards.
        /// Supports multiple '*' in any position. Uses sequential search for each part.
        /// If pattern has no '*', performs a case-insensitive substring match.
        /// </summary>
        private static bool MatchesPattern(string fileName, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return true;
            if (pattern == "*")
                return true;

            if (!pattern.Contains("*"))
                return fileName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;

            var parts = pattern.Split(new[] { '*' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return true;

            if (!pattern.StartsWith("*") && !fileName.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase))
                return false;

            if (!pattern.EndsWith("*") && !fileName.EndsWith(parts[parts.Length - 1], StringComparison.OrdinalIgnoreCase))
                return false;

            int currentIndex = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                int foundIndex = fileName.IndexOf(part, currentIndex, StringComparison.OrdinalIgnoreCase);
                if (foundIndex == -1)
                    return false;
                currentIndex = foundIndex + part.Length;
            }

            return true;
        }

        private static HashSet<string> ParseExtensions(string extensionFilter)
        {
            if (string.IsNullOrWhiteSpace(extensionFilter))
                return null;

            var parts = extensionFilter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in parts)
            {
                var p = part.Trim();
                if (string.IsNullOrEmpty(p)) continue;
                if (!p.StartsWith(".")) p = "." + p;
                set.Add(p);
            }
            return set.Count == 0 ? null : set;
        }

        internal class EnumerateSolutionFilesFilter
        {
            public string ExtensionFilter { get; set; }
            public int Limit { get; set; } = 200;
            public string FileName { get; set; }
            public bool ReturnRelative { get; set; } = true;
            public string ProjectFilter { get; set; }
            public bool IncludeProjects { get; set; } = false;
        }
    }
}
