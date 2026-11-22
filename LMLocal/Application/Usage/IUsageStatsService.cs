using System;
using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Application.Usage
{
    /// <summary>
    /// Token usage statistics for the dialog.
    /// </summary>
    internal interface IUsageStatsService
    {
        /// <summary>
        /// Statistics of one chat session (the Current tab). Null or empty id gives an empty result.
        /// </summary>
        Task<UsageStats> GetSessionStatsAsync(string sessionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Statistics of a period. "Now" and the local zone come from the time provider.
        /// </summary>
        Task<UsageStats> GetPeriodStatsAsync(UsagePeriod period, CancellationToken cancellationToken = default);
    }
}
