using System;
using System.Collections.Generic;
using System.Globalization;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers
{
    /// <summary>Parses <c>git blame --porcelain</c> into consecutive ranges of one commit.</summary>
    internal static class GitBlameParser
    {
        private const string UncommittedSha = "0000000000000000000000000000000000000000";

        public static GitBlameData Parse(string output)
        {
            var data = new GitBlameData();
            if (string.IsNullOrEmpty(output))
                return data;

            string currentSha = null;
            int currentStart = 0;
            int currentEnd = 0;
            string author = null;
            string date = null;
            string summary = null;
            bool boundary = false;

            foreach (string raw in output.Replace("\r\n", "\n").Split('\n'))
            {
                if (raw.Length == 0)
                    continue;

                if (raw[0] == '\t')
                    continue; // line text is deliberately not returned

                int space = raw.IndexOf(' ');
                string key = space < 0 ? raw : raw.Substring(0, space);
                string value = space < 0 ? string.Empty : raw.Substring(space + 1);

                if (key == "boundary")
                {
                    boundary = true;
                    continue;
                }

                if (key.Length >= 39 && IsShaLike(key) && value.Length > 0)
                {
                    string[] parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                        continue;

                    int finalLine;
                    int count = 1;
                    if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out finalLine))
                        continue;
                    if (parts.Length >= 3)
                        int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
                    if (count <= 0)
                        count = 1;

                    string sha = key.TrimStart('^');
                    if (sha == UncommittedSha)
                    {
                        Flush(data, ref currentSha, ref currentStart, ref currentEnd, ref author, ref date, ref summary);
                        for (int line = finalLine; line < finalLine + count; line++)
                            data.UncommittedLines.Add(line);
                        continue;
                    }

                    if (currentSha == sha)
                    {
                        // Consecutive lines of the same commit stay in one range; the first
                        // header already gave the end of the group.
                        currentEnd = finalLine;
                        continue;
                    }

                    Flush(data, ref currentSha, ref currentStart, ref currentEnd, ref author, ref date, ref summary);
                    currentSha = sha;
                    currentStart = finalLine;
                    currentEnd = finalLine + count - 1;
                    author = null;
                    date = null;
                    summary = null;
                    continue;
                }

                if (currentSha == null)
                    continue;

                if (key == "author")
                    author = value;
                else if (key == "summary")
                    summary = value;
                else if (key == "author-time")
                {
                    long seconds;
                    date = long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds)
                        ? DateTimeOffset.FromUnixTimeSeconds(seconds).ToString("o", CultureInfo.InvariantCulture)
                        : value;
                }
            }

            Flush(data, ref currentSha, ref currentStart, ref currentEnd, ref author, ref date, ref summary);
            data.Boundary = boundary;
            return data;
        }

        private static void Flush(
            GitBlameData data,
            ref string sha,
            ref int start,
            ref int end,
            ref string author,
            ref string date,
            ref string summary)
        {
            if (sha == null)
                return;

            GitBlameRange last = data.Ranges.Count > 0 ? data.Ranges[data.Ranges.Count - 1] : null;
            if (last != null && last.Sha == sha && last.EndLine + 1 == start)
            {
                last.EndLine = end;
            }
            else
            {
                data.Ranges.Add(new GitBlameRange
                {
                    StartLine = start,
                    EndLine = end,
                    Sha = sha,
                    Author = author,
                    Date = date,
                    Summary = summary
                });
            }

            sha = null;
            start = 0;
            end = 0;
            author = null;
            date = null;
            summary = null;
        }

        private static bool IsShaLike(string value)
        {
            foreach (char c in value)
            {
                if (c == '^')
                    continue;
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }

            return true;
        }
    }
}
