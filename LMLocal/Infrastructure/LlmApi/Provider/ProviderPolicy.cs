using LMLocal.Application.Abstractions.Ports;

namespace LMLocal.Infrastructure.LlmApi.Provider
{
    /// <summary>Answers provider questions from <see cref="ProviderResolver"/>.</summary>
    internal class ProviderPolicy : IProviderPolicy
    {
        /// <summary>True only for LlamaCpp, resolved like everywhere else (unknown names fall back to LmStudio).</summary>
        public bool RequiresStrictRoleAlternation(string providerName)
        {
            return ProviderResolver.ResolveProvider(providerName) == ModelProvider.LlamaCpp;
        }
    }
}
