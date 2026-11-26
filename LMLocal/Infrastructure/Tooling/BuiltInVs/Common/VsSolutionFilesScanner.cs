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
                if (!MatchesNamePattern(normalizedFilePath, fileNamePattern))
                    return false;
            }

            if (!string.IsNullOrEmpty(projectFilter) && !string.IsNullOrEmpty(normalizedSolutionDir))
            {
                if (!normalizedFilePath.StartsWith(normalizedSolutionDir, StringComparison.OrdinalIgnoreCase))
                    return false;

                string relativePath = normalizedFilePath.Substring(normalizedSolutionDir.Length).TrimStart(Path.DirectorySeparatorChar);
                if (!MatchesProjectFilter(relativePath, projectFilter, normalizedSolutionDir))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Applies a tool's file cap to a list enumerated with <c>Limit = limit + 1</c>.
        /// </summary>
        internal static bool TrimToLimit(List<string> files, int limit)
        {
            if (files == null || limit <= 0 || files.Count <= limit)
                return false;

            files.RemoveRange(limit, files.Count - limit);
            return true;
        }

        /// <summary>
        /// Matches a solution-relative path against a project_filter value.
        /// </summary>
        internal static bool MatchesProjectFilter(string relativePath, string projectFilter, string normalizedSolutionDir = null)
        {
            if (string.IsNullOrEmpty(projectFilter))
                return true;
            if (string.IsNullOrEmpty(relativePath))
                return false;

            string filter = NormalizeSeparators(projectFilter.Trim());
            string path = NormalizeSeparators(relativePath);

            if (!string.IsNullOrEmpty(normalizedSolutionDir))
            {
                string solutionDir = NormalizeSeparators(normalizedSolutionDir).TrimEnd('\\') + "\\";
                if (filter.StartsWith(solutionDir, StringComparison.OrdinalIgnoreCase))
                    filter = "\\" + filter.Substring(solutionDir.Length);
            }

            bool isPath = filter.IndexOf('\\') >= 0;
            string[] components = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);

            if (!isPath)
            {
                if (filter.Length == 0)
                    return true;

                foreach (var component in components)
                {
                    if (component.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                return false;
            }

            string[] segments = filter.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return true;

            for (int start = 0; start + segments.Length <= components.Length; start++)
            {
                int i = 0;
                while (i < segments.Length && string.Equals(components[start + i], segments[i], StringComparison.OrdinalIgnoreCase))
                    i++;
                if (i == segments.Length)
                    return true;
            }

            return false;
        }

        private static string NormalizeSeparators(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var sb = new System.Text.StringBuilder(value.Length);
            bool previousWasSeparator = false;
            foreach (char c in value)
            {
                bool isSeparator = c == '\\' || c == '/';
                if (isSeparator)
                {
                    if (!previousWasSeparator)
                        sb.Append('\\');
                }
                else
                {
                    sb.Append(c);
                }
                previousWasSeparator = isSeparator;
            }
            return sb.ToString();
        }

        /// <summary>Matches a file against a file_name pattern; folders before the last '/' or '\' must be the folders right above the file.</summary>
        internal static bool MatchesNamePattern(string normalizedFilePath, string pattern)
        {
            string fileName = Path.GetFileName(normalizedFilePath);
            if (!TrySplitFolderPattern(pattern, out string[] folders, out string namePart))
                return MatchesPattern(fileName, pattern);

            if (!MatchesPattern(fileName, namePart))
                return false;

            string[] directories = (Path.GetDirectoryName(normalizedFilePath) ?? string.Empty)
                .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (folders.Length > directories.Length)
                return false;

            int offset = directories.Length - folders.Length;
            for (int i = 0; i < folders.Length; i++)
            {
                if (!string.Equals(directories[offset + i], folders[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        /// <summary>Splits a file_name pattern into its folders and the name part after the last '/' or '\'; false when it has no separator.</summary>
        internal static bool TrySplitFolderPattern(string pattern, out string[] folders, out string namePart)
        {
            folders = new string[0];
            namePart = pattern;
            if (string.IsNullOrEmpty(pattern) || (pattern.IndexOf('/') < 0 && pattern.IndexOf('\\') < 0))
                return false;

            string normalized = NormalizeSeparators(pattern.Trim());
            int last = normalized.LastIndexOf('\\');
            namePart = normalized.Substring(last + 1);
            folders = normalized.Substring(0, last)
                .Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(segment => segment != ".")
                .ToArray();
            return true;
        }

        /// <summary>
        /// Matches a file name against a pattern that may contain '*' wildcards.
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

        internal static HashSet<string> ParseExtensions(string extensionFilter)
        {
            if (string.IsNullOrWhiteSpace(extensionFilter))
                return null;

            var parts = extensionFilter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in parts)
            {
                var p = part.Trim();
                if (string.IsNullOrEmpty(p)) continue;

                if (p == "*" || p == "*.*" || p == ".*") return null;
                if (p.StartsWith("*.")) p = p.Substring(1);

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
