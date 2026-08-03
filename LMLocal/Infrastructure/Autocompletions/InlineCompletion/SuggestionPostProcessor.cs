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
    }
}
