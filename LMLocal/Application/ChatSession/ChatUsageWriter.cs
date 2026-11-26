using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Usage;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Core.Models.Usage;

namespace LMLocal.Application.ChatSession
{
    /// <summary>
    /// Writes one usage record per chat request, whatever its outcome: a stopped or failed request spent tokens too.
    /// </summary>
    internal sealed class ChatUsageWriter
    {
        private readonly IUsageRecorder _usageRecorder;
        private readonly ISettingsManager _settingsManager;
        private readonly IActiveModelContext _activeModelContext;

        public ChatUsageWriter(
            IUsageRecorder usageRecorder,
            ISettingsManager settingsManager,
            IActiveModelContext activeModelContext)
        {
            _usageRecorder = usageRecorder ?? throw new ArgumentNullException(nameof(usageRecorder));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _activeModelContext = activeModelContext ?? throw new ArgumentNullException(nameof(activeModelContext));
        }

        /// <summary>
        /// Writes the record of the request: the summed usage of its rounds, the provider settings it ran with, and the
        /// outcome of the state it terminated in.
        /// </summary>
        public async Task RecordAsync(
            SessionStateContext sessionContext,
            ChatSessionState terminalState,
            long durationMs,
            string modelId)
        {
            if (sessionContext.AllResults.Count == 0)
                return;

            try
            {
                var usage = new UsageAccumulator();
                usage.Add(sessionContext.AllResults);

                UsageOutcomeMapper.MapSessionTerminal(
                    terminalState,
                    sessionContext.AllResults,
                    sessionContext.LastException,
                    sessionContext.LastResult?.ErrorMessage,
                    out UsageOutcome outcome,
                    out string error);

                var settings = _settingsManager.Current;

                var record = new UsageRecord
                {
                    Timestamp = sessionContext.StartedUtc,
                    SessionId = sessionContext.SessionId,
                    RequestId = sessionContext.RequestId,
                    Source = UsageSource.Main,
                    Origin = UsageOrigins.Chat,
                    Title = sessionContext.Title,
                    Provider = settings?.Provider,
                    BaseUrl = settings?.LmStudioBaseUrl,
                    Model = modelId,
                    DurationMs = durationMs,
                    ContextMax = _activeModelContext.MaxContextLength > 0
                        ? _activeModelContext.MaxContextLength
                        : (int?)null,
                    Outcome = outcome,
                    Error = error
                };

                usage.ApplyTo(record);

                await _usageRecorder.AppendAsync(record, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("ChatUsageWriter: failed to record the usage of the request", ex);
            }
        }
    }
}
