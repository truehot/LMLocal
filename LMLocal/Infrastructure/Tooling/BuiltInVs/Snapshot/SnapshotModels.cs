using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot
{
    /// <summary>
    /// Status of a file change known at snapshot time.
    /// </summary>
    internal enum SnapshotChangeStatus
    {
        /// <summary>Snapshot before creating a file that didn't exist.</summary>
        BeforeCreate,
        /// <summary>Snapshot before modifying an existing file.</summary>
        BeforeModify,
        /// <summary>Snapshot before deleting an existing file.</summary>
        BeforeDelete
    }
    /// <summary>
    /// Entry in the snapshot manifest representing one tracked file.
    /// </summary>
    internal class SnapshotFileEntry
    {
        [JsonProperty("relativePath")]
        public string RelativePath { get; set; }

        [JsonProperty("backupId")]
        public Guid BackupId { get; set; }

        [JsonProperty("existedAtSnapshot")]
        public bool ExistedAtSnapshot { get; set; }

        [JsonProperty("fileSize")]
        public long FileSize { get; set; }

        [JsonProperty("lastWriteTimeUtc")]
        public DateTime LastWriteTimeUtc { get; set; }
    }

    /// <summary>
    /// Serializable manifest describing all files under snapshot management.
    /// </summary>
    internal class SnapshotManifestInfo
    {
        [JsonProperty("solutionRoot")]
        public string SolutionRoot { get; set; }

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("files")]
        public List<SnapshotFileEntry> Files { get; set; } = new List<SnapshotFileEntry>();
    }

    /// <summary>
    /// Represents a file change visible to the UI.
    /// </summary>
    internal class SnapshotFileChange
    {
        /// <summary>Relative path from solution root.</summary>
        [JsonProperty("relativePath")]
        public string RelativePath { get; set; }

        /// <summary>"created", "deleted", or "modified".</summary>
        [JsonProperty("status")]
        public string Status { get; set; }
    }

    /// <summary>
    /// Immutable value object holding on-disk paths for a snapshot
    /// associated with a specific solution.
    /// </summary>
    internal sealed class SnapshotPaths
    {
        public string SnapshotDir { get; }
        public string FilesDirectory { get; }
        public string TmpDirectory { get; }
        public string ManifestPath { get; }

        public SnapshotPaths(string snapshotDir, string filesDirectory, string tmpDirectory, string manifestPath)
        {
            SnapshotDir = snapshotDir ?? throw new ArgumentNullException(nameof(snapshotDir));
            FilesDirectory = filesDirectory ?? throw new ArgumentNullException(nameof(filesDirectory));
            TmpDirectory = tmpDirectory ?? throw new ArgumentNullException(nameof(tmpDirectory));
            ManifestPath = manifestPath ?? throw new ArgumentNullException(nameof(manifestPath));
        }

        public string GetBackupPath(Guid backupId)
            => Path.Combine(FilesDirectory, backupId.ToString("N") + ".bak");
    }
}
