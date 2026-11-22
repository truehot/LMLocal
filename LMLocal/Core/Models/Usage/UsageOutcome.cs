namespace LMLocal.Core.Models.Usage
{
    /// <summary>
    /// How a request ended. Cancelled and failed requests spent tokens too and are counted by the stats.
    /// </summary>
    public enum UsageOutcome
    {
        /// <summary>The request finished normally.</summary>
        Ok,

        /// <summary>The user stopped the request; not treated as a failure.</summary>
        Cancelled,

        /// <summary>A subagent exceeded its time budget.</summary>
        Timeout,

        /// <summary>The model stopped on "length" or "content_filter".</summary>
        Incomplete,

        /// <summary>A stream or provider error, an exception, or an unexpected subagent cancellation.</summary>
        Error
    }
}
