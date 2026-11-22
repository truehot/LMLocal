using System;
using System.Collections.Generic;
using LMLocal.Core.Models.Usage;

namespace LMLocal.Application.Usage
{
    /// <summary>
    /// Everything the stats dialog shows for one selection (the current session or a period). Data only.
    /// </summary>
    internal sealed class UsageStats
    {
        /// <summary>False when the selection has no records at all, so the dialog can show its empty state.</summary>
        public bool HasData { get; set; }

        public UsageTotals Totals { get; set; } = new UsageTotals();

        /// <summary>BY SOURCE rows; empty groups are not included.</summary>
        public List<UsageSourceRow> Sources { get; set; } = new List<UsageSourceRow>();

        /// <summary>BY MODEL rows, keyed by provider + address + model, with the group speed.</summary>
        public List<UsageModelRow> Models { get; set; } = new List<UsageModelRow>();

        /// <summary>Sessions grouped by local day, newest day first. Empty for the current session.</summary>
        public List<UsageDayGroup> Days { get; set; } = new List<UsageDayGroup>();
    }

    /// <summary>
    /// The summary line of the dialog: tokens, requests, failures, time and context peak.
    /// </summary>
    internal sealed class UsageTotals
    {
        public long Prompt { get; set; }
        public long Completion { get; set; }
        public long Cached { get; set; }
        public long Reasoning { get; set; }

        /// <summary>User requests only: main records. Subagents and compactions are not requests.</summary>
        public int Requests { get; set; }

        /// <summary>Records that ended with timeout, incomplete or error. A cancellation is not a failure.</summary>
        public int Failed { get; set; }

        /// <summary>Sum of the main records: a subagent runs inside the main request, so its time is already counted.</summary>
        public long DurationMs { get; set; }

        /// <summary>Largest context_peak / context_max among the records that know the context window; null when none do.</summary>
        public double? PeakContextPercent { get; set; }

        /// <summary>True when any number came from an estimate instead of the provider.</summary>
        public bool Estimated { get; set; }
    }

    /// <summary>
    /// One BY SOURCE row.
    /// </summary>
    internal sealed class UsageSourceRow
    {
        public UsageSource Source { get; set; }

        /// <summary>Number of records: runs for a subagent, calls for a compaction, requests for the main chat.</summary>
        public int Runs { get; set; }

        public int Failed { get; set; }

        public long Prompt { get; set; }
        public long Completion { get; set; }
        public long Cached { get; set; }

        /// <summary>
        /// Time of the source.
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>True when any number of the row came from an estimate instead of the provider.</summary>
        public bool Estimated { get; set; }
    }

    /// <summary>
    /// One BY MODEL row. Speed is shown here and nowhere else: elsewhere it would mix models and providers.
    /// </summary>
    internal sealed class UsageModelRow
    {
        public string Model { get; set; }
        public string Provider { get; set; }
        public string BaseUrl { get; set; }

        public long Prompt { get; set; }
        public long Completion { get; set; }
        public long Cached { get; set; }

        /// <summary>
        /// Prompt tokens of the records whose provider reported a cached count; the base of the cache share.
        /// </summary>
        public long CacheReportedPrompt { get; set; }

        /// <summary>Summed generation speed of the group; null when nothing was measured.</summary>
        public double? TokensPerSecond { get; set; }

        public bool Estimated { get; set; }
    }

    /// <summary>
    /// One session card of a period. The detail blocks are precomputed, so expanding a card needs no new IO.
    /// </summary>
    internal sealed class UsageSessionRow
    {
        public string SessionId { get; set; }

        /// <summary>First non-empty title of the session records; null when the session has none.</summary>
        public string Title { get; set; }

        /// <summary>UTC time of the first record: the moment the session started spending tokens.</summary>
        public DateTime FirstUtc { get; set; }

        /// <summary>Model that carried most of the session (largest prompt); null when no model is known.</summary>
        public string PrimaryModel { get; set; }

        public string Provider { get; set; }
        public string BaseUrl { get; set; }

        public long Prompt { get; set; }
        public long Completion { get; set; }
        public long Cached { get; set; }

        /// <summary>Number of user requests of the session.</summary>
        public int Requests { get; set; }

        public int Failed { get; set; }

        public List<UsageSourceRow> Sources { get; set; } = new List<UsageSourceRow>();
        public List<UsageModelRow> Models { get; set; } = new List<UsageModelRow>();
    }

    /// <summary>
    /// A local day with its sessions; the newest day is expanded in the dialog by default.
    /// </summary>
    internal sealed class UsageDayGroup
    {
        /// <summary>Local date (midnight) the sessions of the group started on.</summary>
        public DateTime DateLocal { get; set; }

        public long Prompt { get; set; }
        public long Completion { get; set; }

        public List<UsageSessionRow> Sessions { get; set; } = new List<UsageSessionRow>();
    }
}
