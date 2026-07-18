using Newtonsoft.Json;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Parameters for the autocomplete FIM (Fill-In-the-Middle) completion request, deserialized from the JSON passed from the JavaScript bridge.
    /// </summary>
    public class CompletionParameters
    {
        [JsonProperty("prompt")]
        public string Prompt { get; set; }

        [JsonProperty("suffix")]
        public string Suffix { get; set; }

        [JsonProperty("maxTokens")]
        public int MaxTokens { get; set; }

        [JsonProperty("temperature")]
        public double Temperature { get; set; }

        [JsonProperty("stop")]
        public string[] Stop { get; set; }
    }
}
