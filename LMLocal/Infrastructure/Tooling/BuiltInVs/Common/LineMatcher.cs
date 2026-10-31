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
