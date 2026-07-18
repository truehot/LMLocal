namespace LMLocal.Models
{
    /// <summary>
    /// Request payload for testing FIM completion against a specified provider and model.
    /// </summary>
    public class TestCompletionRequest
    {
        /// <summary>
        /// Provider type: "lmstudio", "ollama", or "openai"
        /// </summary>
        public string ProviderType { get; set; }

        /// <summary>
        /// Base URL of the provider backend (e.g., "http://localhost:1234")
        /// </summary>
        public string BaseUrl { get; set; }

        /// <summary>
        /// Optional API key for authentication
        /// </summary>
        public string ApiKey { get; set; }

        /// <summary>
        /// Model ID to test (e.g., "ibm/granite-4-micro")
        /// </summary>
        public string ModelId { get; set; }
    }
}
