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
    }

    /// <summary>
    /// <see cref="IMissingFileHint"/> over the solution walk of <see cref="SearchScopeResolver"/>.
    /// </summary>
    internal sealed class MissingFileHint : IMissingFileHint
    {
        private readonly SearchScopeResolver _lookup;

        public MissingFileHint(IPathResolver pathResolver, IFileSystem fileSystem)
        {
            _lookup = new SearchScopeResolver(pathResolver, fileSystem);
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
            catch (Exception)
            {
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
