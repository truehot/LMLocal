using System;
using System.Collections.Generic;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// Outcome state used internally by GetCompletionMessage to distinguish "no chat logs found" from "logs scanned but no matches".
    /// </summary>
    internal enum ChatHistorySearchOutcome
    {
        None = 0,
        Success = 1,
        NoFiles = 2,
        NoMatches = 3
    }

    /// <summary>
    /// Match-quality tiers for search_chat_history. Higher value = better; <see cref="TurnPartial"/> is the only weak tier.
    /// </summary>
    internal enum ChatHistoryMatchKind
    {
        None = 0,

        /// <summary>Fallback: the turn with the most significant query words, below the all-words tiers.</summary>
        TurnPartial = 1,

        /// <summary>All query words inside a single turn.</summary>
        TurnAllTokens = 2,

        /// <summary>The whole phrase inside the turn's final answer.</summary>
        AnswerExact = 3,

        /// <summary>The whole phrase inside the turn's own user text.</summary>
        QuestionExact = 4
    }

    /// <summary>
    /// Response for the search_chat_history tool.
    /// </summary>
    public class ChatHistorySearchResponse
    {
        [JsonIgnore]
        internal ChatHistorySearchOutcome Outcome { get; set; } = ChatHistorySearchOutcome.None;

        [JsonProperty("results")]
        public List<ChatHistoryMatchResult> Results { get; set; }

        [JsonProperty("total_matches")]
        public int TotalMatches { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Set when some results match only part of the query words (they carry matched_terms).
        /// </summary>
        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note { get; set; }
    }

    /// <summary>
    /// A single chat-history match: the best matching turn of one session with its question, the opening of its answer,
    /// a fragment around the match and the files the turn was about.
    /// </summary>
    public class ChatHistoryMatchResult
    {
        /// <summary>
        /// Match tier of this result. Used for ranking and for capping weak results; not serialized.
        /// </summary>
        [JsonIgnore]
        internal ChatHistoryMatchKind Kind { get; set; }

        /// <summary>
        /// Raw timestamp of the matched turn, used for ranking. Not serialized.
        /// </summary>
        [JsonIgnore]
        internal DateTime TimestampUtc { get; set; }

        /// <summary>
        /// Session identifier, used for deterministic tie-breaks only.
        /// </summary>
        [JsonIgnore]
        internal string SessionId { get; set; }

        /// <summary>
        /// Index of the matched turn within the session, used for deterministic tie-breaks only.
        /// </summary>
        [JsonIgnore]
        internal int TurnIndex { get; set; }

        /// <summary>
        /// ISO-8601 timestamp of the matched turn's user message.
        /// </summary>
        [JsonProperty("timestamp", NullValueHandling = NullValueHandling.Ignore)]
        public string Timestamp { get; set; }

        /// <summary>
        /// Query words found in this result; set only when the words matched partially.
        /// </summary>
        [JsonProperty("matched_terms", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> MatchedTerms { get; set; }

        /// <summary>
        /// The user's own text of the matched turn, truncated for presentation.
        /// </summary>
        [JsonProperty("question", NullValueHandling = NullValueHandling.Ignore)]
        public string Question { get; set; }

        /// <summary>
        /// The opening of the turn's final answer — where the conclusion sits — truncated for presentation.
        /// </summary>
        [JsonProperty("answer_start", NullValueHandling = NullValueHandling.Ignore)]
        public string AnswerStart { get; set; }

        /// <summary>
        /// Fragment around the match; omitted when the match is already visible in <see cref="Question"/> or <see cref="AnswerStart"/>.
        /// </summary>
        [JsonProperty("snippet", NullValueHandling = NullValueHandling.Ignore)]
        public string Snippet { get; set; }

        /// <summary>
        /// Files the turn was about: the ones inserted into the session's first question plus the matched turn's own, without duplicates.
        /// </summary>
        [JsonProperty("files", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Files { get; set; }

        /// <summary>
        /// Opening of the session's last answer, when the match is not in that answer.
        /// </summary>
        [JsonProperty("last_answer_start", NullValueHandling = NullValueHandling.Ignore)]
        public string LastAnswerStart { get; set; }

        /// <summary>
        /// Number of sessions this result stands for when several had the same question and answer; omitted when it is 1.
        /// </summary>
        [JsonProperty("similar_sessions", NullValueHandling = NullValueHandling.Ignore)]
        public int? SimilarSessions { get; set; }
    }

    /// <summary>
    /// Cached query results: the collapsed matches plus the significant words found in no session. Keeping the words
    /// (not the finished note) lets the note be built for whatever page of the cached results is returned.
    /// </summary>
    internal sealed class ChatHistoryCachedResults : CachedToolResults<ChatHistoryMatchResult>
    {
        public string[] MissingTerms { get; set; }
    }
}
