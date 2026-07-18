using Newtonsoft.Json;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Parameters for listing models for a given provider, deserialized from the JSON passed from the JavaScript bridge.
    /// </summary>
    public class ListModelsParameters
    {
        [JsonProperty("providerType")]
        public string ProviderType { get; set; }

        [JsonProperty("baseUrl")]
        public string BaseUrl { get; set; }

        [JsonProperty("apiKey")]
        public string ApiKey { get; set; }
    }
}
