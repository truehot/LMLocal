using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// A single matched line within a file.
    /// </summary>
    public class SearchMatch
    {
        [JsonProperty("line")]
        public int LineNumber { get; set; }

        [JsonProperty("text")]
        public string LineText { get; set; }

        [JsonProperty("is_exact_word")]
        public bool IsExactWord { get; set; }

        [JsonProperty("declaration_kind", NullValueHandling = NullValueHandling.Ignore)]
        public string DeclarationKind { get; set; }
    }

    /// <summary>
    /// Matches found within a single file (grouping = file).
    /// </summary>
    public class SearchResult
    {
        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        [JsonProperty("matches")]
        public List<SearchMatch> Matches { get; set; }

        [JsonProperty("match_count")]
        public int MatchCount { get; set; }

        /// <summary>
        /// Internal relevance score used only for ranking. Not part of the response.
        /// </summary>
        [JsonIgnore]
        public int Score { get; set; }
    }

    /// <summary>
    /// A single file entry inside a text group, with the lines where the text occurs.
    /// </summary>
    public class SearchTextGroupFileInfo
    {
        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        [JsonProperty("lines")]
        public List<int> Lines { get; set; }
    }

    /// <summary>
    /// One distinct matching line, grouped with its occurrence count and file locations (grouping = text).
    /// </summary>
    public class SearchTextGroupResult
    {
        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("occurrence_count")]
        public int OccurrenceCount { get; set; }

        [JsonProperty("files")]
        public List<SearchTextGroupFileInfo> Files { get; set; }
    }

    /// <summary>
    /// Response for grouping = file.
    /// </summary>
    public class SearchResultsResponse
    {
        [JsonProperty("results")]
        public List<SearchResult> Results { get; set; }

        [JsonProperty("next_page_token", NullValueHandling = NullValueHandling.Ignore)]
        public string NextPageToken { get; set; }

        [JsonProperty("total_matches")]
        public int TotalMatches { get; set; }

        [JsonProperty("total_files")]
        public int TotalFiles { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Response for grouping = text.
    /// </summary>
    public class SearchTextGroupResponse
    {
        [JsonProperty("results")]
        public List<SearchTextGroupResult> Results { get; set; }

        [JsonProperty("next_page_token", NullValueHandling = NullValueHandling.Ignore)]
        public string NextPageToken { get; set; }

        [JsonProperty("total_matches")]
        public int TotalMatches { get; set; }

        [JsonProperty("total_files")]
        public int TotalFiles { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// A single page of file-grouped results (used for caching).
    /// </summary>
    public class FileSearchPage
    {
        public List<SearchResult> Results { get; set; }
        public int TotalMatches { get; set; }
        public int TotalFiles { get; set; }
    }

    /// <summary>
    /// A single page of text-grouped results (used for caching).
    /// </summary>
    public class TextSearchPage
    {
        public List<SearchTextGroupResult> Results { get; set; }
        public int TotalMatches { get; set; }
        public int TotalFiles { get; set; }
    }
}
