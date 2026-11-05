using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.ChatSessionStream;
using LMLocal.Core.Common;
using LMLocal.Core.Exceptions;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Responses;
using LMLocal.Infrastructure.ModelsConfig;


namespace LMLocal.Application.Chat
{
    /// <summary>
    /// Outcome of a forced compaction performed in the middle of a session.
    /// </summary>
    internal enum CompactionOutcome
    {
        /// <summary>History was replaced; the retry is worth sending.</summary>
        Success,

        /// <summary>Nothing could be compacted; the retry must not be sent.</summary>
        NothingChanged,

        /// <summary>Compaction is not possible (gate disabled, empty summary, summarizer failure).</summary>
        Failed,

        /// <summary>Cancelled by the user.</summary>
        Cancelled
    }

    /// <summary>
    /// Responsible for compacting (summarizing) conversation history.
    /// </summary>
    internal interface IHistoryCompactor
    {
        bool NeedsCompaction();
        Task CompactIfNeededAsync(string modelId, CancellationToken cancellationToken);

        /// <summary>
        /// True when history compaction is enabled in settings.
        /// </summary>
        bool IsCompactionEnabled();

        /// <summary>
        /// Compacts history after a provider reported a context overflow, so the request can be retried.
        /// </summary>
        Task<(CompactionOutcome Outcome, IReadOnlyList<ChatMessage> Messages)> CompactForcedAsync(string modelId, int attempt, CancellationToken ct);

        /// <summary>
        /// Summarizes the given history into a brief text, keeping only user/assistant roles.
        /// </summary>
        Task<string> SummarizeAsync(IReadOnlyList<ChatMessage> history, string modelId, CancellationToken ct);
    }

    internal class HistoryCompactor : IHistoryCompactor
    {
        private const double CompactionTakeRatio = 0.8;
        private const double CompactionThresholdRatio = 0.8;

        /// <summary>
        /// Newest rounds of the current tool cycle that are never compressed below stage A on the first attempt.
        /// </summary>
        private const int KeepRecentRounds = 3;

        /// <summary>
        /// Floor for the summary output budget on the retry.
        /// </summary>
        private const int MinSummaryRetryTokens = 256;

        /// <summary>
        /// System prompt used for every history summarization (both automatic compaction and manual summarize).
        /// </summary>
        private const string SummarySystemPrompt =
            "Summarize this conversation briefly so I can continue it in a new session.\r\n\r\nPreserve key decisions, the main goal, current state, next step, unresolved questions, and important failed approaches.\r\n\r\nCopy all code blocks completely intact, preserving their exact content and formatting. Preserve exact error messages, file paths, commands, and identifiers.\r\n\r\nOutput only the summary; do not continue the conversation.";

        /// <summary>
        /// System prompt for a forced mid-session compaction. Unlike <see cref="SummarySystemPrompt"/> this fragment continues in the same session and must not copy code blocks, otherwise the summary can be as large as its input.
        /// </summary>
        private const string ForcedSummarySystemPrompt =
            "You compress a fragment of a longer conversation that continues in this same session.\r\n" +
            "The fragment below is everything you receive; it is not the whole conversation.\r\n\r\n" +
            "Preserve:\r\n" +
            "- the main goal and essential context;\r\n" +
            "- key decisions and why they were made;\r\n" +
            "- current state and progress, including which files were already created or modified;\r\n" +
            "- failed approaches and why they failed;\r\n" +
            "- the exact next step and unresolved questions.\r\n\r\n" +
            "Rules:\r\n" +
            "- Preserve exact error messages, file paths, commands, and identifiers verbatim.\r\n" +
            "- Do not reproduce large code blocks. Refer to code by file path and symbol name.\r\n" +
            "- Do not invent information. If the next step is not stated, write: \"next step: not stated\".\r\n" +
            "- Keep the summary concise: at most ~1500 words.\r\n" +
            "- Write the summary in the same language as the conversation.\r\n\r\n" +
            "Output only the summary; do not continue the conversation.";

        /// <summary>
        /// User message that carries the forced summary in the compacted history.
        /// </summary>
        private const string ForcedSummaryUserPlaceholder = "Summary of the earlier part of this session:";

        /// <summary>
        /// Reasoning effort sent with a summary call; LM Studio maps it to reasoning "off".
        /// </summary>
        private const string SummaryReasoningEffort = "none";

        /// <summary>
        /// Prompt of the forced summarizer, exposed so the budget can subtract its real size from the summary input.
        /// </summary>
        internal static string ForcedSummaryPromptForBudget
        {
            get { return ForcedSummarySystemPrompt; }
        }


        private readonly IChatHistoryManager _history;
        private readonly IOpenApiAdapter _openApiAdapter;
        private readonly ISettingsManager _settingsManager;
        private readonly IActiveModelContext _activeModelContext;
        private readonly IContextCalibration _calibration;
        private readonly IModelsConfigManager _modelsConfigManager;


        public HistoryCompactor(
            IChatHistoryManager history,
            IOpenApiAdapter openApiAdapter,
            ISettingsManager settingsManager,
            IActiveModelContext activeModelContext,
            IContextCalibration calibration = null,
            IModelsConfigManager modelsConfigManager = null)
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _openApiAdapter = openApiAdapter ?? throw new ArgumentNullException(nameof(openApiAdapter));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _activeModelContext = activeModelContext ?? throw new ArgumentNullException(nameof(activeModelContext));
            _calibration = calibration ?? new ContextCalibration();
            _modelsConfigManager = modelsConfigManager;
        }

        private int GetMaxContext()
        {
            return _activeModelContext.MaxContextLength > 0 ? _activeModelContext.MaxContextLength : 16384;
        }

        public bool NeedsCompaction()
        {
            if (!IsCompactionEnabled())
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
                // The summary continues this same session, so it uses the prompt that refers to code instead of copying it:
                // with the manual prompt a small model copies attached files verbatim until the window runs out.
                string reasoning = await GetSummaryReasoningAsync(modelId, cancellationToken).ConfigureAwait(false);
                var parsedSummary = await SendSummaryAsync(
                    FormatForSummary(toSummarize), modelId, ForcedSummarySystemPrompt, null, reasoning, true, cancellationToken)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(parsedSummary))
                {
                    var recent = snapshot.Skip(toSummarize.Count);
                    var success = await _history.ReplaceHistoryAndPersistAsync(parsedSummary, recent, expectedSize).ConfigureAwait(false);
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

            try
            {
                var parsedSummary = await SummarizeCoreAsync(history, modelId, SummarySystemPrompt, ct).ConfigureAwait(false);

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

        /// <summary>
        /// True when history compaction is enabled in settings.
        /// </summary>
        public bool IsCompactionEnabled()
        {
            return _settingsManager?.Current?.EnableHistoryCompaction ?? false;
        }

        /// <summary>
        /// Compacts history after a provider reported a context overflow.
        /// </summary>
        public async Task<(CompactionOutcome Outcome, IReadOnlyList<ChatMessage> Messages)> CompactForcedAsync(string modelId, int attempt, CancellationToken ct)
        {
            if (!IsCompactionEnabled())
                return (CompactionOutcome.Failed, null);

            try
            {
                ct.ThrowIfCancellationRequested();

                var snapshot = _history.GetHistoryCopy();
                if (snapshot == null || snapshot.Count == 0)
                    return (CompactionOutcome.NothingChanged, null);

                int lastUser = ConversationSegmenter.FindLastUser(snapshot);
                if (lastUser < 0)
                    return (CompactionOutcome.Failed, null);

                var budget = await GetBudgetAsync(modelId, ct).ConfigureAwait(false);
                int historyLimit = budget.HistoryLimit;
                int keepRecentRounds = attempt == 0 ? KeepRecentRounds : 1;

                var pruned = ToolHistoryPruner.Prune(
                    snapshot,
                    new PruneOptions { KeepRecentRounds = keepRecentRounds },
                    messages => budget.Real(EstimateTokens(messages)),
                    historyLimit);

                var compactedSource = pruned.Messages;
                var rounds = ConversationSegmenter.GetCurrentCycleRounds(compactedSource, lastUser);

                bool hasPast = lastUser > 0;

                int minKeepRounds = rounds.Count == 0 ? 0 : 1;
                int maxKeepRounds = Math.Min(keepRecentRounds, rounds.Count);

                if (CollectCompressible(compactedSource, lastUser, ProtectedStart(rounds, minKeepRounds, compactedSource.Count), hasPast).Count == 0)
                {
                    if (pruned.Fits && pruned.PrunedCount > 0)
                    {
                        InternalLogger.Info($"CompactForcedAsync: nothing to summarize; pruned {pruned.PrunedCount} message(s), fits the budget.");
                        return (CompactionOutcome.Success, compactedSource);
                    }

                    InternalLogger.Info("CompactForcedAsync: nothing to summarize and pruning changed nothing.");
                    return (CompactionOutcome.NothingChanged, null);
                }

                for (int keepRounds = maxKeepRounds; keepRounds >= minKeepRounds; keepRounds--)
                {
                    int protectedStart = ProtectedStart(rounds, keepRounds, compactedSource.Count);

                    var compressible = CollectCompressible(compactedSource, lastUser, protectedStart, hasPast);
                    if (compressible.Count == 0)
                        continue;

                    InternalLogger.Info(
                        $"CompactForcedAsync: summarizing {compressible.Count} message(s), {keepRounds} round(s) kept verbatim.");

                    var summary = await SummarizeForcedAsync(compressible, modelId, budget, ct).ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(summary))
                    {
                        InternalLogger.Warn("CompactForcedAsync produced empty summary.");
                        return (CompactionOutcome.Failed, null);
                    }

                    var candidate = BuildCompactedResult(
                        compactedSource, lastUser, protectedStart, hasPast, summary.Trim());

                    if (candidate != null && budget.Real(EstimateTokens(candidate)) <= historyLimit)
                        return (CompactionOutcome.Success, candidate);
                }

                var truncated = new List<ChatMessage> { compactedSource[lastUser] };

                if (rounds.Count > 0)
                    truncated.AddRange(compactedSource.Skip(rounds[rounds.Count - 1].Start));

                if (truncated.Count > 1 && budget.Real(EstimateTokens(truncated)) > historyLimit)
                    truncated = new List<ChatMessage> { compactedSource[lastUser] };

                if (truncated.Count < compactedSource.Count)
                {
                    InternalLogger.Warn(
                        $"CompactForcedAsync: truncated history from {compactedSource.Count} to {truncated.Count} message(s); the current tool cycle was dropped.");
                    return (CompactionOutcome.Success, truncated);
                }

                return (CompactionOutcome.NothingChanged, null);
            }
            catch (OperationCanceledException)
            {
                InternalLogger.Info("CompactForcedAsync cancelled.");
                return (CompactionOutcome.Cancelled, null);
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"CompactForcedAsync failed: {ex.Message}", ex);
                return (CompactionOutcome.Failed, null);
            }
        }

        /// <summary>
        /// First message of the protected window: the newest <paramref name="keepRounds"/> rounds, or a position past the
        /// end of the list when no round is protected, which leaves the whole past as the summary input.
        /// </summary>
        private static int ProtectedStart(IReadOnlyList<ConversationRound> rounds, int keepRounds, int fallbackCount)
        {
            if (keepRounds <= 0 || rounds == null || rounds.Count == 0)
                return fallbackCount;

            return rounds[rounds.Count - keepRounds].Start;
        }

        /// <summary>
        /// Collects the messages that go into the summary: the past before the prompt, plus the rounds of the current cycle that are older than the protected window.
        /// </summary>
        private static List<ChatMessage> CollectCompressible(
            IReadOnlyList<ChatMessage> messages, int lastUser, int protectedStart, bool hasPast)
        {
            var compressible = new List<ChatMessage>();

            if (hasPast)
            {
                for (int i = 0; i < lastUser; i++)
                    compressible.Add(messages[i]);
            }

            for (int i = lastUser + 1; i < protectedStart && i < messages.Count; i++)
                compressible.Add(messages[i]);

            return compressible;
        }

        /// <summary>
        /// Builds the compacted history.
        /// </summary>
        private static List<ChatMessage> BuildCompactedResult(
            IReadOnlyList<ChatMessage> messages, int lastUser, int protectedStart, bool hasPast, string summary)
        {
            var keep = new List<ChatMessage>();
            for (int i = protectedStart; i < messages.Count; i++)
                keep.Add(messages[i]);

            if (hasPast)
            {
                var withPast = new List<ChatMessage>(keep.Count + 3)
                {
                    new ChatMessage("user", ForcedSummaryUserPlaceholder),
                    new ChatMessage("assistant", summary),
                    messages[lastUser]
                };
                withPast.AddRange(keep);
                return withPast;
            }

            int firstAssistant = -1;
            for (int i = 0; i < keep.Count; i++)
            {
                if (keep[i] != null && keep[i].Role == "assistant" && ConversationSegmenter.HasToolCalls(keep[i]))
                {
                    firstAssistant = i;
                    break;
                }
            }

            if (firstAssistant < 0)
                return null;

            var withoutPast = new List<ChatMessage>(keep.Count + 1) { messages[lastUser] };

            for (int i = 0; i < keep.Count; i++)
                withoutPast.Add(i == firstAssistant ? WithProgressSummary(keep[i], summary) : keep[i]);

            return withoutPast;
        }

        /// <summary>
        /// Returns a copy of the message whose content carries the cycle progress in front of whatever text it already had.
        /// </summary>
        private static ChatMessage WithProgressSummary(ChatMessage message, string summary)
        {
            string progress = "Progress so far: " + summary;
            string existing = ContentTextExtractor.ExtractTextContent(message.Content);

            string combined = string.IsNullOrWhiteSpace(existing)
                ? progress
                : progress + "\n\n" + existing.Trim();

            return new ChatMessage(message.Role, combined, message.ToolCallId)
            {
                ToolCalls = message.ToolCalls
            };
        }

        /// <summary>
        /// Builds the sizes of this compaction.
        /// </summary>
        private async Task<ContextBudget> GetBudgetAsync(string modelId, CancellationToken ct)
        {
            int? maxOutputTokens = await GetProfileMaxTokensAsync(modelId, ct).ConfigureAwait(false);

            return ContextBudget.Create(
                _calibration,
                _calibration.ErrorContextLength,
                _activeModelContext?.MaxContextLength,
                GetMaxContext(),
                maxOutputTokens,
                _calibration.FixedOverhead);
        }

        /// <summary>
        /// Output reserve of the model profile of the currently selected provider, or null when there is no profile.
        /// </summary>
        private async Task<int?> GetProfileMaxTokensAsync(string modelId, CancellationToken ct)
        {
            if (_modelsConfigManager == null)
                return null;

            try
            {
                var config = await _modelsConfigManager.GetAsync(ct).ConfigureAwait(false);
                var current = _settingsManager?.Current;

                return ModelProfileLookup.Find(config, modelId, current?.Provider, current?.ProviderId)?.MaxTokens;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("HistoryCompactor: model profile lookup failed: " + ex.Message, ex);
                return null;
            }
        }

        /// <summary>
        /// Reasoning effort for a summary call: "none" when the model profile sets one, so the provider is known to accept
        /// the parameter; null otherwise, leaving the request as it was.
        /// </summary>
        private async Task<string> GetSummaryReasoningAsync(string modelId, CancellationToken ct)
        {
            if (_modelsConfigManager == null)
                return null;

            try
            {
                var config = await _modelsConfigManager.GetAsync(ct).ConfigureAwait(false);
                var current = _settingsManager?.Current;
                var profile = ModelProfileLookup.Find(config, modelId, current?.Provider, current?.ProviderId);

                return string.IsNullOrWhiteSpace(profile?.ReasoningEffort) ? null : SummaryReasoningEffort;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("HistoryCompactor: model profile lookup failed: " + ex.Message, ex);
                return null;
            }
        }

        /// <summary>
        /// Token estimate of a whole conversation: roles, text, and tool call names with their arguments.
        /// </summary>
        private static int EstimateTokens(IReadOnlyList<ChatMessage> messages)
        {
            int total = 0;

            foreach (var message in messages)
            {
                if (message == null)
                    continue;

                var estimator = new StreamedTokenEstimator();
                estimator.Add(message.Role);
                estimator.Add(ContentTextExtractor.ExtractTextContent(message.Content));

                if (message.ToolCalls is List<LMLocal.Infrastructure.LlmApi.Requests.ToolCall> calls)
                {
                    foreach (var call in calls)
                    {
                        if (call?.Function == null)
                            continue;

                        estimator.Add(call.Function.Name);
                        estimator.Add(call.Function.Arguments);
                    }
                }

                total += estimator.EstimatedTokens;
            }

            return total;
        }


        /// <summary>
        /// Summarization core of the manual Summarize: the result becomes a Chat History card, so its prompt and format stay as they are.
        /// </summary>
        private Task<string> SummarizeCoreAsync(IReadOnlyList<ChatMessage> toSummarize, string modelId, string systemPrompt, CancellationToken ct)
        {
            return SendSummaryAsync(FormatForSummary(toSummarize), modelId, systemPrompt, null, null, false, ct);
        }

        /// <summary>
        /// Sends one summarization request and returns the summary text.
        /// </summary>
        private async Task<string> SendSummaryAsync(
            string formattedInput, string modelId, string systemPrompt, int? maxTokens, string reasoning, bool rejectTruncated,
            CancellationToken ct)
        {
            var summaryRequest = new List<ChatMessage>
            {
                new ChatMessage("system", systemPrompt),
                new ChatMessage("user", formattedInput)
            };

            var modelContext = new ModelContext(modelId: modelId, temperature: 0.3, maxOutputTokens: maxTokens, reasoning: reasoning);
            var messageContext = new MessageContext(summaryRequest);

            try
            {
                SendChatResponse response = await _openApiAdapter.SendChatAsync(messageContext, modelContext, ct).ConfigureAwait(false);
                var choice = response?.Choices?.FirstOrDefault(x => x != null);

                if (rejectTruncated && string.Equals(choice?.FinishReason?.Trim(), "length", StringComparison.OrdinalIgnoreCase))
                {
                    InternalLogger.Warn("HistoryCompactor: the summary was cut off by a length stop; discarded.");
                    return null;
                }

                return choice?.Message?.Content?.Trim();
            }
            catch (ApiException ex)
            {
                _calibration.RecordOverflowError(
                    ex.ErrorInfo?.PromptTokens,
                    ex.ErrorInfo?.ContextLength,
                    EnvelopeEstimator.Estimate(summaryRequest, null));

                throw;
            }
        }

        /// <summary>
        /// Summarizes the past during a forced compaction.
        /// </summary>
        private async Task<string> SummarizeForcedAsync(
            IReadOnlyList<ChatMessage> toSummarize, string modelId, ContextBudget budget, CancellationToken ct)
        {
            var (inputLimit, maxTokens) = GetSummaryBudget(budget);
            string reasoning = await GetSummaryReasoningAsync(modelId, ct).ConfigureAwait(false);

            try
            {
                return await SendSummaryAsync(
                    SummaryInputFormatter.Format(toSummarize, inputLimit), modelId, ForcedSummarySystemPrompt, maxTokens, reasoning, true, ct)
                    .ConfigureAwait(false);
            }
            catch (ApiException ex) when (ContextOverflowDetector.IsContextOverflowError(ex))
            {
                int halfLimit = inputLimit / 2;
                int halfTokens = maxTokens / 2;

                if (halfLimit < 1 || halfLimit >= inputLimit || halfTokens < MinSummaryRetryTokens)
                    return null;

                InternalLogger.Warn(
                    $"CompactForcedAsync: the summary call did not fit the window; retrying with {halfLimit} input and {halfTokens} output tokens.");

                return await SendSummaryAsync(
                    SummaryInputFormatter.Format(toSummarize, halfLimit), modelId, ForcedSummarySystemPrompt, halfTokens, reasoning, true, ct)
                    .ConfigureAwait(false);
            }
        }


        /// <summary>
        /// Split of the summary budget, expressed in the estimator's own scale because the formatter works on raw estimates and the coefficient is applied to the limit instead.
        /// </summary>
        private (int InputLimit, int MaxTokens) GetSummaryBudget(ContextBudget budget)
        {
            int rawLimit = budget.Factor > 0
                ? (int)(budget.SummaryInputLimit / budget.Factor)
                : budget.SummaryInputLimit;

            return (Math.Max(1, rawLimit), budget.SummaryOutput);
        }


        private static string FormatForSummary(IReadOnlyList<ChatMessage> messages)
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
