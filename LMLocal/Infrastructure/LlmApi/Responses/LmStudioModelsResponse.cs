using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.LlmApi.Responses
{
    /// <summary>
    /// LM Studio /api/v1/models response format.
    /// Returns models array with detailed information including loaded instances and capabilities.
    /// </summary>
    internal class LmStudioModelsResponse
    {
        [JsonProperty("models")]
        public List<LmStudioModelInfo> Models { get; set; }
    }

    /// <summary>
    /// Model information from LM Studio backend.
    /// Contains detailed metadata and loaded instance information.
    /// </summary>
    internal class LmStudioModelInfo
    {
        /// <summary>
        /// Model type: "llm" for language models, "embedding" for embedding models, etc.
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// Publisher/author of the model.
        /// </summary>
        [JsonProperty("publisher")]
        public string Publisher { get; set; }

        /// <summary>
        /// Unique model identifier/key.
        /// </summary>
        [JsonProperty("key")]
        public string Key { get; set; }

        /// <summary>
        /// Human-readable display name.
        /// </summary>
        [JsonProperty("display_name")]
        public string DisplayName { get; set; }

        /// <summary>
        /// Indicates whether the model supports vision (image inputs).
        /// Top-level flag returned by LM Studio.
        /// </summary>
        [JsonProperty("vision", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Vision { get; set; }

        /// <summary>
        /// Model architecture (e.g., "llama", "mistral", "qwen").
        /// Only for LLM models.
        /// </summary>
        [JsonProperty("architecture", NullValueHandling = NullValueHandling.Ignore)]
        public string Architecture { get; set; }

        /// <summary>
        /// Quantization information.
        /// </summary>
        [JsonProperty("quantization", NullValueHandling = NullValueHandling.Ignore)]
        public QuantizationInfo Quantization { get; set; }

        /// <summary>
        /// Model size on disk in bytes.
        /// </summary>
        [JsonProperty("size_bytes")]
        public long SizeBytes { get; set; }

        /// <summary>
        /// Human-readable parameter count string (e.g., "7B", "4B").
        /// </summary>
        [JsonProperty("params_string", NullValueHandling = NullValueHandling.Ignore)]
        public string ParamsString { get; set; }

        /// <summary>
        /// Currently loaded instances of this model.
        /// Empty if model is not loaded.
        /// </summary>
        [JsonProperty("loaded_instances")]
        public List<LoadedInstance> LoadedInstances { get; set; }

        /// <summary>
        /// Maximum context/token length supported by the model.
        /// </summary>
        [JsonProperty("max_context_length")]
        public int MaxContextLength { get; set; }

        /// <summary>
        /// Model format (e.g., "gguf", "mlx").
        /// </summary>
        [JsonProperty("format", NullValueHandling = NullValueHandling.Ignore)]
        public string Format { get; set; }

        /// <summary>
        /// Model capabilities (vision, tool use, reasoning, etc.).
        /// Only for LLM models.
        /// </summary>
        [JsonProperty("capabilities", NullValueHandling = NullValueHandling.Ignore)]
        public ModelCapabilities Capabilities { get; set; }

        /// <summary>
        /// Optional model description.
        /// </summary>
        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        /// <summary>
        /// Available model variants/quantizations.
        /// </summary>
        [JsonProperty("variants", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Variants { get; set; }

        /// <summary>
        /// Currently selected variant.
        /// </summary>
        [JsonProperty("selected_variant", NullValueHandling = NullValueHandling.Ignore)]
        public string SelectedVariant { get; set; }
    }

    /// <summary>
    /// Quantization details for a model.
    /// </summary>
    internal class QuantizationInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("bits_per_weight", NullValueHandling = NullValueHandling.Ignore)]
        public double? BitsPerWeight { get; set; }
    }

    /// <summary>
    /// Represents a loaded instance of a model.
    /// Indicates the model is actively loaded and ready for inference.
    /// </summary>
    internal class LoadedInstance
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("config")]
        public InstanceConfig Config { get; set; }
    }

    /// <summary>
    /// Configuration for a loaded model instance.
    /// </summary>
    internal class InstanceConfig
    {
        [JsonProperty("context_length")]
        public int ContextLength { get; set; }

        [JsonProperty("eval_batch_size", NullValueHandling = NullValueHandling.Ignore)]
        public int? EvalBatchSize { get; set; }

        [JsonProperty("parallel", NullValueHandling = NullValueHandling.Ignore)]
        public int? Parallel { get; set; }

        [JsonProperty("flash_attention", NullValueHandling = NullValueHandling.Ignore)]
        public bool? FlashAttention { get; set; }

        [JsonProperty("num_experts", NullValueHandling = NullValueHandling.Ignore)]
        public int? NumExperts { get; set; }

        [JsonProperty("offload_kv_cache_to_gpu", NullValueHandling = NullValueHandling.Ignore)]
        public bool? OffloadKvCacheToGpu { get; set; }
    }

    /// <summary>
    /// Model capabilities indicating supported features.
    /// </summary>
    internal class ModelCapabilities
    {
        [JsonProperty("vision")]
        public bool Vision { get; set; }

        [JsonProperty("trained_for_tool_use")]
        public bool TrainedForToolUse { get; set; }

        /// <summary>
        /// Reasoning capability details (if supported).
        /// Contains allowed options and default reasoning mode.
        /// </summary>
        [JsonProperty("reasoning", NullValueHandling = NullValueHandling.Ignore)]
        public ReasoningCapability Reasoning { get; set; }
    }

    /// <summary>
    /// Reasoning capability for models that support extended thinking.
    /// </summary>
    internal class ReasoningCapability
    {
        [JsonProperty("allowed_options", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> AllowedOptions { get; set; }

        [JsonProperty("default", NullValueHandling = NullValueHandling.Ignore)]
        public string Default { get; set; }
    }
}
