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

            var result = raw.TrimStart('\r', '\n', ' ', '\t');
            result = result.TrimEnd('\r', '\n', ' ', '\t');

            if (string.IsNullOrEmpty(result))
                return null;

            if (maxLines <= 0)
                return string.Empty;

            int pos = -1;
            for (int i = 0; i < maxLines; i++)
            {
                int idx = result.IndexOf('\n', pos + 1);
                if (idx == -1)
                    return result;
                pos = idx;
            }

            return result.Substring(0, pos);
        }
    }
}
