using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Explains a missing file to a model that named it with a wrong folder: says where a file of that name exists in the solution.
    /// </summary>
    internal interface IMissingFileHint
    {
        /// <summary>
        /// Builds the error text of a tool whose <c>file_path</c> does not exist: <c>File not found: &lt;shownPath&gt;</c>, followed by the
        /// solution-relative paths of the files with the same name (one: "exists at", several: a list, none: "exists nowhere").
        /// </summary>
        Task<string> BuildFileNotFoundErrorAsync(string requestedPath, string shownPath, string solutionDir, CancellationToken cancellationToken);

        /// <summary>
        /// For a read-only tool whose <c>file_path</c> does not exist: when exactly one file of that name exists in the solution, returns it ,
        /// so the tool can read it instead of failing and costing the model a round.
        /// </summary>
        Task<ResolvedMissingFile> TryResolveUniqueAsync(string requestedPath, string solutionDir, CancellationToken cancellationToken);
    }

    /// <summary>
    /// The only file of the requested name in the solution, found for a path that did not exist.
    /// </summary>
    internal sealed class ResolvedMissingFile
    {
        public ResolvedMissingFile(string absolutePath, string relativePath, string note)
        {
            AbsolutePath = absolutePath;
            RelativePath = relativePath;
            Note = note;
        }

        /// <summary>Absolute path of the file, inside the solution directory.</summary>
        public string AbsolutePath { get; }

        /// <summary>Solution-relative path of the file.</summary>
        public string RelativePath { get; }

        /// <summary>Text for the tool response: says which path was replaced by which, so the model cites the real one.</summary>
        public string Note { get; }
    }

    /// <summary>
    /// <see cref="IMissingFileHint"/> over the solution walk of <see cref="SearchScopeResolver"/>.
    /// </summary>
    internal sealed class MissingFileHint : IMissingFileHint
    {
        private readonly SearchScopeResolver _lookup;
        private readonly IPathResolver _pathResolver;

        public MissingFileHint(IPathResolver pathResolver, IFileSystem fileSystem)
        {
            _pathResolver = pathResolver;
            _lookup = new SearchScopeResolver(pathResolver, fileSystem);
        }

        public async Task<ResolvedMissingFile> TryResolveUniqueAsync(string requestedPath, string solutionDir, CancellationToken cancellationToken)
        {
            IReadOnlyList<string> candidates;
            try
            {
                (_, candidates) = await _lookup.FindFilesByNameAsync(requestedPath, solutionDir, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LMLocal.Core.Common.InternalLogger.Warn($"MissingFileHint: could not search the solution for '{requestedPath}'", ex);
                return null;
            }

            if (candidates == null || candidates.Count != 1)
                return null;

            if (!_pathResolver.TryResolveFilePath(candidates[0], solutionDir, out string absolute)
                || string.IsNullOrEmpty(absolute)
                || !_pathResolver.IsPathInsideDirectory(absolute, solutionDir))
                return null;

            return new ResolvedMissingFile(absolute, candidates[0], SearchScopeResolver.BuildReplacedPathNote(requestedPath, candidates[0]));
        }

        public async Task<string> BuildFileNotFoundErrorAsync(string requestedPath, string shownPath, string solutionDir, CancellationToken cancellationToken)
        {
            string message = "File not found: " + shownPath;

            string fileName;
            IReadOnlyList<string> candidates;
            try
            {
                (fileName, candidates) = await _lookup.FindFilesByNameAsync(requestedPath, solutionDir, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                LMLocal.Core.Common.InternalLogger.Warn($"MissingFileHint: could not search the solution for '{requestedPath}' while building the not-found message", ex);
                return message;
            }

            if (fileName == null)
                return message;

            if (candidates.Count == 0)
                return $"{message}. No file named '{fileName}' exists anywhere in the solution.";

            if (candidates.Count == 1)
                return $"{message}. The file '{fileName}' exists at '{candidates[0]}': call again with that path.";

            var shown = candidates.Take(SearchScopeResolver.MaxNameCandidates).Select(c => "'" + c + "'");
            string more = candidates.Count > SearchScopeResolver.MaxNameCandidates
                ? $" and {candidates.Count - SearchScopeResolver.MaxNameCandidates} more"
                : string.Empty;
            return $"{message}. Files named '{fileName}' exist at: {string.Join(", ", shown)}{more}. Call again with the right one.";
        }
    }
}
