using System;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// Estimates how many tokens a stream has produced so far.
    /// </summary>
    internal sealed class StreamedTokenEstimator
    {
        /// <summary>English text, JSON and code average roughly 4 characters per token.</summary>
        private const int AsciiCharsPerToken = 4;

        /// <summary>Cyrillic and CJK text average roughly 2 characters per token.</summary>
        private const int NonAsciiCharsPerToken = 2;

        private int _asciiChars;
        private int _nonAsciiChars;

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

                double tokens = (double)_asciiChars / AsciiCharsPerToken
                                + (double)_nonAsciiChars / NonAsciiCharsPerToken;

                return Math.Max(1, (int)Math.Round(tokens));
            }
        }
    }
}
