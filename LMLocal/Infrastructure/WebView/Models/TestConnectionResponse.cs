using Newtonsoft.Json;

namespace LMLocal.Infrastructure.WebView.Models
{
    /// <summary>
    /// Error detail returned to the frontend. The toast in settings.dialog.js reads result.error.message.
    /// </summary>
    public class ErrorInfo
    {
        [JsonProperty("message")]
        public string Message { get; set; }
    }

    /// <summary>
    /// Response from testing a provider connection (TestConnectionAsync).
    /// </summary>
    public class TestConnectionResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("error")]
        public ErrorInfo Error { get; set; }
    }

    /// <summary>
    /// Response from testing a certificate file.
    /// </summary>
    public class TestCertificateResponse : TestConnectionResponse
    {
        [JsonProperty("thumbprint")]
        public string Thumbprint { get; set; }
    }
}
