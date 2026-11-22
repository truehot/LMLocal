using System;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// Truncation rules for text read from the chat log and shown in the UI (session prompt, search heading).
    /// </summary>
    public static class PromptText
    {
        /// <summary>
        /// Maximum length of a session prompt shown in the history dialog before truncation.
        /// </summary>
        public const int MaxPromptLength = 200;

        /// <summary>
        /// Truncates a session prompt to <see cref="MaxPromptLength"/> chars, appending an ellipsis when truncated.
        /// </summary>
        public static string TruncatePrompt(string content) => Truncate(content, MaxPromptLength);

        /// <summary>
        /// Truncates <paramref name="content"/> to <paramref name="maxLength"/> chars, appending an ellipsis when truncated.
        /// </summary>
        public static string Truncate(string content, int maxLength)
        {
            if (string.IsNullOrEmpty(content)) return string.Empty;
            if (maxLength < 0) throw new ArgumentOutOfRangeException(nameof(maxLength));

            return content.Length > maxLength
                ? content.Substring(0, maxLength) + "..."
                : content;
        }
    }
}
