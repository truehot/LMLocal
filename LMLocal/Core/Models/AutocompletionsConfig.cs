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

        /// <summary>
        /// Inline completion backend: "legacy" (custom ghost-text stack, default)
        /// or "native" (Visual Studio Inline Suggestions via Microsoft.VisualStudio.Language.Proposals).
        /// </summary>
        [JsonProperty("backend")]
        public string Backend { get; set; } = "legacy";

        /// <summary>
        /// Native backend only: show only the first line as ghost text while Tab inserts the full
        /// multiline suggestion. Prevents the native multiline renderer from shifting the code below.
        /// </summary>
        [JsonProperty("singleLinePreview")]
        public bool SingleLinePreview { get; set; } = true;
    }
}
