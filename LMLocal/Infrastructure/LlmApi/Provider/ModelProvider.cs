namespace LMLocal.Infrastructure.LlmApi.Provider
{
    /// <summary>
    /// Enumeration of supported model providers/backends.
    /// Each value carries a <see cref="ProviderDisplayAttribute"/> with the human-readable name shown in the UI.
    /// Order: local providers first, then OpenAI (local or cloud), then cloud-only providers.
    /// </summary>
    internal enum ModelProvider
    {
        /// <summary>
        /// LM Studio backend
        /// </summary>
        [ProviderDisplay("LM Studio (local)")]
        LmStudio,

        /// <summary>
        /// Ollama backend 
        /// </summary>
        [ProviderDisplay("Ollama (local)")]
        Ollama,

        /// <summary>
        /// Jan backend
        /// </summary>
        [ProviderDisplay("Jan (local)")]
        Jan,

        /// <summary>
        /// llama.cpp local server backend
        /// </summary>
        [ProviderDisplay("Llama.cpp (local)")]
        LlamaCpp,

        /// <summary>
        /// OpenAI-compatible backend or cloud
        /// </summary>
        [ProviderDisplay("OpenAI compatible")]
        OpenAi,

        /// <summary>
        /// DeepSeek cloud
        /// </summary>
        [ProviderDisplay("DeepSeek (cloud)")]
        DeepSeek,

        /// <summary>
        /// Google Gemini cloud
        /// </summary>
        [ProviderDisplay("Gemini (cloud)")]
        Gemini,

        /// <summary>
        /// Github Models cloud
        /// </summary>
        [ProviderDisplay("Deprecated!!! Github Models via Azure (cloud)")]
        GithubModelsAzure,

        /// <summary>
        /// Together AI cloud
        /// </summary>
        [ProviderDisplay("Together AI (cloud)")]
        TogetherAi,
    }
}
