using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using LMLocal.Infrastructure.VisualStudio;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Outcome of resolving a chat file link.
    /// </summary>
    internal enum FileLinkStatus
    {
        Opened,
        NotFound,
        Ambiguous,
        Error
    }

    /// <summary>
    /// Result of <see cref="IFileLinkResolver.OpenAsync"/>.
    /// </summary>
    internal sealed class FileLinkOpenResult
    {
        public FileLinkStatus Status { get; set; }

        /// <summary>Solution-relative path of the file (only for <see cref="FileLinkStatus.Opened"/>).</summary>
        public string Path { get; set; }

        /// <summary>How the file was found: <c>direct</c> or <c>suffix</c>.</summary>
        public string Via { get; set; }

        /// <summary>At most <see cref="FileLinkResolver.MaxCandidates"/> solution-relative candidates (only for <see cref="FileLinkStatus.Ambiguous"/>).</summary>
        public IReadOnlyList<string> Candidates { get; set; }

        public static FileLinkOpenResult ForStatus(FileLinkStatus status)
        {
            return new FileLinkOpenResult { Status = status };
        }
    }

    /// <summary>
    /// Resolves a file link written by the model and opens the matching file in the editor.
    /// </summary>
    internal interface IFileLinkResolver
    {
        /// <summary>
        /// Resolves <paramref name="link"/> and opens the file when exactly one candidate matches. Never throws.
        /// </summary>
        Task<FileLinkOpenResult> OpenAsync(string link, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A chat file link is often a suffix of the real path (the model writes <c>services\app.data.service.js</c>
    /// for <c>LMLocal\Resources\js\services\app.data.service.js</c>).
    /// </summary>
    internal sealed class FileLinkResolver : IFileLinkResolver
    {
        internal const int MaxLinkLength = 512;
        internal const int MaxCandidates = 10;
        internal const int ScanTimeoutMs = 10000;

        /// <summary>Mirrors LOCATION_SUFFIX in chat.controller.js: <c>:42</c>, <c>:10-20</c>, <c>::Symbol</c>.</summary>
        private static readonly Regex LocationSuffix = new Regex(
            @"(?:::\s*[A-Za-z_$][\w$.]*|:\d+(?:[-\u2013]\d+)?)+$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IFileSystem _fileSystem;
        private readonly SearchScopeResolver _scopeResolver;
        private readonly Func<string, Task> _openFile;

        public FileLinkResolver(IVsDependencies vsDependencies, IPathResolver pathResolver, IFileSystem fileSystem)
            : this(vsDependencies, pathResolver, fileSystem, FileViewer.OpenFileAsync)
        {
        }

        /// <summary>
        /// Test seam: <paramref name="openFile"/> replaces <see cref="FileViewer.OpenFileAsync"/>.
        /// </summary>
        internal FileLinkResolver(
            IVsDependencies vsDependencies,
            IPathResolver pathResolver,
            IFileSystem fileSystem,
            Func<string, Task> openFile)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _openFile = openFile ?? throw new ArgumentNullException(nameof(openFile));
            _scopeResolver = new SearchScopeResolver(_pathResolver, _fileSystem);
        }

        public async Task<FileLinkOpenResult> OpenAsync(string link, CancellationToken cancellationToken = default)
        {
            string normalized = NormalizeLink(link);

            try
            {
                if (normalized == null)
                    return FileLinkOpenResult.ForStatus(FileLinkStatus.NotFound);

                if (!_vsDependencies.IsSolutionOpen)
                    return NotFound(normalized, "no solution is open");

                string solutionDir = _vsDependencies.GetSolutionDirectory();
                if (string.IsNullOrEmpty(solutionDir))
                    return NotFound(normalized, "the solution directory is unknown");

                if (_pathResolver.TryResolveFilePath(normalized, solutionDir, out string absolute) && !string.IsNullOrEmpty(absolute))
                {
                    if (_pathResolver.IsPathInsideDirectory(absolute, solutionDir) && _fileSystem.FileExists(absolute))
                    {
                        string directRelative = ToRelative(absolute, solutionDir) ?? normalized;
                        return await OpenResolvedAsync(absolute, directRelative, "direct", cancellationToken).ConfigureAwait(false);
                    }

                    // An absolute link is taken literally: it is never searched for by suffix.
                    if (Path.IsPathRooted(normalized))
                        return NotFound(normalized, "the absolute path does not exist or is outside the solution");
                }

                string fileName = Path.GetFileName(normalized);
                if (string.IsNullOrEmpty(fileName))
                    return NotFound(normalized, "the link has no file name");

                var files = await EnumerateFilesAsync(solutionDir, fileName, cancellationToken).ConfigureAwait(false);
                var relatives = SelectByFileName(files, fileName, solutionDir);
                var matches = SelectMatches(relatives, normalized);

                if (matches.Count == 0)
                    return NotFound(normalized, $"no file named '{fileName}' matches");

                if (matches.Count > 1)
                {
                    var candidates = matches
                        .OrderBy(m => m.Length)
                        .ThenBy(m => m, StringComparer.OrdinalIgnoreCase)
                        .Take(MaxCandidates)
                        .ToList();

                    InternalLogger.Warn(
                        $"FileLink '{normalized}': ambiguous, {matches.Count} candidate(s): {string.Join(", ", candidates)}");

                    return new FileLinkOpenResult
                    {
                        Status = FileLinkStatus.Ambiguous,
                        Candidates = candidates
                    };
                }

                return await OpenResolvedAsync(Path.Combine(solutionDir, matches[0]), matches[0], "suffix", cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                InternalLogger.Warn($"FileLink '{normalized ?? link}': the search was cancelled or timed out");
                return FileLinkOpenResult.ForStatus(FileLinkStatus.Error);
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"FileLink '{normalized ?? link}': failed", ex);
                return FileLinkOpenResult.ForStatus(FileLinkStatus.Error);
            }
        }

        /// <summary>
        /// Trims and sanitizes a link. Returns <c>null</c> when the link cannot be a path inside the solution.
        /// </summary>
        internal static string NormalizeLink(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
                return null;

            string value = link.Trim();
            if (value.Length > MaxLinkLength)
                return null;

            value = value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            value = LocationSuffix.Replace(value, string.Empty);

            // '*' and '?' are invalid in Windows file names, so such a link cannot be a path.
            if (value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0)
                return null;

            while (value.Contains(@"\\"))
                value = value.Replace(@"\\", @"\");

            while (value.StartsWith(@".\", StringComparison.Ordinal))
                value = value.Substring(2);

            value = value.TrimStart(Path.DirectorySeparatorChar);
            if (value.Length == 0)
                return null;

            return HasParentSegment(value) ? null : value;
        }

        /// <summary>
        /// Returns the candidates whose solution-relative path ends with the link on a segment boundary.
        /// </summary>
        internal static IReadOnlyList<string> SelectMatches(IEnumerable<string> relatives, string link)
        {
            return relatives
                .Where(r => !string.IsNullOrEmpty(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(r => IsSuffixMatch(r, link))
                .ToList();
        }

        private static bool IsSuffixMatch(string relativePath, string link)
        {
            return string.Equals(relativePath, link, StringComparison.OrdinalIgnoreCase)
                || relativePath.EndsWith(Path.DirectorySeparatorChar + link, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasParentSegment(string path)
        {
            foreach (var segment in path.Split(Path.DirectorySeparatorChar))
            {
                if (segment == "..")
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Walks the solution directory on disk, pruning generated/dependency folders. Narrowed by extension to keep the walk small.
        /// </summary>
        private async Task<IList<string>> EnumerateFilesAsync(string solutionDir, string fileName, CancellationToken cancellationToken)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(ScanTimeoutMs);

                return await _scopeResolver
                    .EnumerateFolderFilesAsync(new[] { solutionDir }, Path.GetExtension(fileName), 0, timeout.Token)
                    .ConfigureAwait(false);
            }
        }

        private List<string> SelectByFileName(IEnumerable<string> files, string fileName, string solutionDir)
        {
            var result = new List<string>();

            foreach (var file in files)
            {
                if (!string.Equals(Path.GetFileName(file), fileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                string relative = ToRelative(file, solutionDir);
                if (!string.IsNullOrEmpty(relative))
                    result.Add(relative);
            }

            return result;
        }

        private string ToRelative(string absolutePath, string solutionDir)
        {
            string baseDir = solutionDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return _pathResolver.TryGetRelativeNormalizedPath(absolutePath, baseDir, out string relative) ? relative : null;
        }

        private async Task<FileLinkOpenResult> OpenResolvedAsync(
            string absolutePath, string relativePath, string via, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _openFile(absolutePath).ConfigureAwait(false);

            InternalLogger.Info($"FileLink '{relativePath}' opened via {via}: {absolutePath}");

            return new FileLinkOpenResult
            {
                Status = FileLinkStatus.Opened,
                Path = relativePath,
                Via = via
            };
        }

        private static FileLinkOpenResult NotFound(string link, string reason)
        {
            InternalLogger.Warn($"FileLink '{link}': not found ({reason})");
            return FileLinkOpenResult.ForStatus(FileLinkStatus.NotFound);
        }
    }
}
