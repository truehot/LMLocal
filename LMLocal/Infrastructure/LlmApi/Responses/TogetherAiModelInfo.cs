using Newtonsoft.Json;

namespace LMLocal.Infrastructure.LlmApi.Responses
{
    /// <summary>
    /// Together AI /v1/models response — direct JSON array of model objects (no "data" wrapper).
    /// </summary>
    internal class TogetherAiModelInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("object")]
        public string Object { get; set; }

        [JsonProperty("created")]
        public long Created { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("display_name")]
        public string DisplayName { get; set; }

        /// <summary>
        /// Maximum context length (tokens) supported by this model.
        /// </summary>
        [JsonProperty("context_length")]
        public int ContextLength { get; set; }

        /// <summary>
        /// Model configuration (chat_template, stop tokens, etc.).
        /// </summary>
        [JsonProperty("config")]
        public TogetherAiModelConfig Config { get; set; }
    }

    /// <summary>
    /// Configuration sub-object for Together AI models.
    /// </summary>
    internal class TogetherAiModelConfig
    {
        /// <summary>
        /// Jinja chat template string. Used to infer tool-calling support by searching for "tools" or "tool_calls" tokens.
        /// </summary>
        [JsonProperty("chat_template")]
        public string ChatTemplate { get; set; }
    }
}
