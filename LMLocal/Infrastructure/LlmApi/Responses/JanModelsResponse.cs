using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Api.Responses
{
    /// <summary>
    /// Root response object from Jan API for the /v1/models endpoint.
    /// Contains a list of available models.
    /// </summary>
    internal class JanModelsResponse
    {
        /// <summary>
        /// Object type, always "list".
        /// </summary>
        [JsonProperty("object")]
        public string Object { get; set; } = string.Empty;

        /// <summary>
        /// List of model objects available in Jan.
        /// </summary>
        [JsonProperty("data")]
        public List<JanModel> Data { get; set; } = new List<JanModel>();
    }

    /// <summary>
    /// Information about a single model in Jan.
    /// </summary>
    internal class JanModel
    {
        /// <summary>
        /// Unique identifier of the model.
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Object type, always "model".
        /// </summary>
        [JsonProperty("object")]
        public string Object { get; set; } = string.Empty;

        /// <summary>
        /// Display name of the model.
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Version of the model.
        /// </summary>
        [JsonProperty("version")]
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Description of the model.
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Model format, e.g., "gguf".
        /// </summary>
        [JsonProperty("format")]
        public string Format { get; set; } = string.Empty;

        /// <summary>
        /// Model settings such as context length and prompt template.
        /// </summary>
        [JsonProperty("settings")]
        public JanModelSettings Settings { get; set; } = new JanModelSettings();

        /// <summary>
        /// Default generation parameters, e.g., temperature.
        /// </summary>
        [JsonProperty("parameters")]
        public JanModelParameters Parameters { get; set; } = new JanModelParameters();

        /// <summary>
        /// Model metadata (may be null).
        /// </summary>
        [JsonProperty("metadata")]
        public object Metadata { get; set; }

        /// <summary>
        /// Inference engine used, e.g., "nitro".
        /// </summary>
        [JsonProperty("engine")]
        public string Engine { get; set; } = string.Empty;

        /// <summary>
        /// Size of the model on disk in bytes.
        /// </summary>
        [JsonProperty("size")]
        public long Size { get; set; }
    }

    /// <summary>
    /// Model settings such as context length.
    /// </summary>
    internal class JanModelSettings
    {
        /// <summary>
        /// Context window size of the model (in tokens).
        /// </summary>
        [JsonProperty("ctx_len")]
        public int ContextLength { get; set; }

        /// <summary>
        /// Prompt template used by the model.
        /// </summary>
        [JsonProperty("prompt_template")]
        public string PromptTemplate { get; set; } = string.Empty;
    }

    /// <summary>
    /// Default generation parameters.
    /// </summary>
    internal class JanModelParameters
    {
        /// <summary>
        /// Temperature controlling "creativity" of responses.
        /// </summary>
        [JsonProperty("temperature")]
        public double Temperature { get; set; }

        /// <summary>
        /// Top_p (nucleus sampling) parameter.
        /// </summary>
        [JsonProperty("top_p")]
        public double TopP { get; set; }

        /// <summary>
        /// Flag indicating whether the model supports streaming.
        /// </summary>
        [JsonProperty("stream")]
        public bool Stream { get; set; }

        /// <summary>
        /// Maximum number of tokens in the response.
        /// </summary>
        [JsonProperty("max_tokens")]
        public int MaxTokens { get; set; }

        /// <summary>
        /// List of stop sequences.
        /// </summary>
        [JsonProperty("stop")]
        public List<string> Stop { get; set; } = new List<string>();

        /// <summary>
        /// Frequency penalty.
        /// </summary>
        [JsonProperty("frequency_penalty")]
        public double FrequencyPenalty { get; set; }

        /// <summary>
        /// Presence penalty.
        /// </summary>
        [JsonProperty("presence_penalty")]
        public double PresencePenalty { get; set; }
    }
}
