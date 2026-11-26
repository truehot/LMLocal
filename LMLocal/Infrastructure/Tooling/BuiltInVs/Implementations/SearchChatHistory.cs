using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using Microsoft.VisualStudio.Threading;
using static LMLocal.Core.Common.Pluralizer;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Deterministic search over past chat-history sessions.
    /// </summary>
    internal interface ISearchChatHistory : IBuiltInTool
    {
    }

    internal class SearchChatHistory : ISearchChatHistory
    {
        private const int DefaultMaxResults = 3;
        private const int MaxMaxResults = 5;

        private const string AllScope = "all";
        private const string CurrentSolutionScope = "current_solution";

        private const string SessionsCacheKey = "chat_history_sessions|v2";

        private const string CachePartition = "";

        /// <summary>
        /// Upper bound on the number of sessions projected and scored per search.
        /// </summary>
        internal const int MaxSessionsToScan = 1000;

        private readonly IChatPersistenceService _persistence;
        private readonly ISearchResultCache _searchCache;
        private readonly ISettingsManager _settingsManager;

        public string ToolName => "search_chat_history";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

        public SearchChatHistory(IChatPersistenceService persistence, ISearchResultCache searchCache, ISettingsManager settingsManager)
        {
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Searches archived, completed chat sessions from previous conversations. Use this tool when the current request may depend on something discussed in an earlier conversation, especially when the user refers to previous work, decisions, code, configuration, tests, prompts, or asks to continue, recover, verify, or recall something from before. It is also useful for checking whether the current request is a continuation of an earlier conversation. Search when the context is unclear or may have been lost because the current chat was cleared or restarted. The current active session is never returned. Prefer specific keyword phrases, technical terms, identifiers, or topics over full sentences. Whole-phrase or all-words matches come first, newest first; then partial matches, most matched words first. Each result gives the user question of the matched turn, the opening of its final answer (where the conclusion is), a fragment around the match when it lies deeper in the text, and the files the turn was about. Code blocks and files inserted into a message are not returned and are not searched. Results that carry matched_terms are weaker matches: only some of the query words were found. If the needed information is not found in past sessions, do not guess. Independent: several calls can go in one response.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "query", new ToolDetails { Type = "string", Description = "A short keyword phrase, technical term, identifier, feature name, or topic to search for in past chat history. Prefer specific terms over full sentences." } },
                        { "max_results", new ToolDetails { Type = "integer", Description = "Maximum number of matching turns to return. Default 3, maximum 5. Newest matches come first." } }
                    },
                    Required = new List<string> { "query" }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            var (query, scope, maxResults, error) = ExtractAndValidateParameters(parameters);
            if (error != null)
                return Failure(error);

            string cacheKey = BuildCacheKey(query, scope);
            if (_searchCache.TryGet(cacheKey, CachePartition, out CachedToolResults<ChatHistoryMatchResult> cachedResults)
                && cachedResults?.AllResults != null)
            {
                var cachedPage = cachedResults.AllResults.Take(maxResults).ToList();
                string cachedNote = BuildNote((cachedResults as ChatHistoryCachedResults)?.MissingTerms, cachedPage);

                return Success(cachedPage, cachedResults.AllResults.Count, ChatHistorySearchOutcome.Success, cachedNote);
            }

            await TaskScheduler.Default;

            try
            {
                var sessions = await GetSessionsAsync(cancellationToken).ConfigureAwait(false);
                if (sessions.Count == 0)
                    return Success(new List<ChatHistoryMatchResult>(), 0, ChatHistorySearchOutcome.NoFiles);

                var preparedQuery = ChatHistoryQuery.Create(query);
                var foundTerms = new bool[preparedQuery.PartialTerms.Length];

                var matches = new List<ChatHistoryMatchResult>();

                foreach (var session in sessions)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var score = ChatHistoryScorer.Score(session, preparedQuery, foundTerms);
                    if (!score.IsMatch)
                        continue;

                    matches.Add(BuildResult(session, score));
                }

                if (matches.Count == 0)
                    return Success(new List<ChatHistoryMatchResult>(), 0, ChatHistorySearchOutcome.NoMatches);

                matches.Sort(CompareMatches);
                matches = CollapseDuplicates(matches);
                matches = KeepBestPartialResults(matches);

                var top = matches.Take(maxResults).ToList();
                string[] missingTerms = MissingTerms(preparedQuery.PartialTerms, foundTerms);

                _searchCache.Set(cacheKey, CachePartition, new ChatHistoryCachedResults
                {
                    AllResults = matches,
                    MissingTerms = missingTerms
                });

                return Success(top, matches.Count, ChatHistorySearchOutcome.Success, BuildNote(missingTerms, top));
            }
            catch (OperationCanceledException)
            {
                return Failure("Operation was cancelled.");
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("search_chat_history: unexpected error", ex);
                return Failure(ex.Message);
            }
        }

        /// <summary>
        /// Returns the parsed and projected sessions, from the session cache when available, otherwise via a single read-only scan of the chat logs.
        /// </summary>
        private async Task<List<ChatHistorySession>> GetSessionsAsync(CancellationToken cancellationToken)
        {
            if (_searchCache.TryGet(SessionsCacheKey, CachePartition, out CachedToolResults<ChatHistorySession> cachedSessions)
                && cachedSessions?.AllResults != null)
            {
                return cachedSessions.AllResults;
            }

            var logs = await _persistence.ReadAllSessionsAsync(cancellationToken).ConfigureAwait(false);

            string currentSessionId = _persistence.CurrentSessionId?.ToString();
            var sessions = new List<ChatHistorySession>(logs.Count);

            foreach (var log in logs)
            {
                if (log?.Entries == null || log.Entries.Count == 0)
                    continue;

                if (currentSessionId != null && string.Equals(log.SessionId, currentSessionId, StringComparison.Ordinal))
                    continue;

                sessions.Add(ChatHistoryScorer.ProjectSession(log.SessionId, log.Entries));
            }

            if (sessions.Count > MaxSessionsToScan)
            {
                sessions.Sort((a, b) =>
                {
                    int c = b.TimestampUtc.CompareTo(a.TimestampUtc);
                    return c != 0 ? c : string.CompareOrdinal(a.SessionId, b.SessionId);
                });

                sessions = sessions.Take(MaxSessionsToScan).ToList();
            }

            _searchCache.Set(SessionsCacheKey, CachePartition, new CachedToolResults<ChatHistorySession>
            {
                AllResults = sessions
            });

            return sessions;
        }

        private static ChatHistoryMatchResult BuildResult(ChatHistorySession session, ChatHistoryScoreResult score)
        {
            var turn = session.Turns[score.TurnIndex];

            return new ChatHistoryMatchResult
            {
                Kind = score.Kind,
                TimestampUtc = turn.TimestampUtc,
                SessionId = session.SessionId,
                TurnIndex = score.TurnIndex,
                Timestamp = turn.TimestampUtc == DateTime.MinValue ? null : turn.TimestampUtc.ToString("o"),
                MatchedTerms = score.MatchedTerms == null ? null : new List<string>(score.MatchedTerms),
                Question = TruncateForPresentation(turn.Question, ChatHistoryScorer.MaxHeadingLength, "question"),
                AnswerStart = TruncateForPresentation(turn.Answer, ChatHistoryScorer.MaxAnswerStartLength, "answer_start"),
                Snippet = BuildSnippetText(turn, score),
                Files = CollectFiles(session, turn),
                LastAnswerStart = LastAnswerStart(session, score.TurnIndex)
            };
        }

        /// <summary>
        /// Keeps the fragment around the match only when it adds something: a match wholly inside the returned question or
        /// the answer's opening is already visible there, and one inside the file names has no readable context.
        /// </summary>
        private static string BuildSnippetText(ChatHistoryTurn turn, ChatHistoryScoreResult score)
        {
            if (string.IsNullOrEmpty(score.Snippet) || score.MatchIndex < 0)
                return null;

            bool inQuestion = string.Equals(score.Role, "user", StringComparison.Ordinal);
            string content = inQuestion ? (turn.Question ?? string.Empty) : (turn.Answer ?? string.Empty);
            int shownLength = inQuestion ? ChatHistoryScorer.MaxHeadingLength : ChatHistoryScorer.MaxAnswerStartLength;

            // A match cut by the truncation is only partly visible, so the fragment is still returned.
            int matchEnd = score.MatchIndex + Math.Max(score.MatchLength, 1);
            return matchEnd <= Math.Min(content.Length, shownLength) ? null : score.Snippet;
        }

        /// <summary>
        /// Files the turn was about: the ones inserted into the session's first question (the topic) plus the matched
        /// turn's own, without duplicates and capped.
        /// </summary>
        private static List<string> CollectFiles(ChatHistorySession session, ChatHistoryTurn turn)
        {
            var files = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (session.Turns.Count > 0)
                AppendFiles(session.Turns[0].QuestionFiles, files, seen);

            AppendFiles(turn.QuestionFiles, files, seen);
            AppendFiles(turn.AnswerFiles, files, seen);

            if (files.Count > ChatHistoryScorer.MaxFilesInResult)
                files = files.Take(ChatHistoryScorer.MaxFilesInResult).ToList();

            return files.Count == 0 ? null : files;
        }

        private static void AppendFiles(List<string> source, List<string> files, HashSet<string> seen)
        {
            if (source == null)
                return;

            foreach (string path in source)
            {
                if (string.IsNullOrEmpty(path) || !seen.Add(path))
                    continue;

                files.Add(path);
            }
        }

        /// <summary>
        /// Opening of the session's last answer, when the matched turn is not that answer: a later turn ("продолжи")
        /// holds the outcome of the work the earlier match started.
        /// </summary>
        private static string LastAnswerStart(ChatHistorySession session, int turnIndex)
        {
            for (int i = session.Turns.Count - 1; i >= 0; i--)
            {
                if (string.IsNullOrWhiteSpace(session.Turns[i].Answer))
                    continue;

                return i == turnIndex
                    ? null
                    : TruncateForPresentation(session.Turns[i].Answer, ChatHistoryScorer.MaxAnswerStartLength, "last_answer_start");
            }

            return null;
        }

        /// <summary>
        /// Truncates a field for presentation; an empty value is left out of the response entirely.
        /// </summary>
        private static string TruncateForPresentation(string text, int maxLength, string field)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string truncated = PromptText.Truncate(text, maxLength);

            if (text.Length > maxLength)
                InternalLogger.Debug($"search_chat_history: truncated {field} from {text.Length} to {truncated.Length} chars.");

            return truncated;
        }

        /// <summary>
        /// Strong matches first (newest first inside each group), then weak ones: a newer turn supersedes an older
        /// discussion, so recency outranks the number of occurrences. Weak results are ordered by how many query words
        /// they matched, and only then by recency.
        /// </summary>
        private static int CompareMatches(ChatHistoryMatchResult a, ChatHistoryMatchResult b)
        {
            int c = WeakRank(a.Kind).CompareTo(WeakRank(b.Kind));
            if (c != 0) return c;

            c = MatchedWordCount(b).CompareTo(MatchedWordCount(a));
            if (c != 0) return c;

            c = b.TimestampUtc.CompareTo(a.TimestampUtc);
            if (c != 0) return c;

            c = string.CompareOrdinal(a.SessionId, b.SessionId);
            if (c != 0) return c;

            return a.TurnIndex.CompareTo(b.TurnIndex);
        }

        private static int WeakRank(ChatHistoryMatchKind kind)
        {
            return kind == ChatHistoryMatchKind.TurnPartial ? 1 : 0;
        }

        /// <summary>
        /// Query words a result matched; 0 for strong results, which carry no matched_terms.
        /// </summary>
        private static int MatchedWordCount(ChatHistoryMatchResult result)
        {
            return result.MatchedTerms?.Count ?? 0;
        }

        /// <summary>
        /// Collapses results with the same question and answer into the newest one, counting how many sessions it stands for.
        /// </summary>
        private static List<ChatHistoryMatchResult> CollapseDuplicates(List<ChatHistoryMatchResult> sorted)
        {
            var kept = new List<ChatHistoryMatchResult>(sorted.Count);
            var byContent = new Dictionary<string, ChatHistoryMatchResult>(StringComparer.Ordinal);

            foreach (var match in sorted)
            {
                string key = (match.Question ?? string.Empty) + "\u0000" + (match.AnswerStart ?? string.Empty);

                if (byContent.TryGetValue(key, out ChatHistoryMatchResult existing))
                {
                    existing.SimilarSessions = (existing.SimilarSessions ?? 1) + 1;
                    continue;
                }

                byContent.Add(key, match);
                kept.Add(match);
            }

            return kept;
        }

        /// <summary>
        /// Keeps the first <see cref="KnowledgePartialMatcher.MaxResults"/> weak results and drops the rest.
        /// </summary>
        private static List<ChatHistoryMatchResult> KeepBestPartialResults(List<ChatHistoryMatchResult> sorted)
        {
            var kept = new List<ChatHistoryMatchResult>(sorted.Count);
            int partialCount = 0;

            foreach (var match in sorted)
            {
                if (match.Kind == ChatHistoryMatchKind.TurnPartial && ++partialCount > KnowledgePartialMatcher.MaxResults)
                    continue;

                kept.Add(match);
            }

            return kept;
        }

        /// <summary>
        /// Significant query words found in no session, kept in the cache so the note can be built for any returned page.
        /// </summary>
        private static string[] MissingTerms(string[] partialTerms, bool[] foundTerms)
        {
            if (partialTerms.Length == 0)
                return Array.Empty<string>();

            var missing = new List<string>();
            for (int i = 0; i < partialTerms.Length; i++)
            {
                if (foundTerms == null || i >= foundTerms.Length || !foundTerms[i])
                    missing.Add(partialTerms[i]);
            }

            return missing.ToArray();
        }

        /// <summary>
        /// Note for the page being returned; a page without matched_terms gets none, so a strong page stays clean.
        /// </summary>
        private static string BuildNote(string[] missingTerms, List<ChatHistoryMatchResult> page)
        {
            if (missingTerms == null)
                return null;

            foreach (var result in page)
            {
                if (result.MatchedTerms != null)
                    return KnowledgePartialMatcher.BuildNoteForMissing(missingTerms, "session");
            }

            return null;
        }

        private static string BuildCacheKey(string query, string scope)
        {
            return $"search_chat|{scope}|{query}";
        }

        private (string query, string scope, int maxResults, string error) ExtractAndValidateParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, AllScope, DefaultMaxResults, "Parameters cannot be null.");

            if (!parameters.TryGetValue("query", out object queryObj) || !(queryObj is string queryStr))
                return (null, AllScope, DefaultMaxResults, "Parameter 'query' is required and must be a string.");

            queryStr = queryStr.Trim();
            if (queryStr.Length == 0)
                return (null, AllScope, DefaultMaxResults, "Parameter 'query' must be a non-empty string.");

            string scope = AllScope;
            if (parameters.TryGetValue("scope", out object scopeObj) && scopeObj is string scopeStr && scopeStr.Trim().Length > 0)
            {
                scopeStr = scopeStr.Trim();
                if (string.Equals(scopeStr, AllScope, StringComparison.OrdinalIgnoreCase))
                    scope = AllScope;
                else if (string.Equals(scopeStr, CurrentSolutionScope, StringComparison.OrdinalIgnoreCase))
                    scope = CurrentSolutionScope;
                else
                    return (null, AllScope, DefaultMaxResults, $"Parameter 'scope' must be '{AllScope}' or '{CurrentSolutionScope}'.");
            }

            int maxResults = DefaultMaxResults;
            if (parameters.TryGetValue("max_results", out object maxObj) && maxObj != null && int.TryParse(maxObj.ToString(), out int max))
                maxResults = Math.Min(Math.Max(max, 1), MaxMaxResults);

            return (queryStr, scope, maxResults, null);
        }

        private static ChatHistorySearchResponse Failure(string message)
        {
            return new ChatHistorySearchResponse
            {
                Success = false,
                ErrorMessage = message,
                Results = new List<ChatHistoryMatchResult>(),
                TotalMatches = 0,
                Outcome = ChatHistorySearchOutcome.None
            };
        }

        private static ChatHistorySearchResponse Success(
            List<ChatHistoryMatchResult> results,
            int totalMatches,
            ChatHistorySearchOutcome outcome = ChatHistorySearchOutcome.Success,
            string note = null)
        {
            return new ChatHistorySearchResponse
            {
                Success = true,
                Results = results,
                TotalMatches = totalMatches,
                Outcome = outcome,
                Note = note
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var query = parameters != null && parameters.TryGetValue("query", out var q) ? q?.ToString() : "";
            return $"Searching chat history for '{query}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is ChatHistorySearchResponse resp)
            {
                if (!resp.Success)
                    return $"Searching chat history failed: {resp.ErrorMessage}";

                switch (resp.Outcome)
                {
                    case ChatHistorySearchOutcome.NoFiles:
                        return _settingsManager?.Current?.EnableChatLogging == false
                            ? "Chat logging is disabled in settings, so no past chat history is available to search."
                            : "No chat history was found.";
                    case ChatHistorySearchOutcome.NoMatches:
                        return "No occurrences of the query were found in past chat sessions.";
                    case ChatHistorySearchOutcome.Success:
                        return $"Found {resp.TotalMatches} {Pluralize(resp.TotalMatches, "relevant chat match", "relevant chat matches")}.";
                    default:
                        return "Chat history search finished.";
                }
            }

            return "Chat history search finished.";
        }
    }
}
