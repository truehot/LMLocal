using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Responses;


namespace LMLocal.Application.Chat
{
    /// <summary>
    /// Responsible for compacting (summarizing) conversation history.
    /// </summary>
    internal interface IHistoryCompactor
    {
        bool NeedsCompaction();
        Task CompactIfNeededAsync(string modelId, CancellationToken cancellationToken);

        /// <summary>
        /// Summarizes the given history into a brief text, keeping only user/assistant roles.
        /// </summary>
        Task<string> SummarizeAsync(IReadOnlyList<ChatMessage> history, string modelId, CancellationToken ct);
    }

    internal class HistoryCompactor : IHistoryCompactor
    {
        private const double CompactionTakeRatio = 0.8;
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

            int chars = _history.GetHistoryCopy().Sum(m => ContentTextExtractor.ExtractTextLength(m.Content));
            return chars / 4 >= (int)(GetMaxContext() * CompactionThresholdRatio);
        }

        public async Task CompactIfNeededAsync(string modelId, CancellationToken cancellationToken)
        {
            if (!NeedsCompaction())
                return;

            var snapshot = _history.GetHistoryCopy();
            var expectedSize = snapshot.Count;

            int toTake = (int)(snapshot.Count * CompactionTakeRatio);
            if (toTake <= 0) return;
            var toSummarize = snapshot.Take(toTake).ToList();

            if (toSummarize.Count == 0)
                return;

            try
            {
                var summaryRequest = new List<ChatMessage>
                {
                    new ChatMessage("system", "Summarize this conversation briefly, preserving key decisions and full code blocks completely intact so I can continue it later."),
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

        public async Task<string> SummarizeAsync(IReadOnlyList<ChatMessage> history, string modelId, CancellationToken ct)
        {
            if (history == null || history.Count == 0)
                return null;

            var toSummarize = history.ToList();

            try
            {
                var summaryRequest = new List<ChatMessage>
                {
                    new ChatMessage("system", "Summarize the following conversation briefly, preserving key facts, decisions and code details."),
                    new ChatMessage("user", FormatForSummary(toSummarize))
                };

                var modelContext = new ModelContext(modelId: modelId, temperature: 0.3);
                var messageContext = new MessageContext(summaryRequest);

                SendChatResponse response = await _openApiAdapter.SendChatAsync(messageContext, modelContext, ct).ConfigureAwait(false);
                var parsedSummary = response?.Choices?.FirstOrDefault(x => x != null)?.Message?.Content?.Trim();

                if (!string.IsNullOrWhiteSpace(parsedSummary))
                    return parsedSummary;

                InternalLogger.Warn("SummarizeAsync: produced empty summary.");
                return null;
            }
            catch (OperationCanceledException)
            {
                InternalLogger.Info("SummarizeAsync cancelled.");
                return null;
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"SummarizeAsync failed: {ex.Message}", ex);
                return null;
            }
        }

        private static string FormatForSummary(List<ChatMessage> messages)
        {
            var sb = new StringBuilder();
            foreach (var msg in messages)
            {
                if (msg.Role == "tool")
                    continue;
                if (msg.Role == "assistant" && (msg.ToolCalls != null || msg.Content == null))
                    continue;

                sb.AppendLine($"{msg.Role}: {ContentTextExtractor.ExtractTextContent(msg.Content)}");
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
