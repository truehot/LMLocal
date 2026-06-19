using System.Collections.Generic;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;

namespace LMLocal.Infrastructure.WebView
{
    /// <summary>
    /// Message sent to frontend when snapshot file list changes.
    /// </summary>
    internal class WebView2SnapshotMessage : WebView2ScriptMessage
    {
        public WebView2SnapshotMessage()
        {
            Type = WebView2MessageType.SnapshotFilesChanged;
        }

        /// <summary>
        /// List of file changes with their status (created, deleted, or modified).
        /// </summary>
        public List<SnapshotFileChange> ChangedFiles { get; set; }
    }
}
