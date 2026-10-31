using System;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// Estimates how many tokens a stream has produced so far.
    /// </summary>
    internal sealed class StreamedTokenEstimator
    {
        /// <summary>Plain prose averages roughly 4 characters per token.</summary>
        public const int DefaultAsciiCharsPerToken = 4;

        /// <summary>Cyrillic and CJK text average roughly 2 characters per token.</summary>
        public const int DefaultNonAsciiCharsPerToken = 2;

        private readonly int _asciiCharsPerToken;
        private readonly int _nonAsciiCharsPerToken;

        private int _asciiChars;
        private int _nonAsciiChars;

        /// <summary>
        /// Creates an estimator with the default prose-oriented weights.
        /// </summary>
        public StreamedTokenEstimator()
            : this(DefaultAsciiCharsPerToken, DefaultNonAsciiCharsPerToken)
        {
        }

        /// <summary>
        /// Creates an estimator with explicit weights.
        /// </summary>
        public StreamedTokenEstimator(int asciiCharsPerToken, int nonAsciiCharsPerToken)
        {
            _asciiCharsPerToken = asciiCharsPerToken > 0 ? asciiCharsPerToken : DefaultAsciiCharsPerToken;
            _nonAsciiCharsPerToken = nonAsciiCharsPerToken > 0 ? nonAsciiCharsPerToken : DefaultNonAsciiCharsPerToken;
        }

        /// <summary>
        /// Adds a piece of streamed text (content, reasoning or tool-call arguments) to the estimate.
        /// </summary>
        public void Add(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            foreach (var ch in text)
            {
                if (ch <= 0x7F)
                    _asciiChars++;
                else
                    _nonAsciiChars++;
            }
        }

        /// <summary>
        /// Estimated number of tokens produced so far.
        /// </summary>
        public int EstimatedTokens
        {
            get
            {
                if (_asciiChars + _nonAsciiChars == 0)
                    return 0;

                double tokens = (double)_asciiChars / _asciiCharsPerToken
                                + (double)_nonAsciiChars / _nonAsciiCharsPerToken;

                return Math.Max(1, (int)Math.Round(tokens));
            }
        }
    }
}
