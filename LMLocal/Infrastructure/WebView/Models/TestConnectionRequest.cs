namespace LMLocal.Models
{
    public class TestConnectionRequest
    {
        /// <summary>
        /// Provider name: "lmstudio", "ollama", or "openai"
        /// </summary>
        public string Provider { get; set; }

        /// <summary>
        /// Base URL of the provider backend (e.g., "http://localhost:1234")
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Optional API key for authentication
        /// </summary>
        public string ApiKey { get; set; }

        /// <summary>
        /// Optional path to a server certificate (.cer/.crt/.pem) used to trust
        /// a self-signed/private-CA HTTPS endpoint. Empty = default trust.
        /// </summary>
        public string CertificatePath { get; set; }
    }
}
