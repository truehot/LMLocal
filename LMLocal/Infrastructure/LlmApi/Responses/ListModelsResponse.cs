using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.LlmApi.Responses
{
    /// <summary>
    /// OpenAI-compatible models list response format.
    /// </summary>
    internal class ListModelsResponse
    {
        [JsonProperty("object")]
        public string Object { get; set; }

        [JsonProperty("data")]
        public List<OpenAiModelInfo> Data { get; set; }
    }

    /// <summary>
    /// Model info in OpenAI format.
    /// </summary>
    internal class OpenAiModelInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("object")]
        public string Object { get; set; }

        [JsonProperty("created")]
        public long Created { get; set; }

        [JsonProperty("owned_by")]
        public string OwnedBy { get; set; }

        [JsonProperty("parent", NullValueHandling = NullValueHandling.Ignore)]
        public string Parent { get; set; }

        /// <summary>
        /// Human-readable model name (OpenRouter: "name").
        /// </summary>
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        /// <summary>
        /// Human-readable model name (Gemini OpenAI-compatible: "displayName").
        /// </summary>
        [JsonProperty("displayName", NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayName { get; set; }

        /// <summary>
        /// Snake_case alias for providers that emit "display_name" instead of "displayName".
        /// </summary>
        [JsonProperty("display_name", NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayNameSnakeCase { get; set; }

        /// <summary>
        /// Maximum context length in tokens (OpenRouter: "context_length").
        /// </summary>
        [JsonProperty("context_length", NullValueHandling = NullValueHandling.Ignore)]
        public long? ContextLength { get; set; }

        /// <summary>
        /// Alias for maximum context length in tokens ("context_window").
        /// </summary>
        [JsonProperty("context_window", NullValueHandling = NullValueHandling.Ignore)]
        public long? ContextWindow { get; set; }

        /// <summary>
        /// Alias for maximum context length in tokens ("max_context_length").
        /// </summary>
        [JsonProperty("max_context_length", NullValueHandling = NullValueHandling.Ignore)]
        public long? MaxContextLength { get; set; }

        /// <summary>
        /// Alias for maximum context length in tokens ("max_model_len", used by vLLM-style endpoints).
        /// </summary>
        [JsonProperty("max_model_len", NullValueHandling = NullValueHandling.Ignore)]
        public long? MaxModelLen { get; set; }

        /// <summary>
        /// Per-token pricing (OpenRouter: "pricing"). Values are strings.
        /// </summary>
        [JsonProperty("pricing", NullValueHandling = NullValueHandling.Ignore)]
        public OpenAiPricing Pricing { get; set; }

        /// <summary>
        /// Model architecture / modality info .
        /// </summary>
        [JsonProperty("architecture", NullValueHandling = NullValueHandling.Ignore)]
        public OpenAiArchitecture Architecture { get; set; }

        /// <summary>
        /// List of parameters the model supports.
        /// </summary>
        [JsonProperty("supported_parameters", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> SupportedParameters { get; set; }
    }

    /// <summary>
    /// Per-token pricing reported as strings by OpenAI-compatible providers (e.g. OpenRouter).
    /// </summary>
    internal class OpenAiPricing
    {
        /// <summary>Price per input token.</summary>
        [JsonProperty("prompt")]
        public string Prompt { get; set; }

        /// <summary>Price per output token.</summary>
        [JsonProperty("completion")]
        public string Completion { get; set; }

        /// <summary>Price per token read from provider cache.</summary>
        [JsonProperty("input_cache_read")]
        public string InputCacheRead { get; set; }

        /// <summary>Price per token written to provider cache.</summary>
        [JsonProperty("input_cache_write")]
        public string InputCacheWrite { get; set; }
    }

    /// <summary>
    /// Model architecture / modality info (OpenRouter: "architecture").
    /// </summary>
    internal class OpenAiArchitecture
    {
        /// <summary>Modality string, e.g. "text+image-&gt;text".</summary>
        [JsonProperty("modality")]
        public string Modality { get; set; }

        /// <summary>Accepted input modalities, e.g. ["text", "image"].</summary>
        [JsonProperty("input_modalities")]
        public List<string> InputModalities { get; set; }

        /// <summary>Produced output modalities, e.g. ["text"].</summary>
        [JsonProperty("output_modalities")]
        public List<string> OutputModalities { get; set; }
    }
}
