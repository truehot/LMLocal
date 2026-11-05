using System;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Matches a configured model profile against the model that actually serves the current request.
    /// </summary>
    internal static class ModelProfileLookup
    {
        /// <summary>
        /// Returns the enabled profile for the given model id and provider, or null when there is none.
        /// </summary>
        public static ModelDefinition Find(ModelsConfigFile config, string modelId, string providerType, int? providerId)
        {
            if (config == null || config.Models == null || config.Models.Count == 0)
                return null;

            if (string.IsNullOrWhiteSpace(modelId))
                return null;

            foreach (var profile in config.Models)
            {
                if (profile == null || !profile.Enabled)
                    continue;

                if (!string.Equals(profile.ModelId, modelId, StringComparison.Ordinal))
                    continue;

                if (!string.Equals(profile.ProviderType, providerType, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!Nullable.Equals(profile.ProviderId, providerId))
                    continue;

                return profile;
            }

            return null;
        }
    }
}
