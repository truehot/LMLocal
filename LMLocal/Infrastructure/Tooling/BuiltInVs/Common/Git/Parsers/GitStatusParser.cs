using System;
using System.Collections.Generic;
using System.Globalization;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers
{
    /// <summary>Parses <c>git status --porcelain=v2 --branch -z</c> into typed lists.</summary>
    internal static class GitStatusParser
    {
        public static GitStatusData Parse(string output)
        {
            var data = new GitStatusData();
            if (string.IsNullOrEmpty(output))
                return data;

            // With -z records are NUL-terminated; branch headers may share a record with a file
            // entry or be separated by newlines. Flatten both spellings into one list of lines.
            var records = new List<string>();
            foreach (string chunk in output.Split('\0'))
            {
                foreach (string line in chunk.Replace("\r\n", "\n").Split('\n'))
                {
                    if (!string.IsNullOrEmpty(line))
                        records.Add(line);
                }
            }

            for (int i = 0; i < records.Count; i++)
            {
                string record = records[i];
                if (string.IsNullOrEmpty(record))
                    continue;

                if (record.StartsWith("# ", StringComparison.Ordinal))
                {
                    ParseHeader(record.Substring(2), data);
                    continue;
                }

                char type = record[0];
                if (type == '?')
                {
                    if (record.Length > 2)
                        data.Untracked.Add(record.Substring(2));
                    continue;
                }

                if (type == '!')
                    continue; // ignored files are not part of the status answer

                if (type == '1' || type == '2')
                {
                    // 1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>
                    // 2 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <X><score> <path>NUL<origPath>  (renamed or copied)
                    int fieldCount = type == '1' ? 9 : 10;
                    string[] fields = record.Split(new[] { ' ' }, fieldCount);
                    if (fields.Length < fieldCount || fields[1].Length < 2)
                        continue;

                    string xy = fields[1];
                    string sub = fields[2];
                    string path = fields[fieldCount - 1];
                    char indexStatus = xy[0];
                    char worktreeStatus = xy[1];

                    if (type == '2')
                    {
                        string from = i + 1 < records.Count ? records[++i] : null;
                        data.Renamed.Add(new GitRename
                        {
                            From = from,
                            To = path,
                            Staged = indexStatus == 'R' || indexStatus == 'C'
                        });
                    }

                    // <sub> is "N..." for a plain file and "S<c><m><u>" for a submodule.
                    if (sub.Length == 4 && sub[0] == 'S')
                    {
                        data.Submodules.Add(new GitSubmodule
                        {
                            Path = path,
                            Changed = sub[1] == 'C' || sub[2] == 'M' || sub[3] == 'U'
                        });
                    }

                    // Porcelain v2 uses '.' (v1 used ' ') for "no change" in the XY columns.
                    if (indexStatus != ' ' && indexStatus != '.' && indexStatus != '?')
                        data.Staged.Add(new GitStatusEntry { Path = path, Status = StatusName(indexStatus) });

                    if (worktreeStatus != ' ' && worktreeStatus != '.' && worktreeStatus != '?')
                        data.Unstaged.Add(new GitStatusEntry { Path = path, Status = StatusName(worktreeStatus) });

                    continue;
                }

                if (type == 'u')
                {
                    // u <XY> <sub> <m1> <m2> <m3> <mW> <h1> <h2> <h3> <path>
                    string[] fields = record.Split(new[] { ' ' }, 11);
                    if (fields.Length < 11 || fields[1].Length < 2)
                        continue;

                    data.Conflicted.Add(new GitStatusEntry
                    {
                        Path = fields[10],
                        Status = "conflicted",
                        Kind = ConflictKind(fields[1])
                    });
                }
            }

            return data;
        }

        private static void ParseHeader(string header, GitStatusData data)
        {
            if (header.StartsWith("branch.head ", StringComparison.Ordinal))
            {
                string value = header.Substring("branch.head ".Length).Trim();
                data.Branch = value == "(detached)" ? null : value;
            }
            else if (header.StartsWith("branch.upstream ", StringComparison.Ordinal))
            {
                data.Upstream = header.Substring("branch.upstream ".Length).Trim();
            }
            else if (header.StartsWith("branch.ab ", StringComparison.Ordinal))
            {
                string[] values = header.Substring("branch.ab ".Length).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string value in values)
                {
                    int parsed;
                    if (value.StartsWith("+", StringComparison.Ordinal) &&
                        int.TryParse(value.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                        data.Ahead = parsed;
                    else if (value.StartsWith("-", StringComparison.Ordinal) &&
                             int.TryParse(value.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                        data.Behind = parsed;
                }
            }
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
                default: return "unknown";
            }
        }

        private static string ConflictKind(string xy)
        {
            switch (xy)
            {
                case "UU": return "both_modified";
                case "AA": return "both_added";
                case "DD": return "both_deleted";
                case "AU": return "added_by_us";
                case "UA": return "added_by_them";
                case "DU": return "deleted_by_us";
                case "UD": return "deleted_by_them";
                default: return "conflicted";
            }
        }
    }
}
