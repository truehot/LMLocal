namespace LMLocal.Infrastructure.LlmApi.Provider
{
    /// <summary>
    /// Standard API endpoints used across providers.
    /// </summary>
    internal static class ApiEndpoints
    {
        /// <summary>
        /// LM Studio - specific endpoint for listing models.
        /// </summary>
        public const string LmStudioListModels = "/api/v1/models";

        /// <summary>
        /// Standard OpenAI - compatible endpoint for listing models.
        /// </summary>
        public const string ListModels = "/v1/models";

        /// <summary>
        /// OpenAI - compatible endpoint for chat completions.
        /// </summary>
        public const string ChatCompletions = "/v1/chat/completions";

        /// <summary>
        /// Ollama - specific endpoint for listing running models.
        /// </summary>
        public const string OllamaRunningModels = "/api/ps";

        /// <summary>
        /// DeepSeek - specific endpoint for chat completions.https://api.deepseek.com
        /// </summary>
        public const string DeepSeekCompletions = "/chat/completions";

        /// <summary>
        /// DeepSeek - specific endpoint for listing models.https://api.deepseek.com
        /// </summary>
        public const string DeepSeekListModels = "/models";

        /// <summary>
        /// Google Gemini - specific endpoint for chat completions.https://generativelanguage.googleapis.com
        /// </summary>
        public const string GeminiCompletions = "/v1beta/openai/chat/completions";

        /// <summary>
        /// Google Gemini - specific endpoint for listing models.https://generativelanguage.googleapis.com
        /// </summary>
        public const string GeminiListModels = "/v1beta/openai/models";

        /// <summary>
        /// Github Models via Azure - specific endpoint for chat completions. https://models.inference.ai.azure.com
        /// </summary>
        public const string GithubModelsAzureCompletions = "/chat/completions";

        /// <summary>
        /// Github Models via Azure - specific endpoint for listing model. https://models.inference.ai.azure.com
        /// </summary>
        public const string GithubModelsAzureListModels = "/models";

    }
}
