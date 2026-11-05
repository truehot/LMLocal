using Newtonsoft.Json;

namespace LMLocal.Infrastructure.WebView
{
    /// <summary>
    /// Sent when history compaction (summarization) finished. Closes the pair opened by
    /// <see cref="WebView2MessageType.CompactionStart"/> on both compaction paths.
    /// </summary>
    internal class WebView2CompactionEndMessage : WebView2ScriptMessage
    {
        /// <summary>
        /// <c>true</c> when the same session continues with a retry, so the front end must open a new
        /// message and stay in <c>PROCESSING</c> instead of returning to idle; <c>false</c> when the
        /// compaction simply ends the session.
        /// </summary>
        [JsonProperty("Continues")]
        public bool Continues { get; set; }
    }
}
