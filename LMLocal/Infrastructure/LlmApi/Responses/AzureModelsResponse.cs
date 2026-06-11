using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.LlmApi.Responses
{
    /// <summary>
    /// Represents a model in the Azure/Github Models API response.
    /// </summary>
    internal class AzureModelInfo
    {
        /// <summary>
        /// Unique identifier of the model (e.g., "azureml://registries/azureml-cohere/models/Cohere-embed-v3-english/versions/3").
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// Model name/identifier (e.g., "Cohere-embed-v3-english").
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// Human-readable friendly name of the model (e.g., "Cohere Embed v3 English").
        /// </summary>
        [JsonProperty("friendly_name")]
        public string FriendlyName { get; set; }

        /// <summary>
        /// Model version number.
        /// </summary>
        [JsonProperty("model_version")]
        public int? ModelVersion { get; set; }

        /// <summary>
        /// Publisher/creator of the model (e.g., "cohere").
        /// </summary>
        [JsonProperty("publisher")]
        public string Publisher { get; set; }

        /// <summary>
        /// Model family (e.g., "cohere").
        /// </summary>
        [JsonProperty("model_family")]
        public string ModelFamily { get; set; }

        /// <summary>
        /// Registry where the model is registered.
        /// </summary>
        [JsonProperty("model_registry")]
        public string ModelRegistry { get; set; }

        /// <summary>
        /// License type (e.g., "custom", "MIT", etc.).
        /// </summary>
        [JsonProperty("license")]
        public string License { get; set; }

        /// <summary>
        /// Type of task this model performs (e.g., "embeddings", "chat-completion", "text-generation").
        /// </summary>
        [JsonProperty("task")]
        public string Task { get; set; }

        /// <summary>
        /// Detailed description of the model and its capabilities.
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>
        /// Short summary of the model.
        /// </summary>
        [JsonProperty("summary")]
        public string Summary { get; set; }

        /// <summary>
        /// Array of tags/labels for the model (e.g., ["RAG", "search"]).
        /// </summary>
        [JsonProperty("tags")]
        public List<string> Tags { get; set; }

        /// <summary>
        /// Additional metadata about the model.
        /// </summary>
        [JsonProperty("metadata", NullValueHandling = NullValueHandling.Ignore)]
        public object Metadata { get; set; }
    }
}
