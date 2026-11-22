using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Models.Usage;

namespace LMLocal.Application.Abstractions.Ports
{
    /// <summary>
    /// Reads usage records back for the statistics dialog. Read-only: the service never writes.
    /// </summary>
    public interface IUsageReader
    {
        /// <summary>
        /// All records whose start time is at or after <paramref name="fromUtc"/>, from the monthly files that cover the range.
        /// </summary>
        Task<IReadOnlyList<UsageRecord>> ReadAsync(DateTime fromUtc, CancellationToken cancellationToken = default);

        /// <summary>
        /// All records of one session.
        /// </summary>
        Task<IReadOnlyList<UsageRecord>> ReadSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    }
}
