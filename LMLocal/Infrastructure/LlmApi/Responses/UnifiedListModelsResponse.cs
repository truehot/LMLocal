using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.LlmApi.Responses
{
    /// <summary>
    /// Unified response containing list of models from any provider.
    /// </summary>
    internal class UnifiedListModelsResponse
    {
        /// <summary>
        /// List of available models.
        /// </summary>
        [JsonProperty("models")]
        public List<UnifiedModelInfo> Models { get; set; } = new List<UnifiedModelInfo>();

        /// <summary>
        /// Error message if the request failed or no models are available.
        /// </summary>
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public string Error { get; set; }

        /// <summary>
        /// Indicates whether an active/loaded/selected model is currently available.
        /// </summary>
        [JsonProperty("hasActiveModel")]
        public bool HasActiveModel { get; set; }

        /// <summary>
        /// Information about the currently first active/selected model
        /// </summary>
        [JsonProperty("activeModel", NullValueHandling = NullValueHandling.Ignore)]
        public UnifiedModelInfo ActiveModel { get; set; }
    }

    /// <summary>
    /// Unified model information across different providers.
    /// All fields may be null/false depending on provider support.
    /// </summary>
    internal class UnifiedModelInfo
    {
        /// <summary>
        /// Unique identifier of the model (required).
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// Human-readable name/display name of the model.
        /// </summary>
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        /// <summary>
        /// Maximum context/token length supported by the model.
        /// </summary>
        [JsonProperty("maxTokens", NullValueHandling = NullValueHandling.Ignore)]
        public int? MaxTokens { get; set; }

        /// <summary>
        /// Indicates whether MaxTokens value is provided by the backend.
        /// </summary>
        [JsonProperty("supportsMaxTokens")]
        public bool SupportsMaxTokens { get; set; }

        /// <summary>
        /// Indicates whether the model is currently loaded and available for inference.
        /// </summary>
        [JsonProperty("isLoaded", NullValueHandling = NullValueHandling.Ignore)]
        public bool? IsLoaded { get; set; }

        /// <summary>
        /// Indicates whether the model is trained for/supports tool use (function calling).
        /// </summary>
        [JsonProperty("supportsToolUse", NullValueHandling = NullValueHandling.Ignore)]
        public bool? SupportsToolUse { get; set; }

        /// <summary>
        /// Model size in bytes.
        /// </summary>
        [JsonProperty("sizeInBytes", NullValueHandling = NullValueHandling.Ignore)]
        public long? SizeInBytes { get; set; }
    }
}
