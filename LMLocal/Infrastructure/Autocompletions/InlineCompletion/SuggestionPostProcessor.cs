using System;
using System.Linq;

namespace LMLocal.Infrastructure.Autocompletions.InlineCompletion
{
    /// <summary>
    /// Post-processing helpers for LLM inline completion responses.
    /// </summary>
    internal static class SuggestionPostProcessor
    {
        internal static string Process(
            string raw,
            int maxLines)
        {
            if (string.IsNullOrEmpty(raw))
                return null;

            if (raw.StartsWith("\r\n"))
            {
                raw = raw.Substring(2).TrimStart(' ', '\t');
            }

            var result = raw.TrimEnd(' ', '\t');

            if (string.IsNullOrEmpty(result))
                return null;

            if (maxLines <= 0)
                return string.Empty;

            string normalized = result.Replace("\r\n", "\n").Replace("\r", "\n");

            int pos = -1;
            for (int i = 0; i < maxLines; i++)
            {
                int idx = normalized.IndexOf('\n', pos + 1);
                if (idx == -1)
                    return result;
                pos = idx;
            }

            return normalized.Substring(0, pos);
        }

        /// <summary>
        /// Multiline post-processing: line endings are always normalized,
        /// a leading LF is kept when the caret line has code, trailing whitespace and blank lines
        /// are removed, trailing lines that repeat the code after the caret are cut, and the
        /// line count is capped.
        /// </summary>
        internal static string ProcessMultiline(string raw, int maxLines, string suffix, bool caretLineIsBlank)
        {
            if (string.IsNullOrEmpty(raw) || maxLines <= 0) return null;

            string text = raw.Replace("\r\n", "\n").Replace('\r', '\n');

            if (caretLineIsBlank)
                text = text.TrimStart('\n');

            text = text.TrimEnd(' ', '\t', '\n');

            text = TrimSuffixOverlap(text, suffix, allowWholeSuggestion: caretLineIsBlank);

            var lines = text.Split('\n');
            if (lines.Length > maxLines)
                text = string.Join("\n", lines, 0, maxLines).TrimEnd(' ', '\t', '\n');

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        /// <summary>
        /// Cuts the last k lines of the suggestion when they match (ignoring surrounding whitespace)
        /// the first k non-empty lines of the suffix.
        /// </summary>
        internal static string TrimSuffixOverlap(string text, string suffix, bool allowWholeSuggestion = false)
        {
            if (string.IsNullOrEmpty(suffix) || string.IsNullOrEmpty(text)) return text;

            var sugLines = text.Split('\n');
            var sufLines = suffix.Replace("\r\n", "\n").Split('\n')
                                 .Select(l => l.Trim())
                                 .Where(l => l.Length > 0)
                                 .ToArray();

            int maxK = allowWholeSuggestion ? sugLines.Length : sugLines.Length - 1;
            for (int k = Math.Min(maxK, sufLines.Length); k > 0; k--)
            {
                bool match = true;
                for (int i = 0; i < k && match; i++)
                    match = sugLines[sugLines.Length - k + i].Trim() == sufLines[i];

                if (match)
                    return string.Join("\n", sugLines, 0, sugLines.Length - k).TrimEnd(' ', '\t', '\n');
            }
            return text;
        }
    }
}
