namespace LMLocal.Core.Models
{
    /// <summary>
    /// Contains token generation statistics for a single streaming chunk.
    /// </summary>
    internal readonly struct TokenGenerationStats
    {
        /// <summary>
        /// Total tokens generated so far.
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
