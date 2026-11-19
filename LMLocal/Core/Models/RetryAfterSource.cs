namespace LMLocal.Core.Models
{
    /// <summary>
    /// Where <see cref="ApiErrorInfo.RetryAfterSeconds"/> came from.
    /// </summary>
    public enum RetryAfterSource
    {
        /// <summary>No server hint was available.</summary>
        None = 0,

        /// <summary>Parsed from the error response body (provider metadata).</summary>
        Body = 1,

        /// <summary>Parsed from the <c>Retry-After</c> HTTP header.</summary>
        Header = 2,

        /// <summary>
        /// Policy default, used when the server sent no hint at all.
        /// </summary>
        Fallback = 3
    }
}
