namespace LMLocal.Application.Autocompletions
{
    /// <summary>
    /// Encapsulates all parameters for a FIM (Fill-In-the-Middle) completion request,
    /// </summary>
    internal class CompletionContext
    {
        /// <summary>Model ID to use (passed as "model" in the request body).</summary>
        public string ModelId { get; set; }

        /// <summary>Prefix text (code before cursor).</summary>
        public string Prompt { get; set; }

        /// <summary>Suffix text (code after cursor).</summary>
        public string Suffix { get; set; }

        /// <summary>Maximum tokens to generate.</summary>
        public int MaxTokens { get; set; }

        /// <summary>Temperature for sampling (0.0–2.0).</summary>
        public double Temperature { get; set; }

        /// <summary>Stop sequences.</summary>
        public string[] Stop { get; set; }

        /// <summary>Base URL for the API endpoint (e.g. "http://localhost:1234").</summary>
        public string BaseUrl { get; set; }

        /// <summary>API key for authentication.</summary>
        public string ApiKey { get; set; }

        /// <summary>Provider type string (e.g. "lmstudio", "openai").</summary>
        public string ProviderType { get; set; }
    }
}
