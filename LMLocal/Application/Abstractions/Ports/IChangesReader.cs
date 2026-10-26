using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Changes.Models;

namespace LMLocal.Application.Abstractions.Ports
{
    /// <summary>
    /// Read-only access to the diff of the pending (uncommitted) file changes recorded by the built-in write tools.
    /// </summary>
    public interface IChangesReader
    {
        /// <summary>
        /// Returns the pending file changes, optionally with their unified diff bodies.
        /// </summary>
        Task<ChangesResult> GetChangesAsync(
            IReadOnlyCollection<string> files,
            bool includeDiff,
            int maxFiles,
            int maxTotalLines,
            CancellationToken cancellationToken = default);
    }
}
