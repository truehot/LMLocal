namespace LMLocal.Infrastructure.Api
{
    /// <summary>
    /// Enumeration of supported model providers/backends.
    /// </summary>
    internal enum ModelProvider
    {
        /// <summary>
        /// LM Studio backend
        /// </summary>
        LmStudio,

        /// <summary>
        /// OpenAI-compatible backend or cloud
        /// </summary>
        OpenAi,

        /// <summary>
        /// Ollama backend 
        /// </summary>
        Ollama,

        /// <summary>
        /// Jan backend
        /// </summary>
        Jan,

        /// <summary>
        /// DeepSeek cloud
        /// </summary>
        DeepSeek,

        /// <summary>
        /// Google Gemini cloud
        /// </summary>
        Gemini,

        /// <summary>
        /// Github Models cloud
        /// </summary>
        GithubModelsAzure
    }
}
