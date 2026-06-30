using System;

namespace LMLocal.Infrastructure.LlmApi.Provider
{
    /// <summary>
    /// Attribute to attach a human-readable display name to ModelProvider enum values.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    internal class ProviderDisplayAttribute : Attribute
    {
        /// <summary>
        /// Human-readable display name shown in the UI (e.g. "LM Studio (local)").
        /// </summary>
        public string DisplayName { get; }

        public ProviderDisplayAttribute(string displayName)
        {
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        }
    }
}
