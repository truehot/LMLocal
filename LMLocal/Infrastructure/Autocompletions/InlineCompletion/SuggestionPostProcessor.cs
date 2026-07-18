using System;

namespace LMLocal.Infrastructure.Autocompletions.InlineCompletion
{
    /// <summary>
    /// Post-processing helpers for LLM inline completion responses.
    /// </summary>
    internal static class SuggestionPostProcessor
    {
        internal static string[] Process(
            string raw,
            string prefix,
            string suffix,
            int maxLines)
        {
            if (string.IsNullOrEmpty(raw))
                return null;

            var result = raw.TrimStart('\r', '\n', ' ', '\t');
            result = result.TrimEnd('\r', '\n', ' ', '\t');

            if (string.IsNullOrEmpty(result))
                return null;

            var allLines = result.Split('\n');
            if (allLines.Length > maxLines)
            {
                var capped = new string[maxLines];
                Array.Copy(allLines, capped, maxLines);
                return capped;
            }

            return allLines;
        }
    }
}
