using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models.Usage;

namespace LMLocal.Application.Usage
{
    /// <summary>
    /// Reads the usage files of the requested selection, drops the IPC runs and hands the records to
    /// <see cref="UsageAggregator"/>.
    /// </summary>
    internal sealed class UsageStatsService : IUsageStatsService
    {
        private readonly IUsageReader _reader;
        private readonly ITimeProvider _timeProvider;

        public UsageStatsService(IUsageReader reader, ITimeProvider timeProvider)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        public async Task<UsageStats> GetSessionStatsAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return new UsageStats();

            IReadOnlyList<UsageRecord> records;

            try
            {
                records = await _reader.ReadSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"UsageStatsService: failed to read the usage of session '{sessionId}'", ex);
                return new UsageStats();
            }

            return UsageAggregator.Aggregate(ForDialog(records, null, null), _timeProvider.LocalTimeZone);
        }

        public async Task<UsageStats> GetPeriodStatsAsync(UsagePeriod period, CancellationToken cancellationToken = default)
        {
            TimeZoneInfo zone = _timeProvider.LocalTimeZone;
            DateTime nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(_timeProvider.UtcNow, DateTimeKind.Utc), zone);
            DateTime? startLocal = UsagePeriods.StartLocal(period, nowLocal);

            if (!startLocal.HasValue)
                return new UsageStats();

            DateTime startUtc = LocalToUtc(startLocal.Value, zone);

            DateTime? endLocal = UsagePeriods.EndLocal(period, nowLocal);
            DateTime? endUtc = endLocal.HasValue ? LocalToUtc(endLocal.Value, zone) : (DateTime?)null;

            IReadOnlyList<UsageRecord> records;

            try
            {
                records = await _reader.ReadAsync(startUtc, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"UsageStatsService: failed to read the usage of period '{UsagePeriods.ToWireName(period)}'", ex);
                return new UsageStats();
            }

            return UsageAggregator.Aggregate(ForDialog(records, startUtc, endUtc), zone);
        }

        /// <summary>
        /// UTC moment of a local midnight.
        /// </summary>
        private static DateTime LocalToUtc(DateTime local, TimeZoneInfo zone)
        {
            DateTime unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

            while (zone.IsInvalidTime(unspecified))
                unspecified = unspecified.AddMinutes(30);

            return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
        }

        /// <summary>
        /// Keeps the records the dialog may show: IPC runs never appear, and the records must fall inside the period,
        /// which starts at <paramref name="fromUtc"/> and, when the period has an end, stops before <paramref name="toUtc"/>.
        /// </summary>
        private static IEnumerable<UsageRecord> ForDialog(IReadOnlyList<UsageRecord> records, DateTime? fromUtc, DateTime? toUtc)
        {
            if (records == null)
                return Enumerable.Empty<UsageRecord>();

            IEnumerable<UsageRecord> filtered = records.Where(r => r != null && !UsageOrigins.IsIpc(r.Origin));

            if (fromUtc.HasValue)
                filtered = filtered.Where(r => r.Timestamp >= fromUtc.Value);

            if (toUtc.HasValue)
                filtered = filtered.Where(r => r.Timestamp < toUtc.Value);

            return filtered;
        }
    }
}
