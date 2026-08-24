using System;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// Decides whether a search query looks like an identifier (single token, e.g. a class or method name) versus a free-text phrase.
    /// </summary>
    internal static class QueryClassifier
    {
        public static bool IsIdentifierQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return false;

            for (int i = 0; i < query.Length; i++)
            {
                char c = query[i];
                if (char.IsWhiteSpace(c))
                    return false;
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-' || c == '$'))
                    return false;
            }

            return true;
        }
    }
}
