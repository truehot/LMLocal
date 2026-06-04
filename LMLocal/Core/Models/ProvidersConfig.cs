using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Root configuration file wrapper for custom OpenAI-compatible providers.
    /// </summary>
    public class ProvidersConfigFile : IEquatable<ProvidersConfigFile>
    {
        /// <summary>
        /// List of default built-in provider profiles.
        /// </summary>
        [JsonProperty("defaultProviders")]
        public List<CustomProvider> DefaultProviders { get; set; } = new List<CustomProvider>();

        /// <summary>
        /// List of custom user-saved provider profiles.
        /// </summary>
        [JsonProperty("providers")]
        public List<CustomProvider> Providers { get; set; } = new List<CustomProvider>();

        public bool Equals(ProvidersConfigFile other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;

            if ((DefaultProviders == null && other.DefaultProviders != null) || (DefaultProviders != null && other.DefaultProviders == null))
                return false;

            if (DefaultProviders != null && other.DefaultProviders != null)
            {
                if (DefaultProviders.Count != other.DefaultProviders.Count)
                    return false;

                for (int i = 0; i < DefaultProviders.Count; i++)
                {
                    if (!Equals(DefaultProviders[i], other.DefaultProviders[i]))
                        return false;
                }
            }

            if ((Providers == null && other.Providers != null) || (Providers != null && other.Providers == null))
                return false;

            if (Providers == null && other.Providers == null)
                return true;

            if (Providers.Count != other.Providers.Count)
                return false;

            for (int i = 0; i < Providers.Count; i++)
            {
                if (!Equals(Providers[i], other.Providers[i]))
                    return false;
            }

            return true;
        }

        public override bool Equals(object obj) => Equals(obj as ProvidersConfigFile);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + (DefaultProviders?.GetHashCode() ?? 0);
                hash = hash * 23 + (Providers?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }

    /// <summary>
    /// Custom provider profile.
    /// </summary>
    public class CustomProvider : IEquatable<CustomProvider>
    {
        /// <summary>
        /// Unique identifier for this provider profile.
        /// </summary>
        [JsonProperty("id")]
        public int Id { get; set; }

        /// <summary>
        /// Display name for this provider profile (e.g., "Work Deepseek", "OpenRouter").
        /// </summary>
        [JsonProperty("name")]
        public string ProviderName { get; set; }

        /// <summary>
        /// Provider type: "lmstudio", "ollama", or "openai".
        /// </summary>
        [JsonProperty("providerType")]
        public string ProviderType { get; set; } = "openai";

        /// <summary>
        /// Base URL for the API endpoint.
        /// </summary>
        [JsonProperty("customBaseUrl")]
        public string CustomBaseUrl { get; set; }

        /// <summary>
        /// API key for authentication with the custom provider.
        /// </summary>
        [JsonProperty("customApiKey")]
        public string CustomApiKey { get; set; }

        public bool Equals(CustomProvider other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;
            return Id == other.Id
                && string.Equals(ProviderName, other.ProviderName, StringComparison.Ordinal)
                && string.Equals(ProviderType, other.ProviderType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(CustomBaseUrl, other.CustomBaseUrl, StringComparison.OrdinalIgnoreCase)
                && string.Equals(CustomApiKey, other.CustomApiKey, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => Equals(obj as CustomProvider);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + Id.GetHashCode();
                hash = hash * 23 + (ProviderName != null ? ProviderName.GetHashCode() : 0);
                hash = hash * 23 + (ProviderType != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(ProviderType) : 0);
                hash = hash * 23 + (CustomBaseUrl != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(CustomBaseUrl) : 0);
                hash = hash * 23 + (CustomApiKey != null ? CustomApiKey.GetHashCode() : 0);
                return hash;
            }
        }
    }
}
