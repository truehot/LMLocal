using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Models.Usage;

namespace LMLocal.Application.Abstractions.Ports
{
    /// <summary>
    /// Writes one usage record per request. Write-only: the writers never read statistics back.
    /// </summary>
    public interface IUsageRecorder
    {
        /// <summary>
        /// Appends the record to the monthly usage file; silently does nothing when logging is disabled.
        /// </summary>
        Task AppendAsync(UsageRecord record, CancellationToken cancellationToken = default);
    }
}
