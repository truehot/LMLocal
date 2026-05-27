using System;
using System.Collections.Generic;
using System.IO;
using LMLocal.Common;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
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
        /// If true, automatically excludes files in temporary directories (%TEMP%, %LOCALAPPDATA%\Temp) and build directories (bin, obj, .vs, .git, CopilotBaseline).
        /// Also excludes minified files (*.min.js, *.min.css, *.udm.js). Default is true.
        /// Set to false in test scenarios where temporary directories should be included.
        /// </summary>
        public bool ExcludeTemporaryDirectories { get; set; } = true;
    }

    internal interface IVsSolutionFilesScanner
    {
        /// <summary>
        /// Enumerates files from the Visual Studio solution by traversing project hierarchies.
        /// Automatically filters out temporary and build directories (bin, obj, .vs, .git, CopilotBaseline),
        /// system temp directories (%TEMP%, %LOCALAPPDATA%\Temp), and minified files (*.min.js, *.min.css, *.udm.js).
        /// </summary>
        /// <param name="filter">Filter configuration for file enumeration.</param>
        /// <returns>Enumerable of file paths (relative or absolute based on returnRelative parameter). Limited by the limit parameter.</returns>
        IEnumerable<string> EnumerateSolutionFiles(EnumerateSolutionFilesFilter filter);
    }

    internal class VsSolutionFilesScanner : IVsSolutionFilesScanner
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IUiThreadGuard _uiThreadGuard;

        private static readonly string _tempPath = Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath();
        private static readonly string _localAppDataTemp = Environment.GetEnvironmentVariable("LOCALAPPDATA");

        public VsSolutionFilesScanner(IVsDependencies vsDependencies, IUiThreadGuard uiThreadGuard)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _uiThreadGuard = uiThreadGuard ?? throw new ArgumentNullException(nameof(uiThreadGuard));
        }

        public IEnumerable<string> EnumerateSolutionFiles(EnumerateSolutionFilesFilter filter)
        {
            if (filter == null)
                throw new ArgumentNullException(nameof(filter));

            _uiThreadGuard.EnsureOnUIThread();

            return EnumerateSolutionFilesIterator(filter);
        }

        private IEnumerable<string> EnumerateSolutionFilesIterator(EnumerateSolutionFilesFilter filter)
        {
            string solutionDir = _vsDependencies.GetSolutionDirectory();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var extensions = ParseExtensions(filter.ExtensionFilter);
            int yielded = 0;

            var provider = _vsDependencies.GetFileProvider();
            var files = provider.GetFiles();

            foreach (var file in files)
            {
                if (!IsMatch(file, extensions, filter.FileName, filter.ProjectFilter, solutionDir, filter.ExcludeTemporaryDirectories))
                    continue;

                if (seen.Contains(file))
                    continue;

                if (seen.Add(file))
                {
                    var output = TryMakeRelative(file, solutionDir, filter.ReturnRelative);
                    yield return output;
                    yielded++;
                    if (filter.Limit > 0 && yielded >= filter.Limit)
                        yield break;
                }
            }
        }

        private static string TryMakeRelative(string absolutePath, string solutionDir, bool makeRelative)
        {
            if (!makeRelative || string.IsNullOrEmpty(solutionDir) || string.IsNullOrEmpty(absolutePath))
                return absolutePath;

            try
            {

                var fullFile = Path.GetFullPath(absolutePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var fullSol = Path.GetFullPath(solutionDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (!fullFile.StartsWith(fullSol, StringComparison.OrdinalIgnoreCase))
                    return absolutePath;

                if (fullFile.Equals(fullSol, StringComparison.OrdinalIgnoreCase))
                    return ".";

                int skipLength = fullSol.Length;

                string[] absoluteParts = absolutePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string[] solParts = solutionDir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);


                int commonCount = 0;
                for (int i = 0; i < Math.Min(absoluteParts.Length, solParts.Length); i++)
                {
                    if (string.Equals(absoluteParts[i], solParts[i], StringComparison.OrdinalIgnoreCase))
                        commonCount++;
                    else
                        break;
                }

                var relParts = new List<string>();
                for (int i = commonCount; i < absoluteParts.Length; i++)
                {
                    if (!string.IsNullOrEmpty(absoluteParts[i]))
                        relParts.Add(absoluteParts[i]);
                }

                if (relParts.Count == 0)
                    return ".";

                return string.Join(Path.DirectorySeparatorChar.ToString(), relParts);
            }
            catch (Exception ex)
            {
                InternalLogger.Debug($"TryMakeRelative failed for file='{absolutePath}', solutionDir='{solutionDir}': {ex.Message}");
                return absolutePath;
            }
        }

        private static bool IsMatch(string filePath, HashSet<string> extensions, string fileName, string projectFilter, string solutionDir, bool excludeTemporaryDirectories)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            if (ShouldExcludePath(filePath, excludeTemporaryDirectories))
                return false;

            if (!string.IsNullOrEmpty(fileName))
            {
                var name = Path.GetFileName(filePath);
                if (string.IsNullOrEmpty(name) || name.IndexOf(fileName, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            if (extensions != null && extensions.Count > 0)
            {
                var ext = Path.GetExtension(filePath);
                if (!extensions.Contains(ext))
                    return false;
            }

            if (!string.IsNullOrEmpty(projectFilter) && !string.IsNullOrEmpty(solutionDir))
            {
                string normalizedFilePath = Path.GetFullPath(filePath).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                string normalizedSolutionDir = Path.GetFullPath(solutionDir).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

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

        private static bool ShouldExcludePath(string filePath, bool excludeTemporaryDirectories = true)
        {
            string normalizedPath = filePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string fileName = Path.GetFileName(filePath);

            if (IsMinifiedFile(fileName))
                return true;

            string[] excludedDirectories = { "bin", "obj", ".vs", ".git", "CopilotBaseline" };
            foreach (var dir in excludedDirectories)
            {
                if (normalizedPath.IndexOf(Path.DirectorySeparatorChar + dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (normalizedPath.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (excludeTemporaryDirectories && IsInTemporaryDirectory(normalizedPath))
                return true;

            return false;
        }

        private static bool IsMinifiedFile(string fileName)
        {
            return fileName.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".udm.js", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInTemporaryDirectory(string normalizedPath)
        {
            if (!string.IsNullOrEmpty(_tempPath))
            {
                string tempPath = _tempPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                if (normalizedPath.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (!string.IsNullOrEmpty(_localAppDataTemp))
            {
                string localAppDataTemp = Path.Combine(_localAppDataTemp, "Temp").Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                if (normalizedPath.StartsWith(localAppDataTemp, StringComparison.OrdinalIgnoreCase))
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
    }
}
