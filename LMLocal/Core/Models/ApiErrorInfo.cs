namespace LMLocal.Core.Models
{
    /// <summary>
    /// Structured error information parsed from LLM API responses.Supports OpenAI, Anthropic, OpenRouter, Venice, FastAPI/Django REST, and generic JSON error formats.
    /// </summary>
    public class ApiErrorInfo
    {
        /// <summary>
        /// Human-readable error message extracted from the response body.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// HTTP status code or provider-specific error code.
        /// </summary>
        public int? Code { get; set; }

        /// <summary>
        /// Seconds to wait before retrying (from 429 or provider metadata).
        /// </summary>
        public double? RetryAfterSeconds { get; set; }

        /// <summary>
        /// Provider that returned the error (from metadata.provider_name).
        /// </summary>
        public string Provider { get; set; }

        /// <summary>
        /// Raw metadata JSON for debugging when parsing fails.
        /// </summary>
        public string RawMetadata { get; set; }

        /// <summary>
        /// True if this is a rate-limit error (HTTP 429 or has Retry-After).
        /// </summary>
        public bool IsRateLimit => Code == 429 || RetryAfterSeconds.HasValue;

        /// <summary>
        /// User-friendly summary for logging.
        /// </summary>
        public override string ToString()
        {
            var providerPart = string.IsNullOrEmpty(Provider) ? "" : $" [{Provider}]";
            var codePart = Code.HasValue ? $" (code={Code.Value})" : "";
            return $"{Message}{codePart}{providerPart}";
        }
    }
}
