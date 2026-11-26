using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Shared helpers for file editing tools (replace_file_lines, insert_file_lines).
    /// </summary>
    internal static class LineMatcher
    {
        /// <summary>
        /// Number of lines to search around the expected position when a mismatch occurs.
        /// </summary>
        public const int MaxSearchWindowLines = 50;

        /// <summary>
        /// Maximum number of empty lines a file tool may append to reach a position past the end of the file.
        /// </summary>
        public const int MaxPaddingLines = 50;

        /// <summary>
        /// Number of surrounding lines included in each candidate's Text for context.
        /// </summary>
        public const int CandidateContextLines = 2;

        /// <summary>
        /// Compares two lines with trailing whitespace ignored.
        /// </summary>
        public static bool LinesEqual(string a, string b) => a.TrimEnd() == b.TrimEnd();

        /// <summary>
        /// Compares two lines ignoring leading and trailing whitespace. Used for the first line of old_lines as a fallback when models omit the initial indentation.
        /// </summary>
        public static bool LinesEqualIgnoringLeadingWhitespace(string a, string b) => a.TrimStart().TrimEnd() == b.TrimStart().TrimEnd();

        /// <summary>
        /// Canonical comparison for MATCHING.
        /// </summary>
        public static bool LinesEqualCanonical(string a, string b) => CharacterMap.Canonicalize(a).TrimEnd() == CharacterMap.Canonicalize(b).TrimEnd();

        /// <summary>
        /// Canonical comparison of the first block line with leading indentation ignored — the canonical analogue of <see cref="LinesEqualIgnoringLeadingWhitespace"/> (both sides trimmed, i.e. <c>Trim()</c>).
        /// </summary>
        public static bool LinesEqualCanonicalIgnoringLeadingWhitespace(string a, string b)
            => CharacterMap.Canonicalize(a).Trim() == CharacterMap.Canonicalize(b).Trim();

        /// <summary>
        /// EXACT canonical equality (no <c>TrimEnd</c>), used by restore only.
        /// </summary>
        public static bool LinesEqualCanonicalExact(string a, string b)
            => CharacterMap.Canonicalize(a) == CharacterMap.Canonicalize(b);

        /// <summary>
        /// Searches for a single line in the file lines within ±SearchWindow of aroundLine. Returns 1-indexed line numbers of all matches.
        /// </summary>
        public static List<int> FindMatches(List<string> lines, string line, int aroundLine)
        {
            int lower = Math.Max(0, aroundLine - 1 - MaxSearchWindowLines);
            int upper = Math.Min(lines.Count - 1, aroundLine - 1 + MaxSearchWindowLines);
            var result = new List<int>();
            for (int i = lower; i <= upper; i++)
            {
                if (LinesEqual(lines[i], line)) result.Add(i + 1); // 1-indexed
            }
            return result;
        }

        /// <summary>
        /// Canonical variant of <see cref="FindMatches"/>, used by insert_file_lines as the last resort before giving up on <c>expected_line</c>.
        /// </summary>
        public static List<int> FindMatchesCanonical(List<string> lines, string line, int aroundLine)
        {
            int lower = Math.Max(0, aroundLine - 1 - MaxSearchWindowLines);
            int upper = Math.Min(lines.Count - 1, aroundLine - 1 + MaxSearchWindowLines);
            var result = new List<int>();
            for (int i = lower; i <= upper; i++)
            {
                if (LinesEqualCanonical(lines[i], line)) result.Add(i + 1); // 1-indexed
            }
            return result;
        }

        /// <summary>
        /// Builds candidate entries with surrounding context lines for each position.
        /// </summary>
        public static List<LineCandidate> BuildCandidates(List<string> lines, List<int> positions, int blockLength = 1)
        {
            var result = new List<LineCandidate>();
            foreach (int p in positions)
            {
                int from = Math.Max(0, p - 1 - CandidateContextLines);
                int to = Math.Min(lines.Count - 1, p - 1 + blockLength - 1 + CandidateContextLines);
                var sb = new System.Text.StringBuilder();
                for (int i = from; i <= to; i++)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(lines[i]);
                }
                result.Add(new LineCandidate { StartLine = p, Text = sb.ToString() });
            }
            return result;
        }

        /// <summary>
        /// Picks the position whose block matches the most leading lines of oldLines (ties: nearest to startLine, then the lower one)
        /// and describes where it first differs. Lines are compared canonically, so characters that matching already normalizes are never reported.
        /// </summary>
        public static (int bestPosition, string description) DescribeClosestMismatch(
            List<string> lines, string[] oldLines, List<int> positions, int startLine)
        {
            int best = positions[0], bestPrefix = -1;
            foreach (int p in positions)
            {
                int prefix = 0;
                while (prefix < oldLines.Length && p - 1 + prefix < lines.Count
                       && LinesEqualCanonical(lines[p - 1 + prefix], oldLines[prefix]))
                    prefix++;
                if (prefix > bestPrefix || (prefix == bestPrefix && Math.Abs(p - startLine) < Math.Abs(best - startLine)))
                {
                    best = p;
                    bestPrefix = prefix;
                }
            }

            if (bestPrefix >= oldLines.Length)
                return (best, null);

            int lineInBlock = bestPrefix + 1;
            int fileLine = best + bestPrefix;
            string head = $"Closest match starts at line {best}: line {lineInBlock} of old_lines (file line {fileLine})";
            if (fileLine > lines.Count)
                return (best, $"{head} is past the end of the file ({lines.Count} lines).");

            string fileText = CharacterMap.Canonicalize(lines[fileLine - 1]).TrimEnd();
            string oldText = CharacterMap.Canonicalize(oldLines[bestPrefix]).TrimEnd();
            int column = 0;
            while (column < fileText.Length && column < oldText.Length && fileText[column] == oldText[column])
                column++;

            return (best, $"{head} differs at column {column + 1}: the file has {DescribeChar(fileText, column)}, old_lines has {DescribeChar(oldText, column)}.");
        }

        /// <summary>
        /// Printable ASCII as a literal with its code, anything else (spaces, controls, non-ASCII) as a code only.
        /// </summary>
        private static string DescribeChar(string text, int index)
        {
            if (index >= text.Length)
                return "end of line";
            int code = char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
                ? char.ConvertToUtf32(text[index], text[index + 1])
                : text[index];
            return code >= 0x21 && code <= 0x7E ? $"'{(char)code}' (U+{code:X4})" : $"U+{code:X4}";
        }
    }

    /// <summary>
    /// Represents a candidate line position returned when the tool finds multiple possible matches for old_lines / expected_line.
    /// </summary>
    internal class LineCandidate
    {
        [JsonProperty("start_line")]
        public int StartLine { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }
    }
}
