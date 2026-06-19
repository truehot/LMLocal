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
        /// <summary>
        /// Asynchronously enumerates files from the Visual Studio solution. 
        Task<IList<string>> EnumerateSolutionFilesAsync(EnumerateSolutionFilesFilter filter, CancellationToken cancellationToken = default);
    }

    internal class VsSolutionFilesScanner : IVsSolutionFilesScanner
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IUiThreadGuard _uiThreadGuard;
        private readonly IPathResolver _pathResolver;
        private readonly ISolutionFileProvider _solutionFileProvider;

        private static readonly HashSet<string> _imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", ".webp", ".tiff" };
        private static readonly string[] _minifiedSuffixes = { ".min.js", ".min.css", ".udm.js" };
        private static readonly string[] _excludedDirectories = { "bin", "obj", ".vs", ".git", "CopilotBaseline" };

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

            var ivSolution = _vsDependencies.GetSolution();
            if (ivSolution == null)
                throw new InvalidOperationException("No solution is currently open.");

            var filesList = _solutionFileProvider.GetFiles(ivSolution, filter.IncludeProjects).ToList();

            var normalizedSolutionDir = NormalizeDir(_vsDependencies.GetSolutionDirectory());
            var extensions = ParseExtensions(filter.ExtensionFilter);
            var returnRelative = filter.ReturnRelative;
            var fileNameFilter = filter.FileName;
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

                    if (!IsMatch(normalizedFilePath, extensions, fileNameFilter, projectFilter, normalizedSolutionDir))
                        continue;

                    if (seen.Add(normalizedFilePath))
                    {
                        string output = normalizedFilePath;

                        if (returnRelative && !string.IsNullOrEmpty(normalizedSolutionDir))
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

        private static bool IsMatch(string normalizedFilePath, HashSet<string> extensions, string fileName, string projectFilter, string normalizedSolutionDir)
        {
            if (string.IsNullOrEmpty(normalizedFilePath))
                return false;

            if (ShouldExcludePath(normalizedFilePath))
                return false;

            var fname = Path.GetFileName(normalizedFilePath);
            if (!string.IsNullOrEmpty(fileName) && (string.IsNullOrEmpty(fname) || fname.IndexOf(fileName, StringComparison.OrdinalIgnoreCase) < 0))
            {
                return false;
            }

            if (IsMinifiedFile(fname))
                return false;

            if (IsImageFile(fname))
                return false;

            if (extensions != null && extensions.Count > 0)
            {
                var ext = Path.GetExtension(normalizedFilePath);
                if (!extensions.Contains(ext))
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

        private static bool ShouldExcludePath(string normalizedFilePath)
        {
            foreach (var dir in _excludedDirectories)
            {
                if (normalizedFilePath.IndexOf(Path.DirectorySeparatorChar + dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (normalizedFilePath.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        private static bool IsImageFile(string fileName) => _imageExtensions.Contains(Path.GetExtension(fileName));
        private static bool IsMinifiedFile(string fileName)
        {
            foreach (var suffix in _minifiedSuffixes)
            {
                if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
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
            /// <summary>
            /// Optional extension filter (e.g. ".cs" or ".cs;.xaml"). Null or empty means no extension filtering.
            /// </summary>
            public string ExtensionFilter { get; set; }

            /// <summary>
            /// Maximum number of results to return. Pass 0 or a negative value to indicate no limit. Default is 200.
            /// This limits the number of returned files, not the number of files scanned.
            /// </summary>
            public int Limit { get; set; } = 200;

            /// <summary>
            /// Optional file name (or partial name) to match. Case-insensitive substring match.
            /// </summary>
            public string FileName { get; set; }

            /// <summary>
            /// If true, returns relative paths from solution directory; if false, returns absolute paths. Default is true.
            /// </summary>
            public bool ReturnRelative { get; set; } = true;

            /// <summary>
            /// Optional project name filter. If specified, only files from projects matching this name 
            /// (case-insensitive substring match) will be returned.
            /// </summary>
            public string ProjectFilter { get; set; }

            /// <summary>
            /// If true, includes the project files themselves (e.g., .csproj, .vbproj) in the result.
            /// Default is false (only source/document files are returned).
            /// </summary>
            public bool IncludeProjects { get; set; } = false;
        }

    }
}
