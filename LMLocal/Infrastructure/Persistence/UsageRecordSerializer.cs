using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LMLocal.Core.Models.Usage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Persistence
{
    /// <summary>
    /// Pure serialization helpers for the usage JSON Lines format: one record per line, monthly files.
    /// </summary>
    internal static class UsageRecordSerializer
    {
        /// <summary>Suffix of a monthly usage file: <c>202611_usage.jsonl</c>.</summary>
        internal const string FileSuffix = "_usage.jsonl";

        /// <summary>Format of <c>ts</c>: UTC, second precision, trailing Z.</summary>
        internal const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        private const string TsField = "ts";
        private const string SessionIdField = "session_id";
        private const string RequestIdField = "request_id";
        private const string SourceField = "source";
        private const string OriginField = "origin";
        private const string TitleField = "title";
        private const string AgentField = "agent";
        private const string RunSessionIdField = "run_session_id";
        private const string PipelineField = "pipeline";
        private const string PipelineRunIdField = "pipeline_run_id";
        private const string PipelineStepField = "pipeline_step";
        private const string PipelineStepIdField = "pipeline_step_id";
        private const string PipelineAttemptField = "pipeline_attempt";
        private const string PipelineBranchField = "pipeline_branch";
        private const string ProviderField = "provider";
        private const string BaseUrlField = "base_url";
        private const string ModelField = "model";
        private const string AppVersionField = "app_version";
        private const string SolutionDirField = "solution_dir";
        private const string PromptField = "prompt";
        private const string CompletionField = "completion";
        private const string CachedField = "cached";
        private const string ReasoningField = "reasoning";
        private const string ReasoningEchoField = "reasoning_echo";
        private const string EstimatedField = "estimated";
        private const string RoundsField = "rounds";
        private const string ToolCallsField = "tool_calls";
        private const string GenCompletionField = "gen_completion";
        private const string GenMsField = "gen_ms";
        private const string DurationMsField = "duration_ms";
        private const string ContextMaxField = "context_max";
        private const string ContextPeakField = "context_peak";
        private const string OutcomeField = "outcome";
        private const string ErrorField = "error";

        /// <summary>
        /// Name of the monthly file a record belongs to: the UTC month of its start time, so a request that crosses
        /// midnight or the end of a month stays in the file of its start.
        /// </summary>
        internal static string BuildFileName(DateTime moment)
        {
            return ToUtc(moment).ToString("yyyyMM", CultureInfo.InvariantCulture) + FileSuffix;
        }

        /// <summary>
        /// Serializes one record as a JSON line.
        /// </summary>
        internal static string BuildLine(UsageRecord record)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            var entry = new Dictionary<string, object>
            {
                { TsField, ToUtc(record.Timestamp).ToString(TimestampFormat, CultureInfo.InvariantCulture) },
                { SessionIdField, record.SessionId },
                { RequestIdField, record.RequestId },
                { SourceField, SourceToString(record.Source) },
                { OriginField, record.Origin },
                { TitleField, record.Title },
                { AgentField, record.Agent },
                { RunSessionIdField, record.RunSessionId },
                { ProviderField, record.Provider },
                { BaseUrlField, record.BaseUrl },
                { ModelField, record.Model },
                { AppVersionField, record.AppVersion },
                { SolutionDirField, record.SolutionDir },
                { PromptField, record.Prompt },
                { CompletionField, record.Completion },
                { CachedField, record.Cached },
                { ReasoningField, record.Reasoning },
                { EstimatedField, record.Estimated },
                { RoundsField, record.Rounds },
                { ToolCallsField, record.ToolCalls },
                { GenCompletionField, record.GenCompletion },
                { GenMsField, record.GenMs },
                { DurationMsField, record.DurationMs },
                { ContextMaxField, record.ContextMax },
                { ContextPeakField, record.ContextPeak },
                { OutcomeField, OutcomeToString(record.Outcome) },
                { ErrorField, record.Error }
            };

            // A pipeline step writes its position; a plain agent run writes none of these fields at all.
            if (!string.IsNullOrEmpty(record.Pipeline))
                entry[PipelineField] = record.Pipeline;
            if (!string.IsNullOrEmpty(record.PipelineRunId))
                entry[PipelineRunIdField] = record.PipelineRunId;
            if (record.PipelineStep.HasValue)
                entry[PipelineStepField] = record.PipelineStep.Value;
            if (!string.IsNullOrEmpty(record.PipelineStepId))
                entry[PipelineStepIdField] = record.PipelineStepId;
            if (record.PipelineAttempt.HasValue)
                entry[PipelineAttemptField] = record.PipelineAttempt.Value;
            if (record.PipelineBranch.HasValue)
                entry[PipelineBranchField] = record.PipelineBranch.Value;

            if (record.ReasoningEcho > 0)
                entry[ReasoningEchoField] = record.ReasoningEcho;

            return JsonConvert.SerializeObject(entry) + Environment.NewLine;
        }

        /// <summary>
        /// Deserializes one line, or null when the line is empty, malformed or has no timestamp.
        /// </summary>
        internal static UsageRecord ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            try
            {
                JObject entry = ParseObject(line);

                if (entry == null)
                    return null;

                DateTime timestamp = ParseUtc(entry.Value<string>(TsField));

                if (timestamp == DateTime.MinValue)
                    return null;

                return new UsageRecord
                {
                    Timestamp = timestamp,
                    SessionId = entry.Value<string>(SessionIdField),
                    RequestId = entry.Value<string>(RequestIdField),
                    Source = ParseSource(entry.Value<string>(SourceField)),
                    Origin = entry.Value<string>(OriginField),
                    Title = entry.Value<string>(TitleField),
                    Agent = entry.Value<string>(AgentField),
                    RunSessionId = entry.Value<string>(RunSessionIdField),
                    Pipeline = entry.Value<string>(PipelineField),
                    PipelineRunId = entry.Value<string>(PipelineRunIdField),
                    PipelineStep = ReadInt(entry, PipelineStepField),
                    PipelineStepId = entry.Value<string>(PipelineStepIdField),
                    PipelineAttempt = ReadInt(entry, PipelineAttemptField),
                    PipelineBranch = ReadInt(entry, PipelineBranchField),
                    Provider = entry.Value<string>(ProviderField),
                    BaseUrl = entry.Value<string>(BaseUrlField),
                    Model = entry.Value<string>(ModelField),
                    AppVersion = entry.Value<string>(AppVersionField),
                    SolutionDir = entry.Value<string>(SolutionDirField),
                    Prompt = ReadInt(entry, PromptField),
                    Completion = ReadInt(entry, CompletionField),
                    Cached = ReadInt(entry, CachedField),
                    Reasoning = ReadInt(entry, ReasoningField),
                    ReasoningEcho = ReadLong(entry, ReasoningEchoField),
                    Estimated = entry.Value<bool?>(EstimatedField) ?? false,
                    Rounds = ReadInt(entry, RoundsField) ?? 0,
                    ToolCalls = ReadInt(entry, ToolCallsField) ?? 0,
                    GenCompletion = ReadLong(entry, GenCompletionField),
                    GenMs = ReadLong(entry, GenMsField),
                    DurationMs = ReadLong(entry, DurationMsField),
                    ContextMax = ReadInt(entry, ContextMaxField),
                    ContextPeak = ReadInt(entry, ContextPeakField),
                    Outcome = ParseOutcome(entry.Value<string>(OutcomeField)),
                    Error = entry.Value<string>(ErrorField)
                };
            }
            catch (Exception ex)
            {
                LMLocal.Core.Common.InternalLogger.Warn("UsageRecordSerializer: skipped a broken usage line", ex);
                return null;
            }
        }

        private static JObject ParseObject(string line)
        {
            using (var reader = new JsonTextReader(new StringReader(line)) { DateParseHandling = DateParseHandling.None })
            {
                return JObject.Load(reader);
            }
        }

        private static int? ReadInt(JObject entry, string field)
        {
            try
            {
                return entry.Value<int?>(field);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static long ReadLong(JObject entry, string field)
        {
            try
            {
                return entry.Value<long?>(field) ?? 0L;
            }
            catch (Exception)
            {
                return 0L;
            }
        }

        private static DateTime ParseUtc(string value)
        {
            if (string.IsNullOrEmpty(value))
                return DateTime.MinValue;

            DateTimeOffset parsed;

            if (DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out parsed))
            {
                return parsed.UtcDateTime;
            }

            return DateTime.MinValue;
        }

        private static DateTime ToUtc(DateTime moment)
        {
            return moment.Kind == DateTimeKind.Utc ? moment : moment.ToUniversalTime();
        }

        internal static string SourceToString(UsageSource source)
        {
            switch (source)
            {
                case UsageSource.Subagent:
                    return "subagent";

                case UsageSource.Compaction:
                    return "compaction";

                default:
                    return "main";
            }
        }

        internal static UsageSource ParseSource(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "subagent":
                    return UsageSource.Subagent;

                case "compaction":
                    return UsageSource.Compaction;

                default:
                    return UsageSource.Main;
            }
        }

        internal static string OutcomeToString(UsageOutcome outcome)
        {
            switch (outcome)
            {
                case UsageOutcome.Cancelled:
                    return "cancelled";

                case UsageOutcome.Timeout:
                    return "timeout";

                case UsageOutcome.Incomplete:
                    return "incomplete";

                case UsageOutcome.Error:
                    return "error";

                default:
                    return "ok";
            }
        }

        internal static UsageOutcome ParseOutcome(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "cancelled":
                    return UsageOutcome.Cancelled;

                case "timeout":
                    return UsageOutcome.Timeout;

                case "incomplete":
                    return UsageOutcome.Incomplete;

                case "error":
                    return UsageOutcome.Error;

                default:
                    return UsageOutcome.Ok;
            }
        }
    }
}
