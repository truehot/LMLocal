using System;
using System.Collections.Generic;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers
{
    /// <summary>Parses the NUL-separated output of <c>--numstat -z</c> and <c>--name-status -z</c>.</summary>
    internal static class GitFileListParser
    {
        public static List<GitFileEntry> Parse(string numstatOutput, string nameStatusOutput = null)
        {
            var result = new List<GitFileEntry>();
            string[] tokens = Normalize(numstatOutput);
            if (tokens.Length == 0 && string.IsNullOrEmpty(nameStatusOutput))
                return result;

            var counts = new Dictionary<string, FileCounts>(StringComparer.OrdinalIgnoreCase);
            int i = 0;

            // numstat records: "added<TAB>removed<TAB>path" ("-" for binary files).
            for (; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (token.IndexOf('\t') < 0)
                    break;

                string[] fields = token.Split('\t');
                if (fields.Length < 3)
                    continue;

                int added = 0;
                int removed = 0;
                bool binary = fields[0] == "-" || fields[1] == "-";
                if (!binary)
                {
                    int.TryParse(fields[0], out added);
                    int.TryParse(fields[1], out removed);
                }

                var fileCounts = new FileCounts { Added = added, Removed = removed, Binary = binary };

                // A rename/copy with -z is "added<TAB>removed<TAB>" NUL old-path NUL new-path; the counts belong to the new path.
                if (fields[2].Length == 0)
                {
                    if (i + 2 < tokens.Length)
                    {
                        counts[tokens[i + 2]] = fileCounts;
                        i += 2;
                    }

                    continue;
                }

                counts[fields[2]] = fileCounts;
            }

            for (; i < tokens.Length; i++)
                ParseNameStatusTokens(tokens, ref i, result);

            if (!string.IsNullOrEmpty(nameStatusOutput))
            {
                string[] nameTokens = Normalize(nameStatusOutput);
                int j = 0;
                while (j < nameTokens.Length)
                    ParseNameStatusTokens(nameTokens, ref j, result);
            }

            foreach (GitFileEntry file in result)
            {
                FileCounts count;
                if (counts.TryGetValue(file.Path, out count))
                {
                    file.Added = count.Added;
                    file.Removed = count.Removed;
                    file.Binary = count.Binary;
                }
            }

            return result;
        }

        /// <summary>Parses only the name-status section (used by git log --name-status -z).</summary>
        public static List<GitFileEntry> ParseNameStatus(string output)
        {
            var result = new List<GitFileEntry>();
            string[] tokens = Normalize(output);
            int i = 0;
            while (i < tokens.Length)
                ParseNameStatusTokens(tokens, ref i, result);

            return result;
        }

        /// <summary>
        /// Reads consecutive name-status entries starting at <paramref name="i"/> and stops before the first token that
        /// is not a status code (the next commit's sha in git log output). Paths are kept exactly as git printed them.
        /// </summary>
        public static List<GitFileEntry> ReadNameStatus(string[] tokens, ref int i)
        {
            var result = new List<GitFileEntry>();
            while (i < tokens.Length)
            {
                string status = tokens[i].Trim('\r', '\n');
                if (status.Length == 0)
                {
                    i++;
                    continue;
                }

                if (!IsStatusToken(status) || i + 1 >= tokens.Length)
                    break;

                i++;
                string path = tokens[i++];
                string from = null;
                char code = status[0];
                if ((code == 'R' || code == 'C') && i < tokens.Length)
                {
                    // git -z prints the old path before the new path.
                    from = path;
                    path = tokens[i++];
                }

                result.Add(new GitFileEntry { Path = path, From = from, Status = StatusName(code) });
            }

            return result;
        }

        private static string[] Normalize(string output)
        {
            if (string.IsNullOrEmpty(output))
                return new string[0];

            string[] raw = output.Split('\0');
            var result = new List<string>(raw.Length);
            foreach (string token in raw)
            {
                string value = token.Trim('\r', '\n');
                if (value.Length > 0)
                    result.Add(value);
            }

            return result.ToArray();
        }

        private static void ParseNameStatusTokens(string[] tokens, ref int i, List<GitFileEntry> result)
        {
            string statusToken = tokens[i++];
            if (!IsStatusToken(statusToken) || i >= tokens.Length)
                return;

            string path = tokens[i++];
            string from = null;
            char code = statusToken[0];
            if ((code == 'R' || code == 'C') && i < tokens.Length)
            {
                // git -z prints the old path before the new path.
                from = path;
                path = tokens[i++];
            }

            result.Add(new GitFileEntry
            {
                Path = path,
                From = from,
                Status = StatusName(code)
            });
        }

        private static bool IsStatusToken(string token)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 4)
                return false;

            char code = token[0];
            if (!char.IsLetter(code))
                return false;

            for (int i = 1; i < token.Length; i++)
            {
                if (token[i] < '0' || token[i] > '9')
                    return false;
            }

            return true;
        }

        private static string StatusName(char code)
        {
            switch (code)
            {
                case 'A': return "added";
                case 'M': return "modified";
                case 'D': return "deleted";
                case 'R': return "renamed";
                case 'C': return "copied";
                case 'T': return "type_changed";
                case 'U': return "conflicted";
                case 'X': return "unknown";
                default: return code.ToString();
            }
        }

        private sealed class FileCounts
        {
            public int Added;
            public int Removed;
            public bool Binary;
        }
    }
}
