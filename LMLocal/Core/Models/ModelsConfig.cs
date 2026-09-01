using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Root configuration file wrapper for user-defined model profiles.
    /// </summary>
    public class ModelsConfigFile : IEquatable<ModelsConfigFile>
    {
        /// <summary>
        /// List of user-saved model profiles.
        /// </summary>
        [JsonProperty("models")]
        public List<ModelDefinition> Models { get; set; } = new List<ModelDefinition>();

        public bool Equals(ModelsConfigFile other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;

            if ((Models == null && other.Models != null) || (Models != null && other.Models == null))
                return false;

            if (Models == null && other.Models == null)
                return true;

            if (Models.Count != other.Models.Count)
                return false;

            for (int i = 0; i < Models.Count; i++)
            {
                if (!Equals(Models[i], other.Models[i]))
                    return false;
            }

            return true;
        }

        public override bool Equals(object obj) => Equals(obj as ModelsConfigFile);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + (Models?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// Custom model profile. Overrides or extends the model information reported by the provider.
    /// </summary>
    public class ModelDefinition : IEquatable<ModelDefinition>
    {
        /// <summary>
        /// Internal auto-generated identifier of this profile. Not editable in the UI.
        /// </summary>
        [JsonProperty("id")]
        public int Id { get; set; }

        /// <summary>
        /// Model ID exactly as reported by the provider (e.g. "qwen2.5-coder-32b-instruct").
        /// </summary>
        [JsonProperty("modelId")]
        public string ModelId { get; set; }

        /// <summary>
        /// Provider type key: "lmstudio", "ollama", "openai", etc.
        /// </summary>
        [JsonProperty("providerType")]
        public string ProviderType { get; set; } = "openai";

        /// <summary>
        /// Id of the provider profile this model belongs to. Null = default profile of the type.
        /// </summary>
        [JsonProperty("providerId", NullValueHandling = NullValueHandling.Ignore)]
        public int? ProviderId { get; set; }

        /// <summary>
        /// Display name shown in the model list. Required for custom models,
        /// because the provider cannot report a name for a model it does not serve.
        /// </summary>
        [JsonProperty("displayName", NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayName { get; set; }

        /// <summary>
        /// Maximum context length override. Null = use the value reported by the provider.
        /// </summary>
        [JsonProperty("contextLength", NullValueHandling = NullValueHandling.Ignore)]
        public int? ContextLength { get; set; }

        /// <summary>
        /// Maximum tokens to generate. Null = not set.
        /// </summary>
        [JsonProperty("maxTokens", NullValueHandling = NullValueHandling.Ignore)]
        public int? MaxTokens { get; set; }

        /// <summary>
        /// Reasoning effort hint: "none", "low", "medium", "high", "max". Null = not set.
        /// </summary>
        [JsonProperty("reasoningEffort", NullValueHandling = NullValueHandling.Ignore)]
        public string ReasoningEffort { get; set; }

        /// <summary>
        /// When true, the model is added manually and is not served by the provider.
        /// </summary>
        [JsonProperty("isCustom")]
        public bool IsCustom { get; set; }

        /// <summary>
        /// When false, the profile is ignored when enriching the provider model list.
        /// </summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        public bool Equals(ModelDefinition other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;
            return Id == other.Id
                && string.Equals(ModelId, other.ModelId, StringComparison.Ordinal)
                && string.Equals(ProviderType, other.ProviderType, StringComparison.OrdinalIgnoreCase)
                && ProviderId == other.ProviderId
                && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal)
                && ContextLength == other.ContextLength
                && MaxTokens == other.MaxTokens
                && string.Equals(ReasoningEffort, other.ReasoningEffort, StringComparison.OrdinalIgnoreCase)
                && IsCustom == other.IsCustom
                && Enabled == other.Enabled;
        }

        public override bool Equals(object obj) => Equals(obj as ModelDefinition);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + Id.GetHashCode();
                hash = hash * 23 + (ModelId != null ? ModelId.GetHashCode() : 0);
                hash = hash * 23 + (ProviderType != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(ProviderType) : 0);
                hash = hash * 23 + (ProviderId.HasValue ? ProviderId.Value.GetHashCode() : 0);
                hash = hash * 23 + (DisplayName != null ? DisplayName.GetHashCode() : 0);
                hash = hash * 23 + (ContextLength.HasValue ? ContextLength.Value.GetHashCode() : 0);
                hash = hash * 23 + (MaxTokens.HasValue ? MaxTokens.Value.GetHashCode() : 0);
                hash = hash * 23 + (ReasoningEffort != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(ReasoningEffort) : 0);
                hash = hash * 23 + IsCustom.GetHashCode();
                hash = hash * 23 + Enabled.GetHashCode();
                return hash;
            }
        }
    }
}
