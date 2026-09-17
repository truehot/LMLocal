using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// Stateless scoring for search_solution_knowledge.
    /// </summary>
    internal static class KnowledgeScorer
    {
        private static readonly Regex TokenRegex = new Regex(
            @"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)*",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Splits a query into unique tokens. 
        /// </summary>
        public static string[] TokenizeQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<string>();

            return TokenRegex.Matches(query)
                .Cast<Match>()
                .Select(m => m.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// Normalizes CRLF/CR line endings to LF so snippet offsets line up with the lines produced by <c>Split('\\n')</c>.
        /// </summary>
        public static string NormalizeLineEndings(string content)
        {
            return content?.Replace("\r\n", "\n").Replace('\r', '\n') ?? string.Empty;
        }

        /// <summary>
        /// Collects every relevance signal for a file without choosing a winner.
        /// </summary>
        public static KnowledgeAnalysis Analyze(string fileName, string normalizedContent, string query)
        {
            string[] tokens = TokenizeQuery(query);
            if (tokens.Length == 0)
                return KnowledgeAnalysis.None;

            var (filenameKind, filenameSubScore) = ScoreFilename(fileName, tokens);

            if (string.IsNullOrEmpty(normalizedContent))
                return new KnowledgeAnalysis(
                    filenameKind, filenameSubScore,
                    headingExactCount: 0, bestHeadingExactIndex: -1, bestHeadingExactLine: 0, bestHeadingExactHeading: null,
                    bodyExactOccurrences: 0, bestBodyExactIndex: -1, bestBodyExactLine: 0, bestBodyExactHeading: null,
                    sectionsWithAllTokens: 0, bestSectionIndex: -1, bestSectionLine: 0, bestSectionHeading: null);

            bool multiWord = tokens.Length > 1;

            // Per-section token accumulation.
            string sectionHeading = null;
            var sectionTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int sectionFirstTokenIndex = -1;
            int sectionFirstTokenLine = 0;

            // Exact-phrase signals.
            int headingExactCount = 0;
            int bestHeadingExactIndex = -1;
            int bestHeadingExactLine = 0;
            string bestHeadingExactHeading = null;

            int bodyExactOccurrences = 0;
            int bestBodyExactIndex = -1;
            int bestBodyExactLine = 0;
            string bestBodyExactHeading = null;

            // Section all-tokens signal.
            int sectionsWithAllTokens = 0;
            int bestSectionIndex = -1;
            int bestSectionLine = 0;
            string bestSectionHeading = null;

            int charIndex = 0;
            int currentLine = 1;

            using (var reader = new StringReader(normalizedContent))
            {
                string rawLine;
                while ((rawLine = reader.ReadLine()) != null)
                {
                    string lineText = rawLine.Trim();
                    int leadingWhitespace = rawLine.Length - rawLine.TrimStart().Length;
                    bool isHeading = IsHeadingLine(lineText);

                    if (isHeading)
                    {
                        int headingPhraseIndex;
                        if (tokens.Length == 1)
                        {
                            if (!TryIndexOfTokenWithWordBoundaries(lineText, query, out headingPhraseIndex))
                                headingPhraseIndex = -1;
                        }
                        else
                        {
                            headingPhraseIndex = lineText.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                        }

                        if (headingPhraseIndex >= 0)
                        {
                            headingExactCount++;
                            if (bestHeadingExactIndex < 0)
                            {
                                bestHeadingExactIndex = charIndex + leadingWhitespace + headingPhraseIndex;
                                bestHeadingExactLine = currentLine;
                                bestHeadingExactHeading = lineText.TrimStart('#').Trim();
                            }
                        }

                        // Close out the previous section before starting the new one.
                        if (multiWord && sectionTokens.Count >= tokens.Length)
                        {
                            sectionsWithAllTokens++;
                            if (bestSectionIndex < 0)
                            {
                                bestSectionIndex = sectionFirstTokenIndex;
                                bestSectionLine = sectionFirstTokenLine;
                                bestSectionHeading = sectionHeading;
                            }
                        }

                        // A logical section includes its heading, so the heading line participates in the all-token scan as well.
                        sectionHeading = lineText.TrimStart('#').Trim();
                        sectionTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        sectionFirstTokenIndex = -1;
                        sectionFirstTokenLine = 0;

                        if (multiWord)
                        {
                            AccumulateSectionTokens(
                                lineText, charIndex + leadingWhitespace, currentLine,
                                tokens, sectionTokens, ref sectionFirstTokenIndex, ref sectionFirstTokenLine);
                        }
                    }
                    else
                    {
                        if (tokens.Length == 1)
                        {
                            // Single-word query: require word boundaries to avoid false positives
                            // like "api" matching inside "rapid".
                            int count = CountWordBoundedOccurrences(lineText, query, out int firstIdx);
                            if (count > 0)
                            {
                                bodyExactOccurrences += count;
                                if (bestBodyExactIndex < 0)
                                {
                                    bestBodyExactIndex = charIndex + leadingWhitespace + firstIdx;
                                    bestBodyExactLine = currentLine;
                                    bestBodyExactHeading = sectionHeading;
                                }
                            }
                        }
                        else
                        {
                            int count = CountOccurrences(lineText, query, out int firstIdx);
                            if (count > 0)
                            {
                                bodyExactOccurrences += count;
                                if (bestBodyExactIndex < 0)
                                {
                                    bestBodyExactIndex = charIndex + leadingWhitespace + firstIdx;
                                    bestBodyExactLine = currentLine;
                                    bestBodyExactHeading = sectionHeading;
                                }
                            }
                        }

                        // Always accumulate tokens: a body line containing the exact phrase is still part of its section, symmetric with the heading branch.
                        if (multiWord)
                        {
                            AccumulateSectionTokens(
                                lineText, charIndex + leadingWhitespace, currentLine,
                                tokens, sectionTokens, ref sectionFirstTokenIndex, ref sectionFirstTokenLine);
                        }
                    }

                    charIndex += rawLine.Length + 1;
                    currentLine++;
                }
            }

            // Close out the final (open) section.
            if (multiWord && sectionTokens.Count >= tokens.Length)
            {
                sectionsWithAllTokens++;
                if (bestSectionIndex < 0)
                {
                    bestSectionIndex = sectionFirstTokenIndex;
                    bestSectionLine = sectionFirstTokenLine;
                    bestSectionHeading = sectionHeading;
                }
            }

            return new KnowledgeAnalysis(
                filenameKind, filenameSubScore,
                headingExactCount, bestHeadingExactIndex, bestHeadingExactLine, bestHeadingExactHeading,
                bodyExactOccurrences, bestBodyExactIndex, bestBodyExactLine, bestBodyExactHeading,
                sectionsWithAllTokens, bestSectionIndex, bestSectionLine, bestSectionHeading);
        }

        /// <summary>
        /// Scores a file by selecting the highest tier reached.
        /// </summary>
        public static KnowledgeScoreResult Score(string fileName, string normalizedContent, string query)
        {
            var a = Analyze(fileName, normalizedContent, query);

            KnowledgeMatchKind bestKind = a.FilenameKind;
            int bestSubScore = a.FilenameSubScore;
            string bestHeading = null;
            int bestIndex = -1;
            int bestLine = 0;

            if (a.HeadingExactCount > 0 && KnowledgeMatchKind.HeadingExact > bestKind)
            {
                bestKind = KnowledgeMatchKind.HeadingExact;
                bestSubScore = a.HeadingExactCount;
                bestHeading = a.BestHeadingExactHeading;
                bestIndex = a.BestHeadingExactIndex;
                bestLine = a.BestHeadingExactLine;
            }

            if (a.BodyExactOccurrences > 0 && KnowledgeMatchKind.BodyExact > bestKind)
            {
                bestKind = KnowledgeMatchKind.BodyExact;
                bestSubScore = a.BodyExactOccurrences;
                bestHeading = a.BestBodyExactHeading;
                bestIndex = a.BestBodyExactIndex;
                bestLine = a.BestBodyExactLine;
            }

            if (a.SectionsWithAllTokens > 0 && KnowledgeMatchKind.SectionAllTokens > bestKind)
            {
                bestKind = KnowledgeMatchKind.SectionAllTokens;
                bestSubScore = a.SectionsWithAllTokens;
                bestHeading = a.BestSectionHeading;
                bestIndex = a.BestSectionIndex;
                bestLine = a.BestSectionLine;
            }

            return new KnowledgeScoreResult(bestKind, bestSubScore, bestHeading, bestIndex, bestLine);
        }

        /// <summary>
        /// Scores a file name against query tokens.
        /// </summary>
        public static (KnowledgeMatchKind Kind, int SubScore) ScoreFilename(string fileName, string[] tokens)
        {
            if (string.IsNullOrEmpty(fileName) || tokens == null || tokens.Length == 0)
                return (KnowledgeMatchKind.None, 0);

            string nameOnly = Path.GetFileNameWithoutExtension(fileName);
            if (nameOnly.Length == 0)
                return (KnowledgeMatchKind.None, 0);

            int boundaryCount = 0;
            int matchedCount = 0;

            foreach (string token in tokens)
            {
                if (TryIndexOfTokenWithWordBoundaries(nameOnly, token, out _))
                {
                    boundaryCount++;
                    matchedCount++;
                }
                else if (nameOnly.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matchedCount++;
                }
            }

            if (matchedCount == 0)
                return (KnowledgeMatchKind.None, 0);

            if (matchedCount == tokens.Length && boundaryCount == tokens.Length)
                return (KnowledgeMatchKind.FilenameExact, matchedCount);

            return (KnowledgeMatchKind.FilenameSubstring, matchedCount);
        }

        /// <summary>
        /// Builds a snippet around <paramref name="centerIndex"/> with an approximate <paramref name="radius"/>.<paramref name="content"/> is the full content string.
        /// </summary>
        public static string BuildSnippet(string content, int centerIndex, int radius)
        {
            if (string.IsNullOrEmpty(content) || centerIndex < 0)
                return string.Empty;
            if (radius < 0)
                throw new ArgumentOutOfRangeException(nameof(radius));

            int index = Math.Max(0, Math.Min(centerIndex, content.Length - 1));
            int start = Math.Max(0, index - radius);
            int end = Math.Min(content.Length, index + radius);

            var sb = new StringBuilder(end - start + 6);
            if (start > 0)
                sb.Append("...");
            sb.Append(content, start, end - start);
            if (end < content.Length)
                sb.Append("...");
            return sb.ToString();
        }

        public static bool ContainsTokenWithWordBoundaries(string text, string token)
        {
            return TryIndexOfTokenWithWordBoundaries(text, token, out _);
        }

        /// <summary>
        /// Case-insensitive token search requiring word boundaries on both sides.
        /// </summary>
        public static bool TryIndexOfTokenWithWordBoundaries(string text, string token, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token))
                return false;

            int i = 0;
            while ((i = text.IndexOf(token, i, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                bool leftBoundary = i == 0 || !IsWordChar(text[i - 1]);
                bool rightBoundary = i + token.Length >= text.Length || !IsWordChar(text[i + token.Length]);

                if (leftBoundary && rightBoundary)
                {
                    index = i;
                    return true;
                }

                i += token.Length;
            }

            return false;
        }

        private static void AccumulateSectionTokens(
            string lineText,
            int lineStartIndex,
            int currentLine,
            string[] tokens,
            HashSet<string> sectionTokens,
            ref int sectionFirstTokenIndex,
            ref int sectionFirstTokenLine)
        {
            foreach (string token in tokens)
            {
                if (sectionTokens.Contains(token))
                    continue;

                if (TryIndexOfTokenWithWordBoundaries(lineText, token, out int tokenIndex))
                {
                    sectionTokens.Add(token);
                    if (sectionFirstTokenIndex < 0)
                    {
                        sectionFirstTokenIndex = lineStartIndex + tokenIndex;
                        sectionFirstTokenLine = currentLine;
                    }
                }
            }
        }

        /// <summary>
        /// Counts occurrences of <paramref name="query"/> (plain substring) in <paramref name="text"/>
        /// and sets <paramref name="firstIndex"/> to the position of the first occurrence, or -1 if none.
        /// Single pass — no double scan.
        /// </summary>
        private static int CountOccurrences(string text, string query, out int firstIndex)
        {
            firstIndex = -1;
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
                return 0;

            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(query, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                if (firstIndex < 0)
                    firstIndex = index;
                count++;
                index += query.Length;
            }
            return count;
        }

        /// <summary>
        /// Counts occurrences of <paramref name="token"/> bounded by word boundaries,
        /// and sets <paramref name="firstIndex"/> to the position of the first occurrence, or -1 if none.
        /// </summary>
        private static int CountWordBoundedOccurrences(string text, string token, out int firstIndex)
        {
            firstIndex = -1;
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token))
                return 0;

            int count = 0;
            int i = 0;
            while (i < text.Length)
            {
                int found = text.IndexOf(token, i, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                    break;

                bool leftBoundary = found == 0 || !IsWordChar(text[found - 1]);
                bool rightBoundary = found + token.Length >= text.Length || !IsWordChar(text[found + token.Length]);

                if (leftBoundary && rightBoundary)
                {
                    if (firstIndex < 0)
                        firstIndex = found;
                    count++;
                }

                i = found + token.Length;
            }
            return count;
        }

        /// <summary>
        /// Returns true if <paramref name="trimmed"/> is a markdown heading (1-6 <c>#</c> followed by space or tab).
        /// Complies with CommonMark spec: <c>#foo</c> is NOT a heading.
        /// </summary>
        private static bool IsHeadingLine(string trimmed)
        {
            if (string.IsNullOrEmpty(trimmed) || trimmed[0] != '#')
                return false;

            int hashCount = 0;
            while (hashCount < trimmed.Length && trimmed[hashCount] == '#')
                hashCount++;

            if (hashCount < 1 || hashCount > 6)
                return false;

            // "###" alone — fringe case, accept as heading (ATX heading can be just hashes in some parsers).
            if (hashCount == trimmed.Length)
                return true;

            // CommonMark: after ### there must be a space or tab.
            return trimmed[hashCount] == ' ' || trimmed[hashCount] == '\t';
        }

        private static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c);
        }
    }

    /// <summary>
    /// Raw, independent relevance signals collected by <see cref="KnowledgeScorer.Analyze"/> .
    /// </summary>
    internal readonly struct KnowledgeAnalysis
    {
        public KnowledgeMatchKind FilenameKind { get; }
        public int FilenameSubScore { get; }

        public int HeadingExactCount { get; }
        public int BestHeadingExactIndex { get; }
        public int BestHeadingExactLine { get; }
        public string BestHeadingExactHeading { get; }

        public int BodyExactOccurrences { get; }
        public int BestBodyExactIndex { get; }
        public int BestBodyExactLine { get; }
        public string BestBodyExactHeading { get; }

        public int SectionsWithAllTokens { get; }
        public int BestSectionIndex { get; }
        public int BestSectionLine { get; }
        public string BestSectionHeading { get; }

        public KnowledgeAnalysis(
            KnowledgeMatchKind filenameKind,
            int filenameSubScore,
            int headingExactCount,
            int bestHeadingExactIndex,
            int bestHeadingExactLine,
            string bestHeadingExactHeading,
            int bodyExactOccurrences,
            int bestBodyExactIndex,
            int bestBodyExactLine,
            string bestBodyExactHeading,
            int sectionsWithAllTokens,
            int bestSectionIndex,
            int bestSectionLine,
            string bestSectionHeading)
        {
            FilenameKind = filenameKind;
            FilenameSubScore = filenameSubScore;
            HeadingExactCount = headingExactCount;
            BestHeadingExactIndex = bestHeadingExactIndex;
            BestHeadingExactLine = bestHeadingExactLine;
            BestHeadingExactHeading = bestHeadingExactHeading;
            BodyExactOccurrences = bodyExactOccurrences;
            BestBodyExactIndex = bestBodyExactIndex;
            BestBodyExactLine = bestBodyExactLine;
            BestBodyExactHeading = bestBodyExactHeading;
            SectionsWithAllTokens = sectionsWithAllTokens;
            BestSectionIndex = bestSectionIndex;
            BestSectionLine = bestSectionLine;
            BestSectionHeading = bestSectionHeading;
        }

        public static readonly KnowledgeAnalysis None = new KnowledgeAnalysis(
            KnowledgeMatchKind.None, 0,
            0, -1, 0, null,
            0, -1, 0, null,
            0, -1, 0, null);
    }

    /// <summary>
    /// Immutable result of scoring one file. <see cref="BestMatchIndex"/> is relative to the normalized content passed to <see cref="KnowledgeScorer.Score"/> .
    /// </summary>
    internal readonly struct KnowledgeScoreResult
    {
        public KnowledgeMatchKind Kind { get; }
        public int SubScore { get; }
        public string Heading { get; }
        public int BestMatchIndex { get; }
        public int BestMatchLine { get; }

        public bool IsMatch => Kind != KnowledgeMatchKind.None;

        public KnowledgeScoreResult(KnowledgeMatchKind kind, int subScore, string heading, int bestMatchIndex, int bestMatchLine)
        {
            Kind = kind;
            SubScore = subScore;
            Heading = heading;
            BestMatchIndex = bestMatchIndex;
            BestMatchLine = bestMatchLine;
        }

        public static readonly KnowledgeScoreResult NoMatch =
            new KnowledgeScoreResult(KnowledgeMatchKind.None, 0, null, -1, 0);
    }
}
