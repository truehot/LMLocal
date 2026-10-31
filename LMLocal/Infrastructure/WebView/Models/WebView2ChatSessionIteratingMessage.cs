using Newtonsoft.Json;

namespace LMLocal.Infrastructure.WebView
{
    internal class WebView2ChatSessionIteratingMessage : WebView2ScriptMessage
    {
        [JsonProperty("RoundNumber")]
        public int RoundNumber { get; set; }

        [JsonProperty("ToolCount")]
        public int ToolCount { get; set; }

        [JsonProperty("IsFinalRound")]
        public bool IsFinalRound { get; set; }

        /// <summary>
        /// Estimate of the tokens taken by the tool results that will be prepended to the next round's prompt.
        /// </summary>
        [JsonProperty("EstimatedToolResultTokens")]
        public int? EstimatedToolResultTokens { get; set; }
    }
}
