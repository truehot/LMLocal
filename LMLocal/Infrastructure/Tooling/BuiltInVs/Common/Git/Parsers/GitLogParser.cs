using System.Collections.Generic;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers
{
    /// <summary>
    /// Parses <c>git log -z</c> output in <see cref="Format"/>, optionally followed by <c>--name-status</c> entries.
    /// Fields are NUL-separated: git cuts commit messages and names at NUL, so their text cannot forge a commit
    /// (with 0x1E/0x1F separators a crafted subject could).
    /// </summary>
    internal static class GitLogParser
    {
        public const string Format = "--format=%H%x00%h%x00%an%x00%aI%x00%s";

        private const int FieldCount = 5;

        public static List<GitCommit> Parse(string output, bool includeFiles)
        {
            var commits = new List<GitCommit>();
            if (string.IsNullOrEmpty(output))
                return commits;

            string[] tokens = output.Split('\0');
            int i = 0;
            while (i < tokens.Length)
            {
                string sha = tokens[i].TrimStart('\r', '\n');
                if (!GitRevision.IsSha(sha))
                {
                    i++;
                    continue;
                }

                // A record cut by the output limit is incomplete: drop it.
                if (i + FieldCount > tokens.Length)
                    break;

                var commit = new GitCommit
                {
                    Sha = sha,
                    Short = tokens[i + 1],
                    Author = tokens[i + 2],
                    Date = tokens[i + 3],
                    Subject = tokens[i + 4]
                };
                i += FieldCount;

                List<GitFileEntry> files = GitFileListParser.ReadNameStatus(tokens, ref i);
                if (includeFiles)
                    commit.Files = files;

                commits.Add(commit);
            }

            return commits;
        }
    }
}
