using System;
using System.Collections.Generic;
using System.Linq;
using LMLocal.Core.Models.Usage;

namespace LMLocal.Application.Usage
{
    /// <summary>
    /// Pure aggregation of usage records into the numbers the dialog shows: totals, BY SOURCE, BY MODEL,
    /// context peak and sessions grouped by local day. No IO and no dependency on "now".
    /// </summary>
    internal static class UsageAggregator
    {
        /// <summary>Separator that cannot appear in a provider id, an address or a model id.</summary>
        private const char KeySeparator = '\u001f';

        /// <summary>Unknown model name shown instead of an empty cell.</summary>
        private const string UnknownModel = "unknown";

        /// <summary>
        /// Aggregates the records. <paramref name="localTimeZone"/> is the zone local days are counted in; null means the
        /// machine zone. Records are UTC, so a change of zone only regroups the days.
        /// </summary>
        internal static UsageStats Aggregate(IEnumerable<UsageRecord> records, TimeZoneInfo localTimeZone = null)
        {
            var stats = new UsageStats();

            var list = records == null
                ? new List<UsageRecord>()
                : records.Where(r => r != null).ToList();

            if (list.Count == 0)
                return stats;

            stats.HasData = true;
            stats.Totals = BuildTotals(list);
            stats.Sources = BuildSourceRows(list);
            stats.Models = BuildModelRows(list);
            stats.Days = BuildDayGroups(list, localTimeZone ?? TimeZoneInfo.Local);

            return stats;
        }

        /// <summary>
        /// True for the outcomes the dialog counts as failures. A cancellation is a user action, not a failure.
        /// </summary>
        internal static bool IsFailure(UsageRecord record)
        {
            if (record == null)
                return false;

            return record.Outcome == UsageOutcome.Timeout
                || record.Outcome == UsageOutcome.Incomplete
                || record.Outcome == UsageOutcome.Error;
        }

        private static UsageTotals BuildTotals(IReadOnlyList<UsageRecord> records)
        {
            var totals = new UsageTotals
            {
                Requests = records.Count(r => r.Source == UsageSource.Main),
                Failed = records.Count(IsFailure),
                DurationMs = records.Where(r => r.Source == UsageSource.Main).Sum(r => r.DurationMs),
                Estimated = records.Any(r => r.Estimated)
            };

            double? peak = null;

            foreach (var record in records)
            {
                totals.Prompt += record.Prompt ?? 0;
                totals.Completion += record.Completion ?? 0;
                totals.Cached += record.Cached ?? 0;
                totals.Reasoning += record.Reasoning ?? 0;

                if (record.ContextPeak is int contextPeak && record.ContextMax is int contextMax && contextMax > 0)
                {
                    double ratio = (double)contextPeak / contextMax;

                    if (!peak.HasValue || ratio > peak.Value)
                        peak = ratio;
                }
            }

            totals.PeakContextPercent = peak;

            return totals;
        }

        private static List<UsageSourceRow> BuildSourceRows(IReadOnlyList<UsageRecord> records)
        {
            var rows = new List<UsageSourceRow>();

            foreach (var source in new[] { UsageSource.Main, UsageSource.Subagent, UsageSource.Compaction })
            {
                var group = records.Where(r => r.Source == source).ToList();

                if (group.Count == 0)
                    continue;

                rows.Add(new UsageSourceRow
                {
                    Source = source,
                    Runs = group.Count,
                    Failed = group.Count(IsFailure),
                    Prompt = group.Sum(r => r.Prompt ?? 0),
                    Completion = group.Sum(r => r.Completion ?? 0),
                    Cached = group.Sum(r => r.Cached ?? 0),
                    DurationMs = source == UsageSource.Main ? group.Sum(r => r.DurationMs) : WallTimeMs(group),
                    Estimated = group.Any(r => r.Estimated)
                });
            }

            return rows;
        }

        /// <summary>
        /// Time during which at least one of the records was running: the union of their [start, start + duration]
        /// intervals.
        /// </summary>
        internal static long WallTimeMs(IEnumerable<UsageRecord> records)
        {
            var intervals = records
                .Where(r => r.DurationMs > 0)
                .Select(r => new { Start = r.Timestamp, End = r.Timestamp.AddMilliseconds(r.DurationMs) })
                .OrderBy(i => i.Start)
                .ToList();

            long total = 0;
            DateTime? start = null;
            DateTime end = DateTime.MinValue;

            foreach (var interval in intervals)
            {
                if (start.HasValue && interval.Start <= end)
                {
                    if (interval.End > end)
                        end = interval.End;

                    continue;
                }

                if (start.HasValue)
                    total += (long)(end - start.Value).TotalMilliseconds;

                start = interval.Start;
                end = interval.End;
            }

            if (start.HasValue)
                total += (long)(end - start.Value).TotalMilliseconds;

            return total;
        }

        private static List<UsageModelRow> BuildModelRows(IReadOnlyList<UsageRecord> records)
        {
            var rows = new List<UsageModelRow>();

            foreach (var group in records.GroupBy(ModelKey))
            {
                var list = group.ToList();
                long genCompletion = list.Sum(r => r.GenCompletion);
                long genMs = list.Sum(r => r.GenMs);

                rows.Add(new UsageModelRow
                {
                    Model = string.IsNullOrWhiteSpace(list[0].Model) ? UnknownModel : list[0].Model,
                    Provider = list[0].Provider,
                    BaseUrl = list[0].BaseUrl,
                    Prompt = list.Sum(r => r.Prompt ?? 0),
                    Completion = list.Sum(r => r.Completion ?? 0),
                    Cached = list.Sum(r => r.Cached ?? 0),
                    CacheReportedPrompt = list.Where(r => r.Cached.HasValue).Sum(r => r.Prompt ?? 0),
                    TokensPerSecond = genMs > 0 ? Math.Round(genCompletion * 1000d / genMs, 1) : (double?)null,
                    Estimated = list.Any(r => r.Estimated)
                });
            }

            rows.Sort((a, b) => b.Prompt.CompareTo(a.Prompt));

            return rows;
        }

        private static List<UsageDayGroup> BuildDayGroups(IReadOnlyList<UsageRecord> records, TimeZoneInfo localTimeZone)
        {
            var sessions = new List<UsageSessionRow>();

            foreach (var group in records.GroupBy(r => r.SessionId ?? string.Empty))
                sessions.Add(BuildSessionRow(group.ToList()));

            var days = new List<UsageDayGroup>();

            foreach (var group in sessions.GroupBy(s => ToLocal(s.FirstUtc, localTimeZone).Date))
            {
                var sessionsOfDay = group.OrderByDescending(s => s.FirstUtc).ToList();

                days.Add(new UsageDayGroup
                {
                    DateLocal = group.Key,
                    Prompt = sessionsOfDay.Sum(s => s.Prompt),
                    Completion = sessionsOfDay.Sum(s => s.Completion),
                    Sessions = sessionsOfDay
                });
            }

            days.Sort((a, b) => b.DateLocal.CompareTo(a.DateLocal));

            return days;
        }

        private static DateTime ToLocal(DateTime utc, TimeZoneInfo zone)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        }

        private static UsageSessionRow BuildSessionRow(IReadOnlyList<UsageRecord> records)
        {
            var ordered = records.OrderBy(r => r.Timestamp).ToList();

            var primary = ordered
                .Where(r => r.Prompt.HasValue)
                .OrderByDescending(r => r.Prompt.Value)
                .FirstOrDefault() ?? ordered[0];

            return new UsageSessionRow
            {
                SessionId = ordered[0].SessionId,
                Title = ordered.Select(r => r.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)),
                FirstUtc = ordered[0].Timestamp,
                PrimaryModel = primary.Model,
                Provider = primary.Provider,
                BaseUrl = primary.BaseUrl,
                Prompt = ordered.Sum(r => r.Prompt ?? 0),
                Completion = ordered.Sum(r => r.Completion ?? 0),
                Cached = ordered.Sum(r => r.Cached ?? 0),
                Requests = ordered.Count(r => r.Source == UsageSource.Main),
                Failed = ordered.Count(IsFailure),
                Sources = BuildSourceRows(ordered),
                Models = BuildModelRows(ordered)
            };
        }

        private static string ModelKey(UsageRecord record)
        {
            return (record.Provider ?? string.Empty) + KeySeparator
                + (record.BaseUrl ?? string.Empty) + KeySeparator
                + (record.Model ?? string.Empty);
        }
    }
}
