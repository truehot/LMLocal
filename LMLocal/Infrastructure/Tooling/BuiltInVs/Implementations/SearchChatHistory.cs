using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using Microsoft.VisualStudio.Threading;
using static LMLocal.Core.Common.Pluralizer;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Deterministic search over past chat-history sessions. Returns one best-matching turn per session
    /// (session title + matched question heading + markdown snippet).
    /// </summary>
    internal interface ISearchChatHistory : IBuiltInTool
    {
    }

    internal class SearchChatHistory : ISearchChatHistory
    {
        private const int DefaultMaxResults = 5;
        private const int MaxMaxResults = 50;

        private const string AllScope = "all";
        private const string CurrentSolutionScope = "current_solution";

        private const string SessionsCacheKey = "chat_history_sessions|v1";

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
                Description = "Searches archived, completed chat sessions from previous conversations. Use this tool when the current request may depend on something discussed in an earlier conversation, especially when the user refers to previous work, decisions, code, configuration, tests, prompts, or asks to continue, recover, verify, or recall something from before. It is also useful for checking whether the current request is a continuation of an earlier conversation. Search when the context is unclear or may have been lost because the current chat was cleared or restarted. The current active session is never returned. Prefer specific keyword phrases, technical terms, identifiers, or topics over full sentences. Exact-phrase matches score highest. Returns the most relevant matching sessions as the session title, the matched user question (heading), and a markdown snippet. If the needed information is not found in past sessions, do not guess. Safe to call several times in parallel.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "query", new ToolDetails { Type = "string", Description = "A short keyword phrase, technical term, identifier, feature name, or topic to search for in past chat history. Prefer specific terms over full sentences." } },
                        { "max_results", new ToolDetails { Type = "integer", Description = "Maximum number of top matching sessions to return. Default 5, maximum 50." } }
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
                return Success(cachedResults.AllResults.Take(maxResults).ToList(), cachedResults.AllResults.Count);
            }

            await TaskScheduler.Default;

            try
            {
                var sessions = await GetSessionsAsync(cancellationToken).ConfigureAwait(false);
                if (sessions.Count == 0)
                    return Success(new List<ChatHistoryMatchResult>(), 0, ChatHistorySearchOutcome.NoFiles);

                var matches = new List<ChatHistoryMatchResult>();

                foreach (var session in sessions)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var score = ChatHistoryScorer.Score(session, query);
                    if (!score.IsMatch)
                        continue;

                    matches.Add(BuildResult(session, score));
                }

                if (matches.Count == 0)
                    return Success(new List<ChatHistoryMatchResult>(), 0, ChatHistorySearchOutcome.NoMatches);

                matches.Sort(CompareMatches);

                _searchCache.Set(cacheKey, CachePartition, new CachedToolResults<ChatHistoryMatchResult>
                {
                    AllResults = matches
                });

                return Success(matches.Take(maxResults).ToList(), matches.Count);
            }
            catch (OperationCanceledException)
            {
                return Failure("Operation was cancelled.");
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"search_chat_history: unexpected error: {ex.Message}");
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
            return new ChatHistoryMatchResult
            {
                Kind = score.Kind,
                SubScore = score.SubScore,
                TimestampUtc = session.TimestampUtc,
                SessionId = session.SessionId,
                TurnIndex = score.TurnIndex,
                SessionTitle = ChatLogSerializer.TruncatePrompt(session.Title),
                Timestamp = session.TimestampUtc == DateTime.MinValue ? null : session.TimestampUtc.ToString("o"),
                Role = score.Role,
                Heading = TruncateHeading(score.Heading),
                Snippet = score.Snippet
            };
        }

        /// <summary>
        /// Truncates the matched user question (heading) for presentation.
        /// </summary>
        private static string TruncateHeading(string heading)
        {
            string truncated = ChatLogSerializer.Truncate(heading, ChatHistoryScorer.MaxHeadingLength);

            if (heading != null && heading.Length > ChatHistoryScorer.MaxHeadingLength)
            {
                InternalLogger.Debug(
                    $"search_chat_history: truncated heading from {heading.Length} to {truncated.Length} chars.");
            }

            return truncated;
        }

        private static int CompareMatches(ChatHistoryMatchResult a, ChatHistoryMatchResult b)
        {
            int c = b.Kind.CompareTo(a.Kind);
            if (c != 0) return c;

            c = b.SubScore.CompareTo(a.SubScore);
            if (c != 0) return c;

            c = b.TimestampUtc.CompareTo(a.TimestampUtc);
            if (c != 0) return c;

            c = string.CompareOrdinal(a.SessionId, b.SessionId);
            if (c != 0) return c;

            return a.TurnIndex.CompareTo(b.TurnIndex);
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
            ChatHistorySearchOutcome outcome = ChatHistorySearchOutcome.Success)
        {
            return new ChatHistorySearchResponse
            {
                Success = true,
                Results = results,
                TotalMatches = totalMatches,
                Outcome = outcome
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
