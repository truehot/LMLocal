using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Application.Changes.Models
{
    /// <summary>
    /// Serialized responses carry no such field.
    /// </summary>
    internal enum ChangesOutcome
    {
        /// <summary>No result computed (failure path).</summary>
        None = 0,

        /// <summary>At least one file diff was returned.</summary>
        Success = 1,

        /// <summary>The pending change set is empty (nothing was changed, or everything was committed/rolled back).</summary>
        NoPendingChanges = 2,

        /// <summary>Changes exist, but no diff could be built because the pre-change snapshots are missing or unreadable.</summary>
        SnapshotHistoryUnavailable = 3
    }

    /// <summary>
    /// Response of the get_changes_diff tool.
    /// </summary>
    public class ChangesResult
    {
        /// <summary>True unless the call itself failed (for example, no solution is open).</summary>
        [JsonProperty("success")]
        public bool Success { get; set; }

        /// <summary>Failure reason; omitted from the JSON when null.</summary>
        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Number of files in the selected set: after the "files" filter, before the max_files/max_lines caps.
        /// </summary>
        [JsonProperty("total_files")]
        public int TotalFiles { get; set; }

        /// <summary>Files present in the response. Never null; may be empty.</summary>
        [JsonProperty("files")]
        public List<ChangesFile> Files { get; set; } = new List<ChangesFile>();

        /// <summary>Added lines summed over the selected set (before the caps).</summary>
        [JsonProperty("total_added")]
        public int TotalAdded { get; set; }

        /// <summary>Removed lines summed over the selected set (before the caps).</summary>
        [JsonProperty("total_removed")]
        public int TotalRemoved { get; set; }

        /// <summary>
        /// True when max_files/max_lines removed something from the response.
        /// </summary>
        [JsonProperty("has_more_results")]
        public bool HasMoreResults { get; set; }

        /// <summary>Reason for an empty or partial response. Not serialized.</summary>
        [JsonIgnore]
        internal ChangesOutcome Outcome { get; set; }
    }

    /// <summary>
    /// One changed file in a <see cref="ChangesResult"/>.
    /// </summary>
    public class ChangesFile
    {
        /// <summary>Path relative to the solution root.</summary>
        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        /// <summary>"modified", "created" or "deleted".</summary>
        [JsonProperty("status")]
        public string Status { get; set; }

        /// <summary>Number of added lines in this file.</summary>
        [JsonProperty("added")]
        public int Added { get; set; }

        /// <summary>Number of removed lines in this file.</summary>
        [JsonProperty("removed")]
        public int Removed { get; set; }

        /// <summary>
        /// Unified diff hunks
        /// </summary>
        [JsonProperty("hunks", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Hunks { get; set; }
    }
}
