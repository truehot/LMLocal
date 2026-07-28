using Newtonsoft.Json;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Configuration for the autocomplete (FIM) feature.
    /// </summary>
    public class AutocompletionsConfig
    {
        /// <summary>
        /// Whether the autocomplete feature is enabled.
        /// </summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// The provider ID referencing a CustomProvider.Id from providers config.
        /// Default is 0 (LM Studio).
        /// </summary>
        [JsonProperty("providerId")]
        public int ProviderId { get; set; } = 0;

        /// <summary>
        /// Provider type string: "lmstudio", "ollama", etc.
        /// </summary>
        [JsonProperty("providerType")]
        public string ProviderType { get; set; } = "lmstudio";

        /// <summary>
        /// The model ID to use for autocomplete (e.g. "ibm/granite-4-micro").
        /// </summary>
        [JsonProperty("modelId")]
        public string ModelId { get; set; } = string.Empty;

        /// <summary>
        /// Debounce delay in milliseconds before triggering an autocomplete request after the user stops typing.
        /// </summary>
        [JsonProperty("debounceDelayMs")]
        public int DebounceDelayMs { get; set; } = 300;
    }
}
