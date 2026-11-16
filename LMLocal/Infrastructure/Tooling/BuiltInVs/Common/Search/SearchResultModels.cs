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

        /// <summary>
        /// Fallback term that matched this line.
        /// </summary>
        [JsonProperty("matched_term", NullValueHandling = NullValueHandling.Ignore)]
        public string MatchedTerm { get; set; }
    }

    /// <summary>
    /// Matches found within a single file (grouping = file).
    /// </summary>
    public class SearchResult
    {
        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        /// <summary>
        /// Line count of the file (the scan reads it in full), so a following read_file_lines can stay within it.
        /// </summary>
        [JsonProperty("total_lines", NullValueHandling = NullValueHandling.Ignore)]
        public int? TotalLines { get; set; }

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
        /// <summary>The searched text; set only on a block of <see cref="SearchMultiTermResponse"/>.</summary>
        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore, Order = -2)]
        public string Text { get; set; }

        [JsonProperty("results")]
        public List<SearchResult> Results { get; set; }

        /// <summary>Set when the literal text had no matches and an OR-split / unquoted fallback was searched.</summary>
        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note { get; set; }

        /// <summary>Number of this page; set only when the result has more than one page.</summary>
        [JsonProperty("page", NullValueHandling = NullValueHandling.Ignore)]
        public int? Page { get; set; }

        /// <summary>Total page count at this page size; set only when the result has more than one page.</summary>
        [JsonProperty("total_pages", NullValueHandling = NullValueHandling.Ignore)]
        public int? TotalPages { get; set; }

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
        /// <summary>The searched text; set only on a block of <see cref="SearchMultiTermResponse"/>.</summary>
        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore, Order = -2)]
        public string Text { get; set; }

        [JsonProperty("results")]
        public List<SearchTextGroupResult> Results { get; set; }

        /// <summary>Set when the literal text had no matches and an OR-split / unquoted fallback was searched.</summary>
        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note { get; set; }

        /// <summary>Number of this page; set only when the result has more than one page.</summary>
        [JsonProperty("page", NullValueHandling = NullValueHandling.Ignore)]
        public int? Page { get; set; }

        /// <summary>Total page count at this page size; set only when the result has more than one page.</summary>
        [JsonProperty("total_pages", NullValueHandling = NullValueHandling.Ignore)]
        public int? TotalPages { get; set; }

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
    /// Response for a call with 'texts': one block per searched text, in request order. Each block is a
    /// <see cref="SearchResultsResponse"/> (grouping = file) or <see cref="SearchTextGroupResponse"/> (grouping = text)
    /// with <c>Text</c> set and its own totals, note and next_page_token.
    /// </summary>
    public class SearchMultiTermResponse
    {
        [JsonProperty("terms")]
        public List<object> Terms { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }
    }
}
