using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Single "recently used model" entry: provider + model pair with last usage timestamp.
    /// </summary>
    public class RecentModelEntry
    {
        [JsonProperty("providerType")]
        public string ProviderType { get; set; }

        [JsonProperty("providerId")]
        public int? ProviderId { get; set; }

        [JsonProperty("modelId")]
        public string ModelId { get; set; }

        [JsonProperty("modelName")]
        public string ModelName { get; set; }

        [JsonProperty("lastUsedUtc")]
        public DateTimeOffset LastUsedUtc { get; set; }
    }

    /// <summary>
    /// Root document of recent-models.json. Newest entries first.
    /// </summary>
    public class RecentModelsFile
    {
        [JsonProperty("entries")]
        public List<RecentModelEntry> Entries { get; set; } = new List<RecentModelEntry>();
    }

    /// <summary>
    /// Payload sent from the WebView when a model is selected (RecordModelUsageAsync).
    /// </summary>
    public class RecentModelUsageRequest
    {
        [JsonProperty("providerType")]
        public string ProviderType { get; set; }

        [JsonProperty("providerId")]
        public int? ProviderId { get; set; }

        [JsonProperty("modelId")]
        public string ModelId { get; set; }

        [JsonProperty("modelName")]
        public string ModelName { get; set; }
    }
}
