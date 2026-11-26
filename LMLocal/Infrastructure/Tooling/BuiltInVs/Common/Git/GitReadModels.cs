using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git
{
    /// <summary>Common envelope plus the optional operation-specific fields of a git_read response.</summary>
    internal sealed class GitReadResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("operation")]
        public string Operation { get; set; }

        [JsonProperty("repo_root")]
        public string RepoRoot { get; set; }

        [JsonProperty("head")]
        public string Head { get; set; }

        [JsonProperty("branch")]
        public string Branch { get; set; }

        [JsonProperty("has_more_results")]
        public bool HasMoreResults { get; set; }

        [JsonProperty("page_token", NullValueHandling = NullValueHandling.Ignore)]
        public string PageToken { get; set; }

        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }

        [JsonProperty("timed_out")]
        public bool TimedOut { get; set; }

        [JsonProperty("warning", NullValueHandling = NullValueHandling.Ignore)]
        public string Warning { get; set; }

        [JsonProperty("shallow")]
        public bool Shallow { get; set; }

        // status
        [JsonProperty("detached", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Detached { get; set; }

        [JsonProperty("upstream", NullValueHandling = NullValueHandling.Ignore)]
        public string Upstream { get; set; }

        [JsonProperty("ahead", NullValueHandling = NullValueHandling.Ignore)]
        public int? Ahead { get; set; }

        [JsonProperty("behind", NullValueHandling = NullValueHandling.Ignore)]
        public int? Behind { get; set; }

        [JsonProperty("in_progress", NullValueHandling = NullValueHandling.Ignore)]
        public string InProgress { get; set; }

        [JsonProperty("staged", NullValueHandling = NullValueHandling.Ignore)]
        public List<GitStatusEntry> Staged { get; set; }

        [JsonProperty("unstaged", NullValueHandling = NullValueHandling.Ignore)]
        public List<GitStatusEntry> Unstaged { get; set; }

        [JsonProperty("untracked", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Untracked { get; set; }

        [JsonProperty("conflicted", NullValueHandling = NullValueHandling.Ignore)]
        public List<GitStatusEntry> Conflicted { get; set; }

        [JsonProperty("renamed", NullValueHandling = NullValueHandling.Ignore)]
        public List<GitRename> Renamed { get; set; }

        [JsonProperty("submodules", NullValueHandling = NullValueHandling.Ignore)]
        public List<GitSubmodule> Submodules { get; set; }

        // diff / show
        [JsonProperty("files", NullValueHandling = NullValueHandling.Ignore)]
        public List<GitFileEntry> Files { get; set; }

        [JsonProperty("total_added", NullValueHandling = NullValueHandling.Ignore)]
        public int? TotalAdded { get; set; }

        [JsonProperty("total_removed", NullValueHandling = NullValueHandling.Ignore)]
        public int? TotalRemoved { get; set; }

        // log
        [JsonProperty("commits", NullValueHandling = NullValueHandling.Ignore)]
        public List<GitCommit> Commits { get; set; }

        // show
        [JsonProperty("revision", NullValueHandling = NullValueHandling.Ignore)]
        public string Revision { get; set; }

        [JsonProperty("parents", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Parents { get; set; }

        [JsonProperty("author", NullValueHandling = NullValueHandling.Ignore)]
        public string Author { get; set; }

        [JsonProperty("author_date", NullValueHandling = NullValueHandling.Ignore)]
        public string AuthorDate { get; set; }

        [JsonProperty("committer", NullValueHandling = NullValueHandling.Ignore)]
        public string Committer { get; set; }

        [JsonProperty("committer_date", NullValueHandling = NullValueHandling.Ignore)]
        public string CommitterDate { get; set; }

        [JsonProperty("message", NullValueHandling = NullValueHandling.Ignore)]
        public string Message { get; set; }

        // blame
        [JsonProperty("ranges", NullValueHandling = NullValueHandling.Ignore)]
        public List<GitBlameRange> Ranges { get; set; }

        [JsonProperty("uncommitted_lines", NullValueHandling = NullValueHandling.Ignore)]
        public List<int> UncommittedLines { get; set; }

        [JsonProperty("boundary", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Boundary { get; set; }

        // file_at_revision / log -L
        [JsonProperty("path", NullValueHandling = NullValueHandling.Ignore)]
        public string Path { get; set; }

        [JsonProperty("lines", NullValueHandling = NullValueHandling.Ignore)]
        public List<string[]> Lines { get; set; }

        [JsonProperty("patch", NullValueHandling = NullValueHandling.Ignore)]
        public string Patch { get; set; }

        public static GitReadResponse Failure(string operation, string message, bool timedOut = false)
        {
            return new GitReadResponse
            {
                Success = false,
                Operation = operation,
                ErrorMessage = message,
                TimedOut = timedOut,
                HasMoreResults = false
            };
        }
    }

    internal sealed class GitStatusEntry
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("kind", NullValueHandling = NullValueHandling.Ignore)]
        public string Kind { get; set; }
    }

    internal sealed class GitRename
    {
        [JsonProperty("from")]
        public string From { get; set; }

        [JsonProperty("to")]
        public string To { get; set; }

        [JsonProperty("staged")]
        public bool Staged { get; set; }
    }

    internal sealed class GitSubmodule
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("changed")]
        public bool Changed { get; set; }
    }

    internal sealed class GitFileEntry
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("from", NullValueHandling = NullValueHandling.Ignore)]
        public string From { get; set; }

        [JsonProperty("added")]
        public int Added { get; set; }

        [JsonProperty("removed")]
        public int Removed { get; set; }

        [JsonProperty("binary")]
        public bool Binary { get; set; }

        [JsonProperty("hunks", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Hunks { get; set; }
    }

    internal sealed class GitCommit
    {
        [JsonProperty("sha")]
        public string Sha { get; set; }

        [JsonProperty("short")]
        public string Short { get; set; }

        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("date")]
        public string Date { get; set; }

        [JsonProperty("subject")]
        public string Subject { get; set; }

        [JsonProperty("files", NullValueHandling = NullValueHandling.Ignore)]
        public List<GitFileEntry> Files { get; set; }
    }

    internal sealed class GitBlameRange
    {
        [JsonProperty("start_line")]
        public int StartLine { get; set; }

        [JsonProperty("end_line")]
        public int EndLine { get; set; }

        [JsonProperty("sha")]
        public string Sha { get; set; }

        [JsonProperty("author")]
        public string Author { get; set; }

        [JsonProperty("date")]
        public string Date { get; set; }

        [JsonProperty("summary")]
        public string Summary { get; set; }
    }

    /// <summary>Intermediate result of the porcelain v2 status parser.</summary>
    internal sealed class GitStatusData
    {
        public string Branch { get; set; }
        public string Upstream { get; set; }
        public int Ahead { get; set; }
        public int Behind { get; set; }
        public List<GitStatusEntry> Staged { get; } = new List<GitStatusEntry>();
        public List<GitStatusEntry> Unstaged { get; } = new List<GitStatusEntry>();
        public List<string> Untracked { get; } = new List<string>();
        public List<GitStatusEntry> Conflicted { get; } = new List<GitStatusEntry>();
        public List<GitRename> Renamed { get; } = new List<GitRename>();
        public List<GitSubmodule> Submodules { get; } = new List<GitSubmodule>();
    }

    /// <summary>Intermediate result of the unified diff parser.</summary>
    internal sealed class GitDiffData
    {
        public List<GitFileEntry> Files { get; } = new List<GitFileEntry>();
        public bool HasMoreResults { get; set; }
    }

    /// <summary>Intermediate result of the blame porcelain parser.</summary>
    internal sealed class GitBlameData
    {
        public List<GitBlameRange> Ranges { get; } = new List<GitBlameRange>();
        public List<int> UncommittedLines { get; } = new List<int>();
        public bool Boundary { get; set; }
    }
}
