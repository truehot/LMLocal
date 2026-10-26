using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.SubAgents;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace LMLocal.Infrastructure.SubAgents
{
    /// <summary>
    /// Writes SubAgent failures as single JSON Lines.
    /// </summary>
    internal sealed class SubAgentFailureLogger : ISubAgentFailureLogger
    {
        internal const string LogsFolderName = "SubAgentsLogs";
        internal const string LogFileName = "SubAgentsFailures.jsonl";
        internal const long DefaultMaxFileBytes = 20L * 1024 * 1024;

        /// <summary>
        /// Number of rotated files kept next to the live log (live + 4 rotated = 5 files).
        /// </summary>
        internal const int MaxRotatedFiles = 4;

        private const int MaxWriteAttempts = 3;
        private const int RetryDelayMs = 40;
        private const string FallbackAppDataFolder = "LMLocalChat";

        /// <summary>
        /// How many tool-call attempts are kept in a failure record.
        /// </summary>
        internal const int MaxRecentCalls = 3;

        /// <summary>
        /// Result budget per attempt; longer results are cut and flagged by <c>resultTruncated</c>.
        /// </summary>
        internal const int MaxRecentCallResultChars = 2048;

        /// <summary>
        /// Arguments budget per attempt. The last attempt (the failing call) always keeps its arguments in full.
        /// </summary>
        internal const int MaxRecentCallArgumentsChars = 2048;

        /// <summary>
        /// Error budget per attempt. Errors are short and are the most valuable part, so the budget is wider.
        /// </summary>
        internal const int MaxRecentCallErrorChars = 8 * 1024;

        /// <summary>
        /// Serializes all writers of the process.
        /// </summary>
        private static readonly SemaphoreSlim WriteGate = new SemaphoreSlim(1, 1);

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc
        };

        private readonly IFileSystem _fileSystem;
        private readonly string _logsFolder;
        private readonly long _maxFileBytes;

        public SubAgentFailureLogger(IFileSystem fileSystem, ISettingsManager settingsManager)
            : this(fileSystem, ResolveLogsFolder(settingsManager), DefaultMaxFileBytes)
        {
        }

        internal SubAgentFailureLogger(IFileSystem fileSystem, string logsFolder, long maxFileBytes = DefaultMaxFileBytes)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

            if (string.IsNullOrWhiteSpace(logsFolder))
                throw new ArgumentNullException(nameof(logsFolder));

            _logsFolder = logsFolder;
            _maxFileBytes = maxFileBytes > 0 ? maxFileBytes : DefaultMaxFileBytes;
        }

        /// <summary>
        /// Absolute path of the live failure log.
        /// </summary>
        internal string LogFilePath => Path.Combine(_logsFolder, LogFileName);

        public Task LogAsync(SubAgentFailureRecord record, CancellationToken cancellationToken = default)
            => SafeLogAsync(() => record);

        public Task LogFromRequestAsync(
            SubAgentRunRequest request,
            string stage,
            string error,
            string runId = null,
            int? round = null,
            Guid? chatSessionId = null,
            Exception exception = null,
            IReadOnlyList<SubAgentRecentCall> recentCalls = null,
            CancellationToken cancellationToken = default)
            => SafeLogAsync(() => BuildFromRequest(request, stage, error, runId, round, chatSessionId, exception, recentCalls));

        public Task LogFromToolCallAsync(
            ToolCallRecord call,
            string stage,
            string error,
            Exception exception = null,
            CancellationToken cancellationToken = default)
            => SafeLogAsync(() => BuildFromToolCall(call, stage, error, exception));

        private Task SafeLogAsync(Func<SubAgentFailureRecord> factory)
        {
#if DEBUG
            return SafeLogCoreAsync(factory);
#else
            // Release: no formatting, no allocations, no file I/O.
            return Task.CompletedTask;
#endif
        }

#if DEBUG
        private async Task SafeLogCoreAsync(Func<SubAgentFailureRecord> factory)
        {
            try
            {
                var record = factory();
                if (record == null)
                    return;

                if (record.TimestampUtc == default)
                    record.TimestampUtc = DateTime.UtcNow;

                await WriteLineAsync(record).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"SubAgentFailureLogger: failed to record failure: {ex.Message}");
            }
        }

        private async Task WriteLineAsync(SubAgentFailureRecord record)
        {
            string line = JsonConvert.SerializeObject(record, JsonSettings) + "\n";
            byte[] data = Utf8NoBom.GetBytes(line);
            string path = LogFilePath;

            await WriteGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                for (int attempt = 1; attempt <= MaxWriteAttempts; attempt++)
                {
                    try
                    {
                        _fileSystem.CreateDirectory(_logsFolder);
                        RotateIfNeeded(path);
                        await _fileSystem.AppendAllBytesAsync(path, data, CancellationToken.None).ConfigureAwait(false);
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (attempt == MaxWriteAttempts)
                        {
                            InternalLogger.Warn(
                                $"SubAgentFailureLogger: could not append to '{path}' after {MaxWriteAttempts} attempts: {ex.Message}");
                            return;
                        }

                        InternalLogger.Warn($"SubAgentFailureLogger: append attempt {attempt} failed for '{path}': {ex.Message}");

                        try
                        {
                            await Task.Delay(RetryDelayMs, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            // Retry delay is best-effort only.
                        }
                    }
                }
            }
            finally
            {
                WriteGate.Release();
            }
        }

        private void RotateIfNeeded(string path)
        {
            if (!_fileSystem.FileExists(path))
                return;

            long length;
            try
            {
                length = _fileSystem.GetFileInfo(path).Length;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"SubAgentFailureLogger: could not read the size of '{path}': {ex.Message}");
                return;
            }

            if (length < _maxFileBytes)
                return;

            // Shift .3 -> .4 ... .1 -> .2, then the live file -> .1 (live + 4 rotated = 5 files).
            for (int index = MaxRotatedFiles - 1; index >= 1; index--)
            {
                string source = RotatedPath(index);
                if (!_fileSystem.FileExists(source))
                    continue;

                string destination = RotatedPath(index + 1);
                if (_fileSystem.FileExists(destination))
                    _fileSystem.Delete(destination);

                _fileSystem.Move(source, destination);
            }

            string first = RotatedPath(1);
            if (_fileSystem.FileExists(first))
                _fileSystem.Delete(first);

            _fileSystem.Move(path, first);

            InternalLogger.Info($"SubAgentFailureLogger: rotated '{path}' ({length} bytes).");
        }

        private string RotatedPath(int index) => Path.Combine(_logsFolder, $"SubAgentsFailures.{index}.jsonl");
#endif

        private static SubAgentFailureRecord BuildFromRequest(
            SubAgentRunRequest request,
            string stage,
            string error,
            string runId,
            int? round,
            Guid? chatSessionId,
            Exception exception,
            IReadOnlyList<SubAgentRecentCall> recentCalls)
        {
            var record = new SubAgentFailureRecord
            {
                Stage = stage,
                Error = error,
                RunId = runId,
                Round = round,
                ChatSessionId = chatSessionId,
                RecentCalls = BuildRecentCalls(recentCalls),
                ExceptionInfo = SubAgentFailureExceptionInfo.From(exception)
            };

            if (request == null)
                return record;

            // Explicit allow-list: ApiKey / CustomApiKey are deliberately never copied.
            record.AgentName = request.AgentName;
            record.Tool = request.AgentName;
            record.Prompt = request.Prompt;
            record.System = request.System;
            record.Model = request.Model;
            record.ProviderType = request.ProviderType;
            record.BaseUrl = request.BaseUrl;
            record.Temperature = request.Temperature;
            record.ReasoningEffort = request.ReasoningEffort;
            record.MaxTokens = request.MaxTokens;
            record.MaxRounds = request.MaxRounds;
            record.TimeoutSeconds = request.TimeoutSeconds;
            record.AllowedTools = request.AllowedTools;
            record.ExcludedAgentNames = request.ExcludedAgentNames;
            record.PromptChars = request.Prompt?.Length ?? 0;
            record.PromptTruncated = false;

            return record;
        }

        private static SubAgentFailureRecord BuildFromToolCall(
            ToolCallRecord call,
            string stage,
            string error,
            Exception exception)
        {
            string prompt = TryExtractPrompt(call?.ArgumentsJson);

            return new SubAgentFailureRecord
            {
                Stage = stage,
                Error = error,
                CallId = call?.CallId,
                AgentName = call?.FunctionName,
                Tool = call?.FunctionName,
                RawArgumentsJson = call?.ArgumentsJson,
                Prompt = prompt,
                PromptChars = prompt?.Length ?? 0,
                PromptTruncated = false,
                ExceptionInfo = SubAgentFailureExceptionInfo.From(exception)
            };
        }

        /// <summary>
        /// Reads the <c>task</c> argument (the SubAgent prompt) out of the raw tool-call arguments.
        /// </summary>
        internal static string TryExtractPrompt(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
                return null;

            try
            {
                var arguments = JObject.Parse(argumentsJson);
                return arguments.Value<string>(SubAgentsToolSource.TaskParam);
            }
            catch (Exception)
            {
                // Invalid JSON: the raw arguments are still recorded as-is.
                return null;
            }
        }

        /// <summary>
        /// Applies the record budgets.
        /// </summary>
        private static IReadOnlyList<SubAgentRecentCall> BuildRecentCalls(IReadOnlyList<SubAgentRecentCall> calls)
        {
            if (calls == null || calls.Count == 0)
                return null;

            int first = Math.Max(0, calls.Count - MaxRecentCalls);
            var kept = new List<SubAgentRecentCall>(calls.Count - first);

            for (int index = first; index < calls.Count; index++)
            {
                var source = calls[index];
                if (source == null)
                    continue;

                bool isFailingCall = index == calls.Count - 1;

                string arguments = TruncateText(
                    source.ArgumentsJson,
                    isFailingCall ? 0 : MaxRecentCallArgumentsChars,
                    out bool argumentsTruncated);

                string resultText = TruncateText(source.ResultText, MaxRecentCallResultChars, out bool resultTruncated);

                kept.Add(new SubAgentRecentCall
                {
                    ToolName = source.ToolName,
                    CallId = source.CallId,
                    Round = source.Round,
                    Step = source.Step,
                    ArgumentsJson = arguments,
                    ArgumentsChars = source.ArgumentsJson?.Length,
                    ArgumentsTruncated = source.ArgumentsJson == null ? (bool?)null : argumentsTruncated,
                    ResultText = resultText,
                    ResultChars = source.ResultText?.Length,
                    ResultTruncated = source.ResultText == null ? (bool?)null : resultTruncated,
                    Error = TruncateText(source.Error, MaxRecentCallErrorChars, out _)
                });
            }

            return kept.Count > 0 ? kept : null;
        }

        /// <summary>
        /// Cuts <paramref name="text"/> to <paramref name="maxChars"/>; a non-positive budget means "no limit".
        /// </summary>
        private static string TruncateText(string text, int maxChars, out bool truncated)
        {
            truncated = false;

            if (text == null || maxChars <= 0 || text.Length <= maxChars)
                return text;

            truncated = true;
            return text.Substring(0, maxChars);
        }

        /// <summary>
        /// Renders a tool result as text.
        /// </summary>
        internal static string StringifyResult(object result)
        {
            if (result == null)
                return null;

            if (result is string text)
                return text;

            try
            {
                return JsonConvert.SerializeObject(result, JsonSettings);
            }
            catch (Exception)
            {
                // Best effort: a hostile result must never break the failure path.
                return result.ToString();
            }
        }

        private static string ResolveLogsFolder(ISettingsManager settingsManager)
        {
            string root = string.IsNullOrWhiteSpace(settingsManager?.LocalAppDataFolder)
                ? FallbackAppDataFolder
                : settingsManager.LocalAppDataFolder;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                root,
                LogsFolderName);
        }
    }
}
