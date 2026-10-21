using System;
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
    /// Writes SubAgent failures as single JSON Lines appended to
    /// <c>%LOCALAPPDATA%\{LocalAppDataFolder}\SubAgentsLogs\SubAgentsFailures.jsonl</c>.
    /// The prompt is stored in full (never truncated) so a failing run can be replayed.
    /// </summary>
    /// <remarks>
    /// Debug-only: in Release every public method returns a completed task and no file I/O happens.
    /// Writes are serialized process-wide because <see cref="IFileSystem.AppendAllBytesAsync"/> opens
    /// the file with <c>FileShare.None</c>. Logging never throws and never changes a run's outcome.
    /// </remarks>
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
        /// Serializes all writers of the process: <see cref="IFileSystem.AppendAllBytesAsync"/>
        /// uses <c>FileMode.Append</c> + <c>FileShare.None</c>, so parallel writes would collide.
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
            CancellationToken cancellationToken = default)
            => SafeLogAsync(() => BuildFromRequest(request, stage, error, runId, round, chatSessionId, exception));

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
            Exception exception)
        {
            var record = new SubAgentFailureRecord
            {
                Stage = stage,
                Error = error,
                RunId = runId,
                Round = round,
                ChatSessionId = chatSessionId,
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
