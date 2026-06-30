using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Response model for the providers endpoint, returning default profiles,
    /// user-saved profiles, and the list of all known provider types for the UI.
    /// </summary>
    public class GetProvidersResponse
    {
        /// <summary>
        /// Built-in default provider profiles (local + openai).
        /// </summary>
        [JsonProperty("defaultProviders")]
        public List<CustomProvider> DefaultProviders { get; set; } = new List<CustomProvider>();

        /// <summary>
        /// User-customised provider profiles.
        /// </summary>
        [JsonProperty("providers")]
        public List<CustomProvider> Providers { get; set; } = new List<CustomProvider>();

        /// <summary>
        /// All known provider type keys with human-readable display names for the UI dropdown.
        /// Derived from <see cref="LMLocal.Infrastructure.LlmApi.Provider.ModelProvider"/> enum.
        /// </summary>
        [JsonProperty("providerTypes")]
        public List<ProviderTypeInfo> ProviderTypes { get; set; } = new List<ProviderTypeInfo>();
    }
}
