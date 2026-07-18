using Newtonsoft.Json;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Configuration for the autocomplete (FIM) feature.
    /// Controls whether autocomplete is enabled, which provider and model to use.
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
        /// Passed as "model" in the /v1/completions request body.
        /// </summary>
        [JsonProperty("modelId")]
        public string ModelId { get; set; } = string.Empty;
    }
}
