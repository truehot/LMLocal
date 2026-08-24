using System;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// Result of matching a single line: whether it contains the query (substring, case-insensitive), whether the query is an exact whole word at that position, and what declaration kind the line is.
    /// </summary>
    internal readonly struct LineMatch
    {
        public readonly bool IsMatch;
        public readonly bool IsExactWord;
        public readonly SearchMatchKind Kind;

        public LineMatch(bool isMatch, bool isExactWord, SearchMatchKind kind)
        {
            IsMatch = isMatch;
            IsExactWord = isExactWord;
            Kind = kind;
        }

        public static readonly LineMatch NoMatch = new LineMatch(false, false, SearchMatchKind.Other);
    }

    /// <summary>
    /// Orchestrates line matching for search_file_content: substring recall + exact-word detection + declaration classification.
    /// </summary>
    internal static class ContentSearchMatcher
    {
        public static LineMatch Match(string line, string query, string extension, bool computeExactWord)
        {
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(query))
                return LineMatch.NoMatch;

            int index = line.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return LineMatch.NoMatch;

            bool exactWord = computeExactWord && IsExactWord(line, query, index);
            SearchMatchKind kind = DeclarationMatcher.Classify(extension, line);
            return new LineMatch(true, exactWord, kind);
        }

        /// <summary>
        /// True if the query at <paramref name="index"/> is surrounded by non-identifier characters (or string boundaries).
        /// </summary>
        public static bool IsExactWord(string line, string query, int index)
        {
            int before = index - 1;
            int after = index + query.Length;
            bool leftOk = before < 0 || !IsIdentifierChar(line[before]);
            bool rightOk = after >= line.Length || !IsIdentifierChar(line[after]);
            return leftOk && rightOk;
        }

        private static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '$';
        }
    }
}
