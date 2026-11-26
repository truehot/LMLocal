using System;
using System.Collections.Generic;
using System.Text;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers
{
    /// <summary>
    /// Turns a unified diff into files and whole hunks. Each element of Hunks is one complete hunk
    /// (header line plus its lines); a hunk that does not fit the line budget is dropped entirely.
    /// </summary>
    internal static class GitDiffParser
    {
        public static GitDiffData Parse(string output, int maxFiles, int maxLines)
        {
            var data = new GitDiffData();
            if (string.IsNullOrEmpty(output))
                return data;

            GitFileEntry current = null;
            List<string> hunks = null;
            var hunkLines = new List<string>();
            int totalLines = 0;
            bool truncated = false;

            foreach (string raw in output.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw;

                if (line.StartsWith("diff --git ", StringComparison.Ordinal))
                {
                    Flush(data, ref current, ref hunks, hunkLines, ref totalLines, maxLines, ref truncated);
                    hunkLines.Clear();

                    if (data.Files.Count >= maxFiles)
                    {
                        data.HasMoreResults = true;
                        continue;
                    }

                    current = new GitFileEntry { Status = "modified" };
                    hunks = new List<string>();
                    continue;
                }

                if (current == null)
                    continue;

                if (line.StartsWith("+++ ", StringComparison.Ordinal))
                {
                    string path = StripDiffPath(line.Substring(4));
                    if (path != "/dev/null")
                        current.Path = path;
                    continue;
                }

                if (line.StartsWith("--- ", StringComparison.Ordinal))
                {
                    string path = StripDiffPath(line.Substring(4));
                    if (string.IsNullOrEmpty(current.Path) && path != "/dev/null")
                        current.Path = path;
                    continue;
                }

                if (line.StartsWith("rename from ", StringComparison.Ordinal))
                {
                    current.From = Unquote(line.Substring("rename from ".Length));
                    current.Status = "renamed";
                    continue;
                }

                if (line.StartsWith("rename to ", StringComparison.Ordinal))
                {
                    current.Path = Unquote(line.Substring("rename to ".Length));
                    current.Status = "renamed";
                    continue;
                }

                if (line.StartsWith("new file mode ", StringComparison.Ordinal))
                {
                    current.Status = "added";
                    continue;
                }

                if (line.StartsWith("deleted file mode ", StringComparison.Ordinal))
                {
                    current.Status = "deleted";
                    continue;
                }

                if (line.StartsWith("Binary files ", StringComparison.Ordinal) || line.StartsWith("GIT binary patch", StringComparison.Ordinal))
                {
                    current.Binary = true;
                    continue;
                }

                if (line.StartsWith("@@", StringComparison.Ordinal))
                {
                    FlushHunk(current, hunks, hunkLines, ref totalLines, maxLines, ref truncated);
                    hunkLines.Add(line);
                    continue;
                }

                if (hunkLines.Count > 0 &&
                    (line.Length == 0 || line[0] == ' ' || line[0] == '+' || line[0] == '-' || line[0] == '\\'))
                {
                    hunkLines.Add(line);
                }
            }

            Flush(data, ref current, ref hunks, hunkLines, ref totalLines, maxLines, ref truncated);
            return data;
        }

        private static void Flush(
            GitDiffData data,
            ref GitFileEntry current,
            ref List<string> hunks,
            List<string> hunkLines,
            ref int totalLines,
            int maxLines,
            ref bool truncated)
        {
            if (current == null)
                return;

            FlushHunk(current, hunks, hunkLines, ref totalLines, maxLines, ref truncated);
            if (truncated)
                data.HasMoreResults = true;

            current.Hunks = hunks;
            data.Files.Add(current);

            current = null;
            hunks = null;
            truncated = false;
        }

        private static void FlushHunk(
            GitFileEntry file,
            List<string> hunks,
            List<string> hunkLines,
            ref int totalLines,
            int maxLines,
            ref bool truncated)
        {
            if (hunkLines.Count == 0)
                return;

            if (totalLines + hunkLines.Count > maxLines)
            {
                truncated = true;
                hunkLines.Clear();
                return;
            }

            foreach (string line in hunkLines)
            {
                if (line.Length == 0)
                    continue;
                if (line[0] == '+')
                    file.Added++;
                else if (line[0] == '-')
                    file.Removed++;
            }

            totalLines += hunkLines.Count;
            hunks.Add(string.Join("\n", hunkLines.ToArray()));
            hunkLines.Clear();
        }

        private static string StripDiffPath(string value)
        {
            string path = value;
            int tab = path.IndexOf('\t');
            if (tab >= 0)
                path = path.Substring(0, tab);

            path = path.Trim();
            if (path.StartsWith("a/", StringComparison.Ordinal) || path.StartsWith("b/", StringComparison.Ordinal))
                path = path.Substring(2);

            return Unquote(path);
        }

        private static string Unquote(string value)
        {
            if (string.IsNullOrEmpty(value) || value[0] != '"' || value.Length < 2 || value[value.Length - 1] != '"')
                return value;

            var result = new StringBuilder();
            for (int i = 1; i < value.Length - 1; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length - 1)
                {
                    char next = value[++i];
                    if (next == 'n') result.Append('\n');
                    else if (next == 'r') result.Append('\r');
                    else if (next == 't') result.Append('\t');
                    else result.Append(next);
                }
                else
                {
                    result.Append(value[i]);
                }
            }

            return result.ToString();
        }
    }
}
