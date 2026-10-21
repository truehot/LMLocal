namespace LMLocal.Core.Models
{
    /// <summary>
    /// Contains token generation statistics for a single streaming chunk.
    /// </summary>
    internal readonly struct TokenGenerationStats
    {
        /// <summary>
        /// Estimated number of tokens generated so far. This is a character-based estimate:
        /// exact usage arrives only with the stream completion, so the live value cannot be authoritative.
        /// </summary>
        public int TotalTokens { get; }

        /// <summary>
        /// Current generation speed in tokens per second.
        /// </summary>
        public double TokensPerSecond { get; }

        public TokenGenerationStats(int totalTokens, double tokensPerSecond)
        {
            TotalTokens = totalTokens;
            TokensPerSecond = tokensPerSecond;
        }
    }
}
