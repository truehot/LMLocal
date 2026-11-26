using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// Outcome state used internally by GetCompletionMessage to distinguish "no knowledge files found" from "files scanned but no matches".
    /// </summary>
    internal enum KnowledgeSearchOutcome
    {
        None = 0,
        Success = 1,
        NoFiles = 2,
        NoMatches = 3
    }

    /// <summary>
    /// Match-quality tiers for search_solution_knowledge. Higher value = better.
    /// </summary>
    internal enum KnowledgeMatchKind
    {
        None = 0,
        FilenameSubstring = 1,

        /// <summary>Fallback: the record (file name + one section) with the most query words, below the all-words tiers.</summary>
        PartialWords = 2,

        FilenameExact = 3,
        SectionAllTokens = 4,
        BodyExact = 5,
        HeadingExact = 6
    }

    /// <summary>
    /// Response for search_solution_knowledge tool.
    /// </summary>
    public class KnowledgeSearchResponse
    {
        /// <summary>
        /// Internal outcome used by GetCompletionMessage.
        /// </summary>
        [JsonIgnore]
        internal KnowledgeSearchOutcome Outcome { get; set; } = KnowledgeSearchOutcome.None;

        [JsonProperty("results")]
        public List<KnowledgeMatchResult> Results { get; set; }

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
    /// A single knowledge file match with tier, heading and snippet context.
    /// </summary>
    public class KnowledgeMatchResult
    {
        /// <summary>
        /// Highest match tier reached by this file. Used for ranking.
        /// </summary>
        [JsonIgnore]
        internal KnowledgeMatchKind Kind { get; set; }

        /// <summary>
        /// Sub-score used only to rank files within the same <see cref="Kind"/>.
        /// </summary>
        [JsonIgnore]
        internal int SubScore { get; set; }

        /// <summary>
        /// Second ranking key within the same <see cref="Kind"/> and <see cref="SubScore"/>; 0 for tiers that do not use it.
        /// </summary>
        [JsonIgnore]
        internal int TieBreakScore { get; set; }

        /// <summary>
        /// Raw last-write timestamp used only for deterministic ranking tie-breaks.
        /// </summary>
        [JsonIgnore]
        internal DateTime LastWriteTimeUtc { get; set; }

        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        [JsonProperty("updated")]
        public string Updated { get; set; }

        [JsonProperty("heading", NullValueHandling = NullValueHandling.Ignore)]
        public string Heading { get; set; }

        [JsonProperty("snippet")]
        public string Snippet { get; set; }

        /// <summary>
        /// 1-based line number of the best match in the file.
        /// </summary>
        [JsonProperty("line")]
        public int LineNumber { get; set; }

        /// <summary>
        /// Query words found in this result; set only for <see cref="KnowledgeMatchKind.PartialWords"/>.
        /// </summary>
        [JsonProperty("matched_terms", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> MatchedTerms { get; set; }
    }
}
