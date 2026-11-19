using System;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// Texts the app writes into the chat history and into the chat log itself instead of the user's own words.
    /// None of them is a prompt: the model cannot be asked them again.
    /// </summary>
    public static class ChatMessagePlaceholders
    {
        /// <summary>
        /// Asks the model for the summary that replaces the compacted part of the session.
        /// </summary>
        public const string SummaryRequest = "Provide a brief summary of our previous session to continue.";

        /// <summary>
        /// Carries a forced mid-session summary in the compacted history, in front of the real last prompt.
        /// </summary>
        public const string ForcedSummaryUser = "Summary of the earlier part of this session:";

        /// <summary>
        /// Tail shared by every image placeholder.
        /// </summary>
        private const string ImagePlaceholderSuffix = " attached - not available in this session]";

        /// <summary>
        /// Placeholder written instead of the images of a message. Base64 never reaches the log.
        /// </summary>
        public static string FormatImagePlaceholder(int count)
        {
            return count == 1
                ? "[1 image" + ImagePlaceholderSuffix
                : $"[{count} images" + ImagePlaceholderSuffix;
        }

        /// <summary>
        /// True when the <b>last line</b> of the text is <see cref="FormatImagePlaceholder"/> for any image count.
        /// </summary>
        public static bool IsImagePlaceholder(string text)
        {
            var lastLine = GetLastLine(text);
            if (lastLine.Length < 2)
                return false;

            if (!lastLine.StartsWith("[", StringComparison.Ordinal) ||
                !lastLine.EndsWith(ImagePlaceholderSuffix, StringComparison.Ordinal))
                return false;

            int digitsEnd = lastLine.IndexOf(' ');
            if (digitsEnd <= 1)
                return false;

            return int.TryParse(lastLine.Substring(1, digitsEnd - 1), out int count) && count > 0;
        }

        /// <summary>
        /// Last non-empty line of the text, trimmed.
        /// </summary>
        private static string GetLastLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var value = text.TrimEnd();
            int newlineIndex = value.LastIndexOf('\n');

            return (newlineIndex >= 0 ? value.Substring(newlineIndex + 1) : value).Trim();
        }
    }
}
