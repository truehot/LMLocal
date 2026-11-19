namespace LMLocal.Infrastructure.WebView
{
    /// <summary>
    /// Tells the UI that the backend is waiting out a provider rate limit before retrying the request.
    /// </summary>

    internal class WebView2RateLimitWaitMessage : WebView2ScriptMessage
    {
        public WebView2RateLimitWaitMessage()
        {
            Type = WebView2MessageType.RateLimitWait;
        }

        /// <summary>Identifier of this wait, so an End cannot clear a banner it does not own.</summary>
        public string WaitId { get; set; }

        /// <summary>1-based number of the attempt that starts after this wait.</summary>
        public int Attempt { get; set; }

        /// <summary>Total attempts allowed for this round.</summary>
        public int MaxAttempts { get; set; }

        /// <summary>Seconds the backend is waiting.</summary>
        public double DelaySeconds { get; set; }

        /// <summary>Where the delay came from: "header", "body" or "fallback".</summary>
        public string Source { get; set; }

        /// <summary>"Begin" while the delay is pending, "End" once it is over.</summary>
        public string Phase { get; set; }
    }
}
