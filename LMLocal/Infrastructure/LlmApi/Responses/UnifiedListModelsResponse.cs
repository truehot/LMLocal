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

        /// <summary>
        /// Indicates whether the provider supports the IsLoaded indicator on models.
        /// Default: true. Set to false for cloud providers (OpenAI, Azure, Together AI) that cannot report whether a model is loaded in memory.
        /// </summary>
        [JsonProperty("supportsIsLoaded")]
        public bool SupportsIsLoaded { get; set; } = true;
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
        /// Indicates whether the model supports vision (image inputs).
        /// Null when the provider does not report this capability.
        /// </summary>
        [JsonProperty("supportsVision", NullValueHandling = NullValueHandling.Ignore)]
        public bool? SupportsVision { get; set; }

        /// <summary>
        /// Model size in bytes.
        /// </summary>
        [JsonProperty("sizeInBytes", NullValueHandling = NullValueHandling.Ignore)]
        public long? SizeInBytes { get; set; }

        /// <summary>
        /// Price per 1M input tokens in USD. Reported by providers like OpenRouter.
        /// </summary>
        [JsonProperty("inputPricePerMillion", NullValueHandling = NullValueHandling.Ignore)]
        public decimal? InputPricePerMillion { get; set; }

        /// <summary>
        /// Price per 1M output tokens in USD. Reported by providers like OpenRouter.
        /// </summary>
        [JsonProperty("outputPricePerMillion", NullValueHandling = NullValueHandling.Ignore)]
        public decimal? OutputPricePerMillion { get; set; }

        /// <summary>
        /// Price per 1M tokens read from provider cache in USD (if reported).
        /// </summary>
        [JsonProperty("cacheReadPricePerMillion", NullValueHandling = NullValueHandling.Ignore)]
        public decimal? CacheReadPricePerMillion { get; set; }

        /// <summary>
        /// Price per 1M tokens written to provider cache in USD (if reported).
        /// </summary>
        [JsonProperty("cacheWritePricePerMillion", NullValueHandling = NullValueHandling.Ignore)]
        public decimal? CacheWritePricePerMillion { get; set; }
    }
}
