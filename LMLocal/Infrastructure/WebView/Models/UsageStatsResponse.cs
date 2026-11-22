using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LMLocal.Application.Usage;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.WebView.Models
{
    /// <summary>
    /// Response for GetUsageStatsAsync — everything the Usage &amp; Stats dialog renders.
    /// </summary>
    public class UsageStatsResponse
    {
        /// <summary>False when the selection has no records, so the dialog shows its empty state.</summary>
        [JsonProperty("hasData")]
        public bool HasData { get; set; }

        /// <summary>Wire name of the period the numbers belong to.</summary>
        [JsonProperty("period")]
        public string Period { get; set; }

        /// <summary>
        /// False when chat logging is off, so the dialog can tell "nothing recorded" apart from "nothing will be
        /// recorded until the option is turned on".
        /// </summary>
        [JsonProperty("loggingEnabled")]
        public bool LoggingEnabled { get; set; }

        [JsonProperty("totals")]
        public UsageTotalsResponse Totals { get; set; } = new UsageTotalsResponse();

        [JsonProperty("sources")]
        public List<UsageSourceResponse> Sources { get; set; } = new List<UsageSourceResponse>();

        [JsonProperty("models")]
        public List<UsageModelResponse> Models { get; set; } = new List<UsageModelResponse>();

        /// <summary>Sessions grouped by day; empty for the Current tab.</summary>
        [JsonProperty("days")]
        public List<UsageDayResponse> Days { get; set; } = new List<UsageDayResponse>();
    }

    /// <summary>
    /// Summary line of the dialog.
    /// </summary>
    public class UsageTotalsResponse
    {
        [JsonProperty("prompt")]
        public long Prompt { get; set; }

        [JsonProperty("completion")]
        public long Completion { get; set; }

        [JsonProperty("cached")]
        public long Cached { get; set; }

        [JsonProperty("reasoning")]
        public long Reasoning { get; set; }

        /// <summary>User requests only (main records).</summary>
        [JsonProperty("requests")]
        public int Requests { get; set; }

        /// <summary>timeout + incomplete + error; a cancellation is not a failure.</summary>
        [JsonProperty("failed")]
        public int Failed { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }

        /// <summary>Largest context_peak / context_max as a fraction (0..1); null when no record knows the window.</summary>
        [JsonProperty("peakContextPercent", NullValueHandling = NullValueHandling.Ignore)]
        public double? PeakContextPercent { get; set; }

        /// <summary>True when any number came from an estimate; the dialog marks those with "≈".</summary>
        [JsonProperty("estimated")]
        public bool Estimated { get; set; }
    }

    /// <summary>
    /// One BY SOURCE row.
    /// </summary>
    public class UsageSourceResponse
    {
        [JsonProperty("source")]
        public string Source { get; set; }

        [JsonProperty("runs")]
        public int Runs { get; set; }

        [JsonProperty("failed")]
        public int Failed { get; set; }

        [JsonProperty("prompt")]
        public long Prompt { get; set; }

        [JsonProperty("completion")]
        public long Completion { get; set; }

        [JsonProperty("cached")]
        public long Cached { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }

        [JsonProperty("estimated")]
        public bool Estimated { get; set; }
    }

    /// <summary>
    /// One BY MODEL row; the only place the dialog shows a speed.
    /// </summary>
    public class UsageModelResponse
    {
        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("provider")]
        public string Provider { get; set; }

        [JsonProperty("baseUrl")]
        public string BaseUrl { get; set; }

        /// <summary>Caption shown under the model, e.g. "LM Studio" or "LM Studio · 192.168.1.5".</summary>
        [JsonProperty("providerLabel")]
        public string ProviderLabel { get; set; }

        /// <summary>Captions are shown only when the selection mixes providers or addresses.</summary>
        [JsonProperty("showProviderLabel")]
        public bool ShowProviderLabel { get; set; }

        [JsonProperty("prompt")]
        public long Prompt { get; set; }

        [JsonProperty("completion")]
        public long Completion { get; set; }

        [JsonProperty("cached")]
        public long Cached { get; set; }

        /// <summary>
        /// Share of the prompt served from the cache, 0..100, counted only over the requests whose provider reported
        /// a cached count; null when the provider never reports it, so the dialog shows nothing.
        /// </summary>
        [JsonProperty("cachedPercent", NullValueHandling = NullValueHandling.Ignore)]
        public int? CachedPercent { get; set; }

        /// <summary>Summed speed of the row; null when nothing was measured, which the dialog shows as "—".</summary>
        [JsonProperty("tokensPerSecond", NullValueHandling = NullValueHandling.Ignore)]
        public double? TokensPerSecond { get; set; }

        [JsonProperty("estimated")]
        public bool Estimated { get; set; }
    }

    /// <summary>
    /// A local day of the session list.
    /// </summary>
    public class UsageDayResponse
    {
        /// <summary>Local date of the day, yyyy-MM-dd.</summary>
        [JsonProperty("date")]
        public string Date { get; set; }

        [JsonProperty("prompt")]
        public long Prompt { get; set; }

        [JsonProperty("completion")]
        public long Completion { get; set; }

        [JsonProperty("sessions")]
        public List<UsageSessionResponse> Sessions { get; set; } = new List<UsageSessionResponse>();
    }

    /// <summary>
    /// One session card, with the blocks it shows when expanded.
    /// </summary>
    public class UsageSessionResponse
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; }

        /// <summary>First phrase of the session; the dialog shows "Untitled" when it is empty.</summary>
        [JsonProperty("title")]
        public string Title { get; set; }

        /// <summary>Local time the session started, ISO 8601.</summary>
        [JsonProperty("first")]
        public string First { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("provider")]
        public string Provider { get; set; }

        [JsonProperty("baseUrl")]
        public string BaseUrl { get; set; }

        [JsonProperty("prompt")]
        public long Prompt { get; set; }

        [JsonProperty("completion")]
        public long Completion { get; set; }

        [JsonProperty("cached")]
        public long Cached { get; set; }

        [JsonProperty("requests")]
        public int Requests { get; set; }

        [JsonProperty("failed")]
        public int Failed { get; set; }

        [JsonProperty("sources")]
        public List<UsageSourceResponse> Sources { get; set; } = new List<UsageSourceResponse>();

        [JsonProperty("models")]
        public List<UsageModelResponse> Models { get; set; } = new List<UsageModelResponse>();
    }

    /// <summary>
    /// Maps the aggregated stats of the Application layer to the contract the dialog reads.
    /// </summary>
    internal static class UsageStatsMapper
    {
        private const string UnknownModel = "unknown";

        internal static UsageStatsResponse From(UsageStats stats, UsagePeriod period, bool loggingEnabled = true)
        {
            var response = new UsageStatsResponse
            {
                Period = UsagePeriods.ToWireName(period),
                LoggingEnabled = loggingEnabled
            };

            if (stats == null || !stats.HasData)
                return response;

            response.HasData = true;
            response.Totals = ToTotals(stats.Totals);
            response.Sources = ToSources(stats.Sources);
            response.Models = ToModels(stats.Models);

            foreach (UsageDayGroup day in stats.Days ?? new List<UsageDayGroup>())
                response.Days.Add(ToDay(day));

            return response;
        }

        private static UsageTotalsResponse ToTotals(UsageTotals totals)
        {
            if (totals == null)
                return new UsageTotalsResponse();

            return new UsageTotalsResponse
            {
                Prompt = totals.Prompt,
                Completion = totals.Completion,
                Cached = totals.Cached,
                Reasoning = totals.Reasoning,
                Requests = totals.Requests,
                Failed = totals.Failed,
                DurationMs = totals.DurationMs,
                PeakContextPercent = totals.PeakContextPercent,
                Estimated = totals.Estimated
            };
        }

        private static List<UsageSourceResponse> ToSources(List<UsageSourceRow> rows)
        {
            var response = new List<UsageSourceResponse>();

            if (rows == null)
                return response;

            foreach (UsageSourceRow row in rows)
            {
                response.Add(new UsageSourceResponse
                {
                    Source = row.Source.ToString().ToLowerInvariant(),
                    Runs = row.Runs,
                    Failed = row.Failed,
                    Prompt = row.Prompt,
                    Completion = row.Completion,
                    Cached = row.Cached,
                    DurationMs = row.DurationMs,
                    Estimated = row.Estimated
                });
            }

            return response;
        }

        private static List<UsageModelResponse> ToModels(List<UsageModelRow> rows)
        {
            var response = new List<UsageModelResponse>();

            if (rows == null)
                return response;

            // The provider caption is noise when every row comes from the same provider at the same address.
            bool showProviderLabel = rows.Select(r => r.Provider).Distinct().Count() > 1
                || rows.Select(r => r.BaseUrl ?? string.Empty).Distinct().Count() > 1;

            foreach (UsageModelRow row in rows)
            {
                response.Add(new UsageModelResponse
                {
                    Model = string.IsNullOrWhiteSpace(row.Model) ? UnknownModel : row.Model,
                    Provider = row.Provider,
                    BaseUrl = row.BaseUrl,
                    ProviderLabel = BuildProviderLabel(row, rows, showProviderLabel),
                    ShowProviderLabel = showProviderLabel,
                    Prompt = row.Prompt,
                    Completion = row.Completion,
                    Cached = row.Cached,
                    CachedPercent = row.CacheReportedPrompt > 0
                        ? (int?)Math.Round(row.Cached * 100d / row.CacheReportedPrompt, MidpointRounding.AwayFromZero)
                        : null,
                    TokensPerSecond = row.TokensPerSecond,
                    Estimated = row.Estimated
                });
            }

            return response;
        }

        private static UsageDayResponse ToDay(UsageDayGroup day)
        {
            var response = new UsageDayResponse
            {
                Date = day.DateLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Prompt = day.Prompt,
                Completion = day.Completion
            };

            foreach (UsageSessionRow session in day.Sessions ?? new List<UsageSessionRow>())
                response.Sessions.Add(ToSession(session));

            return response;
        }

        private static UsageSessionResponse ToSession(UsageSessionRow session)
        {
            return new UsageSessionResponse
            {
                SessionId = session.SessionId,
                Title = session.Title,
                First = DateTime.SpecifyKind(session.FirstUtc, DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture),
                Model = session.PrimaryModel,
                Provider = session.Provider,
                BaseUrl = session.BaseUrl,
                Prompt = session.Prompt,
                Completion = session.Completion,
                Cached = session.Cached,
                Requests = session.Requests,
                Failed = session.Failed,
                Sources = ToSources(session.Sources),
                Models = ToModels(session.Models)
            };
        }

        /// <summary>
        /// Caption of a model row: the provider name, plus the host only when one provider is reached through several
        /// addresses (two LM Studio instances, for example).
        /// </summary>
        private static string BuildProviderLabel(UsageModelRow row, List<UsageModelRow> all, bool showProviderLabel)
        {
            if (!showProviderLabel)
                return null;

            string name = ProviderDisplayName(row.Provider);
            bool severalAddresses = all
                .Where(r => string.Equals(r.Provider, row.Provider, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.BaseUrl ?? string.Empty)
                .Distinct()
                .Count() > 1;

            if (!severalAddresses)
                return name;

            string host = HostOf(row.BaseUrl);

            return string.IsNullOrEmpty(host) ? name : name + " · " + host;
        }

        private static string HostOf(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                return null;

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri uri))
                return null;

            return uri.Host;
        }

        /// <summary>
        /// Friendly provider name for the caption; an unknown id is shown as it is, capitalised.
        /// </summary>
        internal static string ProviderDisplayName(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
                return string.Empty;

            switch (provider.Trim().ToLowerInvariant())
            {
                case "lmstudio":
                    return "LM Studio";

                case "ollama":
                    return "Ollama";

                case "openai":
                    return "OpenAI";

                case "azureopenai":
                case "azure":
                    return "Azure OpenAI";

                case "deepseek":
                    return "DeepSeek";

                case "anthropic":
                    return "Anthropic";

                case "gemini":
                case "google":
                    return "Gemini";

                case "mistral":
                    return "Mistral";

                case "groq":
                    return "Groq";

                case "openrouter":
                    return "OpenRouter";

                default:
                    string text = provider.Trim();
                    return char.ToUpperInvariant(text[0]) + text.Substring(1);
            }
        }
    }
}
