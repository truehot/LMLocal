using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// Fallback word matching for search_solution_knowledge: a file is ranked by its record (file name + one section) with the most query words.
    /// </summary>
    internal static class KnowledgePartialMatcher
    {
        /// <summary>
        /// Most fallback results kept per search.
        /// </summary>
        public const int MaxResults = 15;

        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "in", "or", "and", "of", "to", "the", "a", "an", "for", "on", "is", "by", "with", "at", "as", "from"
        };

        private static readonly Regex FileExtensionRegex = new Regex(
            @"\.(md|cs|js|ts|css|html|json|jsonl|xml|txt|csproj|sln|vsct|xaml|config)\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// Query words that count in the fallback: without stop words, single letters, bare numbers and file extensions written after a dot.
        /// </summary>
        public static string[] GetSignificantTerms(string query)
        {
            string[] tokens = KnowledgeScorer.TokenizeQuery(query);
            if (tokens.Length == 0)
                return tokens;

            var extensions = new HashSet<string>(
                FileExtensionRegex.Matches(query).Cast<Match>().Select(m => m.Groups[1].Value),
                StringComparer.OrdinalIgnoreCase);

            return tokens.Where(t => !IsStopTerm(t, extensions)).ToArray();
        }

        /// <summary>
        /// Words a record needs: half of the significant words, but at least 2.
        /// </summary>
        public static int GetRequiredCount(int termCount)
        {
            return Math.Max(2, (termCount + 1) / 2);
        }

        /// <summary>
        /// Cheap pass over one file: the best record word count, how many sections reach it, and which terms occur anywhere in the file.
        /// </summary>
        public static KnowledgePartialMatch Analyze(string fileName, string normalizedContent, IReadOnlyList<string> terms)
        {
            int termCount = terms?.Count ?? 0;
            var inName = new bool[termCount];
            var present = new bool[termCount];
            if (termCount == 0)
                return new KnowledgePartialMatch(0, null, inName, present);

            string content = normalizedContent ?? string.Empty;
            string nameOnly = string.IsNullOrEmpty(fileName) ? string.Empty : Path.GetFileNameWithoutExtension(fileName);
            string[] stems = terms.Select(Stem).ToArray();

            var inContent = new bool[termCount];
            int fileWords = 0;
            int nameWords = 0;
            for (int i = 0; i < termCount; i++)
            {
                inName[i] = nameOnly.IndexOf(stems[i], StringComparison.OrdinalIgnoreCase) >= 0;
                inContent[i] = IndexOfWordStart(content, stems[i], 0, content.Length) >= 0;
                present[i] = inName[i] || inContent[i];
                if (present[i])
                    fileWords++;
                if (inName[i])
                    nameWords++;
            }

            int required = GetRequiredCount(termCount);
            if (fileWords < required)
                return new KnowledgePartialMatch(0, null, inName, present);

            int bestWords = 0;
            var bestSections = new List<KnowledgeSectionRange>();
            foreach (var section in SplitSections(content))
            {
                int words = nameWords;
                for (int i = 0; i < termCount; i++)
                {
                    if (!inName[i] && inContent[i] && IndexOfWordStart(content, stems[i], section.Start, section.End) >= 0)
                        words++;
                }

                if (words < required || words < bestWords)
                    continue;

                if (words > bestWords)
                {
                    bestWords = words;
                    bestSections.Clear();
                }
                bestSections.Add(section);
            }

            return bestWords == 0
                ? new KnowledgePartialMatch(0, null, inName, present)
                : new KnowledgePartialMatch(bestWords, bestSections, inName, present);
        }

        /// <summary>
        /// Picks, among the best records, the one whose snippet window holds the most distinct found words, and the snippet center in it.
        /// </summary>
        public static KnowledgePartialDetails BuildDetails(string normalizedContent, KnowledgePartialMatch match, IReadOnlyList<string> terms, int radius)
        {
            string content = normalizedContent ?? string.Empty;
            string[] stems = terms.Select(Stem).ToArray();

            int bestCovered = -1;
            int bestCenter = -1;
            KnowledgeSectionRange bestSection = match.BestSections[0];
            bool[] bestFound = new bool[terms.Count];

            foreach (var section in match.BestSections)
            {
                var occurrences = new List<KeyValuePair<int, int>>();
                var found = new bool[terms.Count];
                for (int t = 0; t < terms.Count; t++)
                {
                    if (match.InName[t])
                        continue;

                    int index = section.Start;
                    while ((index = IndexOfWordStart(content, stems[t], index, section.End)) >= 0)
                    {
                        occurrences.Add(new KeyValuePair<int, int>(index, t));
                        found[t] = true;
                        index++;
                    }
                }

                occurrences.Sort((a, b) => a.Key.CompareTo(b.Key));

                int covered = 0;
                int center = -1;
                foreach (var occurrence in occurrences)
                {
                    int count = CountDistinctTerms(occurrences, occurrence.Key - radius, occurrence.Key + radius);
                    if (count > covered)
                    {
                        covered = count;
                        center = occurrence.Key;
                    }
                }

                if (covered > bestCovered)
                {
                    bestCovered = covered;
                    bestCenter = center;
                    bestSection = section;
                    bestFound = found;
                }
            }

            var matchedTerms = new List<string>();
            for (int t = 0; t < terms.Count; t++)
            {
                if (match.InName[t] || bestFound[t])
                    matchedTerms.Add(terms[t]);
            }

            if (bestCenter < 0)
                return new KnowledgePartialDetails(-1, 0, null, matchedTerms);

            int line = 1;
            for (int i = 0; i < bestCenter; i++)
            {
                if (content[i] == '\n')
                    line++;
            }

            return new KnowledgePartialDetails(bestCenter, line, GetSectionHeading(content, bestSection), matchedTerms);
        }

        /// <summary>
        /// Note for a response that contains fallback results; lists the significant words found in no scanned record.
        /// </summary>
        /// <param name="terms">Significant query words.</param>
        /// <param name="foundAnywhere">Per term: found somewhere in the scanned records.</param>
        /// <param name="noun">What was scanned ("document", "session"); names the place the words were not found in.</param>
        public static string BuildNote(IReadOnlyList<string> terms, bool[] foundAnywhere, string noun = "document")
        {
            var missing = new List<string>();
            for (int i = 0; i < terms.Count; i++)
            {
                if (foundAnywhere == null || i >= foundAnywhere.Length || !foundAnywhere[i])
                    missing.Add(terms[i]);
            }

            return BuildNoteForMissing(missing, noun);
        }

        /// <summary>
        /// Note for a response that contains fallback results, built from the words found nowhere — the form a cache can
        /// keep, so the note is rebuilt for whatever page is returned.
        /// </summary>
        public static string BuildNoteForMissing(IReadOnlyList<string> missingTerms, string noun)
        {
            string note = "Results with matched_terms contain only some of the query words.";
            if (missingTerms != null && missingTerms.Count > 0)
                note += " Not found in any " + noun + ": " + string.Join(", ", missingTerms.Select(t => "'" + t + "'")) + ".";
            return note;
        }

        /// <summary>
        /// Drops the trailing 's' of words longer than 4 letters so that a plural query word also finds the singular.
        /// </summary>
        internal static string Stem(string term)
        {
            return term.Length > 4 && term.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                ? term.Substring(0, term.Length - 1)
                : term;
        }

        /// <summary>
        /// First case-insensitive occurrence of <paramref name="value"/> within [start, end) that starts a word.
        /// </summary>
        internal static int IndexOfWordStart(string text, string value, int start, int end)
        {
            int i = start;
            while (i < end && end - i >= value.Length)
            {
                int found = text.IndexOf(value, i, end - i, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                    return -1;
                if (found == 0 || !char.IsLetterOrDigit(text[found - 1]))
                    return found;
                i = found + 1;
            }
            return -1;
        }

        private static bool IsStopTerm(string token, HashSet<string> extensions)
        {
            if (StopWords.Contains(token) || extensions.Contains(token))
                return true;
            if (token.Length == 1 && char.IsLetter(token[0]))
                return true;
            return token.All(char.IsDigit);
        }

        private static List<KnowledgeSectionRange> SplitSections(string content)
        {
            var sections = new List<KnowledgeSectionRange>();
            int sectionStart = 0;
            int lineStart = 0;
            while (lineStart < content.Length)
            {
                int lineEnd = content.IndexOf('\n', lineStart);
                if (lineEnd < 0)
                    lineEnd = content.Length;

                if (lineStart > sectionStart && IsHeadingAt(content, lineStart, lineEnd))
                {
                    sections.Add(new KnowledgeSectionRange(sectionStart, lineStart));
                    sectionStart = lineStart;
                }
                lineStart = lineEnd + 1;
            }
            sections.Add(new KnowledgeSectionRange(sectionStart, content.Length));
            return sections;
        }

        private static bool IsHeadingAt(string content, int lineStart, int lineEnd)
        {
            int i = lineStart;
            while (i < lineEnd && char.IsWhiteSpace(content[i]))
                i++;
            if (i >= lineEnd || content[i] != '#')
                return false;
            return KnowledgeScorer.IsHeadingLine(content.Substring(i, lineEnd - i).Trim());
        }

        private static string GetSectionHeading(string content, KnowledgeSectionRange section)
        {
            int lineEnd = content.IndexOf('\n', section.Start);
            if (lineEnd < 0 || lineEnd > section.End)
                lineEnd = section.End;
            string firstLine = content.Substring(section.Start, lineEnd - section.Start).Trim();
            return KnowledgeScorer.IsHeadingLine(firstLine) ? firstLine.TrimStart('#').Trim() : null;
        }

        private static int CountDistinctTerms(List<KeyValuePair<int, int>> occurrences, int from, int to)
        {
            var terms = new HashSet<int>();
            foreach (var occurrence in occurrences)
            {
                if (occurrence.Key >= from && occurrence.Key < to)
                    terms.Add(occurrence.Value);
            }
            return terms.Count;
        }
    }

    /// <summary>
    /// Character range [Start, End) of one Markdown section in normalized content.
    /// </summary>
    internal readonly struct KnowledgeSectionRange
    {
        public int Start { get; }
        public int End { get; }

        public KnowledgeSectionRange(int start, int end)
        {
            Start = start;
            End = end;
        }
    }

    /// <summary>
    /// Result of <see cref="KnowledgePartialMatcher.Analyze"/> for one file.
    /// </summary>
    internal sealed class KnowledgePartialMatch
    {
        /// <summary>Query words in the best record; 0 when no record reaches the required count.</summary>
        public int WordCount { get; }

        /// <summary>Sections whose record has <see cref="WordCount"/> words.</summary>
        public IReadOnlyList<KnowledgeSectionRange> BestSections { get; }

        /// <summary>Per term: found in the file name.</summary>
        public bool[] InName { get; }

        /// <summary>Per term: found anywhere in the file (name or text).</summary>
        public bool[] PresentTerms { get; }

        public bool IsMatch => WordCount > 0;
        public int SectionCount => BestSections?.Count ?? 0;

        public KnowledgePartialMatch(int wordCount, IReadOnlyList<KnowledgeSectionRange> bestSections, bool[] inName, bool[] presentTerms)
        {
            WordCount = wordCount;
            BestSections = bestSections;
            InName = inName;
            PresentTerms = presentTerms;
        }
    }

    /// <summary>
    /// Snippet center, line, heading and found words of the chosen fallback record.
    /// </summary>
    internal readonly struct KnowledgePartialDetails
    {
        /// <summary>Index in normalized content, or -1 when the record's words are only in the file name.</summary>
        public int CenterIndex { get; }
        public int Line { get; }
        public string Heading { get; }
        public List<string> MatchedTerms { get; }

        public KnowledgePartialDetails(int centerIndex, int line, string heading, List<string> matchedTerms)
        {
            CenterIndex = centerIndex;
            Line = line;
            Heading = heading;
            MatchedTerms = matchedTerms;
        }
    }
}
