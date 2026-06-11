using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LMLocal.Infrastructure.LlmApi.Responses
{
    /// <summary>
    /// Root response object from Ollama /api/ps endpoint.
    /// Contains list of models currently loaded in memory.
    /// </summary>
    internal class OllamaPsResponse
    {
        /// <summary>
        /// List of models currently loaded in memory.
        /// </summary>
        [JsonPropertyName("models")]
        public List<OllamaRunningModel> Models { get; set; } = new List<OllamaRunningModel>();
    }

    /// <summary>
    /// Information about a model loaded in Ollama.
    /// </summary>
    internal class OllamaRunningModel
    {
        /// <summary>
        /// Model name (e.g., "llama3.2:latest").
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Base model name (often matches the name).
        /// </summary>
        [JsonPropertyName("model")]
        public string Model { get; set; }

        /// <summary>
        /// Model size on disk in bytes.
        /// </summary>
        [JsonPropertyName("size")]
        public long Size { get; set; }

        /// <summary>
        /// SHA256 digest/hash of the model.
        /// </summary>
        [JsonPropertyName("digest")]
        public string Digest { get; set; }

        /// <summary>
        /// Model details such as format, family, etc.
        /// </summary>
        [JsonPropertyName("details")]
        public OllamaModelDetails Details { get; set; }

        /// <summary>
        /// ISO 8601 timestamp when the model will be unloaded from memory.
        /// </summary>
        [JsonPropertyName("expires_at")]
        public string ExpiresAt { get; set; }

        /// <summary>
        /// Amount of VRAM (video memory) used by the model in bytes.
        /// </summary>
        [JsonPropertyName("size_vram")]
        public long SizeVram { get; set; }

        /// <summary>
        /// Maximum context length supported by the model (in tokens).
        /// </summary>
        [JsonPropertyName("context_length")]
        public int ContextLength { get; set; }
    }

    /// <summary>
    /// Detailed information about an Ollama model.
    /// </summary>
    internal class OllamaModelDetails
    {
        /// <summary>
        /// Model format (e.g., "gguf").
        /// </summary>
        [JsonPropertyName("format")]
        public string Format { get; set; }

        /// <summary>
        /// Model family (e.g., "llama", "gemma").
        /// </summary>
        [JsonPropertyName("family")]
        public string Family { get; set; }

        /// <summary>
        /// List of model families this model may belong to.
        /// </summary>
        [JsonPropertyName("families")]
        public List<string> Families { get; set; }

        /// <summary>
        /// Model size in parameters (e.g., "8B", "70B").
        /// </summary>
        [JsonPropertyName("parameter_size")]
        public string ParameterSize { get; set; }

        /// <summary>
        /// Model quantization level (e.g., "Q4_K_M").
        /// </summary>
        [JsonPropertyName("quantization_level")]
        public string QuantizationLevel { get; set; }
    }
}
