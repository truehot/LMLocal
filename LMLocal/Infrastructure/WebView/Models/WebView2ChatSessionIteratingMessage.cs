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
    }
}
