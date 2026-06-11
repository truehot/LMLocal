using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Responses;
using LMLocal.Infrastructure.Settings;


namespace LMLocal.Application.Chat
{
    /// <summary>
    /// Responsible for compacting (summarizing) conversation history when it exceeds a certain size threshold.
    /// </summary>
    internal interface IHistoryCompactor
    {
        bool NeedsCompaction();
        Task CompactIfNeededAsync(string modelId, CancellationToken cancellationToken);
    }

    internal class HistoryCompactor : IHistoryCompactor
    {
        private const int KeepRecentMessages = 6;
        private const double CompactionThresholdRatio = 0.8;

        private readonly IChatHistoryManager _history;
        private readonly IOpenApiAdapter _openApiAdapter;
        private readonly ISettingsManager _settingsManager;
        private readonly IActiveModelContext _activeModelContext;

        public HistoryCompactor(IChatHistoryManager history, IOpenApiAdapter openApiAdapter, ISettingsManager settingsManager, IActiveModelContext activeModelContext)
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _openApiAdapter = openApiAdapter ?? throw new ArgumentNullException(nameof(openApiAdapter));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _activeModelContext = activeModelContext ?? throw new ArgumentNullException(nameof(activeModelContext));
        }

        private int GetMaxContext()
        {
            return _activeModelContext.MaxContextLength > 0 ? _activeModelContext.MaxContextLength : 16384;
        }

        public bool NeedsCompaction()
        {
            bool enabled = _settingsManager?.Current?.EnableHistoryCompaction ?? false;
            if (!enabled)
                return false;

            int chars = _history.GetHistoryCopy().Sum(m => m.Content?.ToString()?.Length ?? 0); ;
            return chars / 4 >= (int)(GetMaxContext() * CompactionThresholdRatio);
        }

        public async Task CompactIfNeededAsync(string modelId, CancellationToken cancellationToken)
        {
            if (!NeedsCompaction())
                return;

            var snapshot = _history.GetHistoryCopy();
            var expectedSize = snapshot.Count;

            int toTake = snapshot.Count - KeepRecentMessages;
            if (toTake <= 0) return;
            var toSummarize = snapshot.Take(toTake).ToList();

            if (toSummarize.Count == 0)
                return;

            try
            {
                var summaryRequest = new List<ChatMessage>
                {
                    new ChatMessage("system", "Summarize the following conversation briefly, preserving key facts, decisions and code details."),
                    new ChatMessage("user", FormatForSummary(toSummarize))
                };

                var modelContext = new ModelContext(modelId: modelId, temperature: 0.3);
                var messageContext = new MessageContext(summaryRequest);

                SendChatResponse response = await _openApiAdapter.SendChatAsync(messageContext, modelContext, cancellationToken).ConfigureAwait(false);
                if (response != null)
                {
                    var parsedSummary = response?.Choices?.FirstOrDefault(x => x != null)?.Message?.Content?.Trim();

                    if (!string.IsNullOrWhiteSpace(parsedSummary))
                    {
                        var recent = snapshot.Skip(toSummarize.Count);
                        var success = _history.ReplaceHistory(parsedSummary, recent, expectedSize);
                        if (!success)
                        {
                            InternalLogger.Debug("History size changed during compaction, skipping replace.");
                        }
                    }
                    else
                    {
                        InternalLogger.Warn("Compaction produced empty summary, skipping history replacement.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                InternalLogger.Info("History compaction cancelled.");
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"History compaction failed: {ex.Message}", ex);
            }
        }

        private string FormatForSummary(List<ChatMessage> messages)
        {
            var sb = new StringBuilder();
            foreach (var msg in messages)
            {
                string content = msg.Content?.ToString() ?? "[tool call]";
                sb.AppendLine($"{msg.Role}: {content}");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
