using System;
using System.Collections.Generic;
using System.Linq;
using LMLocal.Core.Common;
using LMLocal.Core.Models;

namespace LMLocal.Application.Chat
{
    /// <summary>
    /// Projects raw, already-parsed chat logs into the lightweight session summaries shown in the chat history
    /// dialog.
    /// </summary>
    internal static class ChatSessionSummaryBuilder
    {
        private const string UserRole = "user";
        private const string AssistantRole = "assistant";

        /// <summary>
        /// Builds summaries of the sessions with the most recent activity, newest first, at most
        /// <paramref name="limit"/> of them. Logs without entries are skipped.
        /// </summary>
        public static List<ChatSessionSummary> Build(
            IReadOnlyList<ChatSessionLog> logs,
            int limit = ChatLogConstants.DefaultSessionListLimit)
        {
            if (logs == null || logs.Count == 0 || limit <= 0)
                return new List<ChatSessionSummary>();

            var summaries = new List<ChatSessionSummary>(logs.Count);

            foreach (var log in logs)
            {
                var summary = BuildOne(log);
                if (summary != null)
                    summaries.Add(summary);
            }

            return summaries
                .OrderByDescending(summary => summary.Timestamp, StringComparer.Ordinal)
                .ThenByDescending(summary => summary.SessionId, StringComparer.Ordinal)
                .Take(limit)
                .ToList();
        }

        /// <summary>
        /// Builds one summary from a session whose entries are already in chronological order, so the model list is
        /// a plain dedupe and needs no timestamp bookkeeping.
        /// </summary>
        private static ChatSessionSummary BuildOne(ChatSessionLog log)
        {
            if (log?.Entries == null || log.Entries.Count == 0)
                return null;

            string prompt = string.Empty;
            string servicePrompt = string.Empty;
            DateTime lastActivity = DateTime.MinValue;
            var models = new List<string>();
            var seenModels = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in log.Entries)
            {
                if (entry?.Message == null)
                    continue;

                if (entry.TimestampUtc > lastActivity)
                    lastActivity = entry.TimestampUtc;

                string role = entry.Message.Role ?? string.Empty;

                if (string.Equals(role, UserRole, StringComparison.OrdinalIgnoreCase))
                {
                    string text = ContentTextExtractor.ExtractTextContent(entry.Message.Content).Trim();

                    if (text.Length == 0)
                        continue;

                    if (IsServicePromptText(text))
                        servicePrompt = text;
                    else
                        prompt = text;
                }
                else if (string.Equals(role, AssistantRole, StringComparison.OrdinalIgnoreCase))
                {
                    string modelId = entry.Message.ModelId;
                    if (!string.IsNullOrWhiteSpace(modelId) && seenModels.Add(modelId))
                        models.Add(modelId);
                }
            }

            return new ChatSessionSummary
            {
                SessionId = log.SessionId ?? string.Empty,
                Prompt = PromptText.TruncatePrompt(prompt.Length > 0 ? prompt : servicePrompt),
                Timestamp = lastActivity == DateTime.MinValue ? string.Empty : lastActivity.ToString("o"),
                MessageCount = log.Entries.Count,
                Models = models
            };
        }

        /// <summary>
        /// True for the texts the app writes instead of the user's own words: the summary request that starts a
        /// compacted session and the placeholder that carries a forced mid-session summary
        /// (<see cref="ChatMessagePlaceholders"/>).
        /// </summary>
        private static bool IsServicePromptText(string text)
        {
            return text == ChatMessagePlaceholders.SummaryRequest
                || text == ChatMessagePlaceholders.ForcedSummaryUser;
        }
    }
}
