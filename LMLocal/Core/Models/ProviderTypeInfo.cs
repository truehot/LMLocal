using Newtonsoft.Json;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Represents a single provider type option for the UI selector.
    /// </summary>
    public class ProviderTypeInfo
    {
        /// <summary>
        /// Provider type key used in configuration and API calls (e.g. "lmstudio", "openai").
        /// </summary>
        [JsonProperty("key")]
        public string Key { get; set; }

        /// <summary>
        /// Human-readable name shown in the UI dropdown (e.g. "LM Studio (local)").
        /// </summary>
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }
    }
}
