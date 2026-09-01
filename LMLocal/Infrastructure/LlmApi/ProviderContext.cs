namespace LMLocal.Infrastructure.LlmApi
{
    /// <summary>
    /// Explicit provider connection for a single chat request. 
    /// </summary>
    internal class ProviderContext
    {
        /// <summary>Provider type key (e.g. "lmstudio", "openai", "deepseek").</summary>
        public string ProviderType { get; set; }

        /// <summary>Base URL override for the API endpoint.</summary>
        public string BaseUrl { get; set; }

        /// <summary>API key override for authentication.</summary>
        public string ApiKey { get; set; }
    }
}
