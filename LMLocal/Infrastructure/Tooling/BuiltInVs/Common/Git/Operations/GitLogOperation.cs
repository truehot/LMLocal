using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Operations
{
    internal sealed class GitLogOperation : GitOperationBase
    {
        private const int MaxLineRange = 200;
        private const int MaxPatchLines = 2000;

        public override string Name => "log";

        private sealed class Request
        {
            public int MaxCount;
            public bool IncludeFiles;
            public bool NoMerges;
            public bool FirstParent;
            public bool Follow;
            public bool IncludeFromCommit;
            public List<string> Paths;
            public string From;
            public string To;
            public string TokenSha;
            public int Offset;
            public string Since;
            public string Until;
            public string Author;
            public string Message;
            public string Added;
            public string Regex;
            public string LinePath;
            public int LineStart;
            public int LineEnd;
        }

        public override object Parse(Dictionary<string, object> parameters, out string error)
        {
            var request = new Request();
            request.MaxCount = GitParams.GetInt(parameters, "max_count", 20, 1, 100, out error);
            if (error != null) return null;
            request.IncludeFiles = GitParams.GetBool(parameters, "include_files", false, out error);
            if (error != null) return null;
            request.NoMerges = GitParams.GetBool(parameters, "no_merges", false, out error);
            if (error != null) return null;
            request.FirstParent = GitParams.GetBool(parameters, "first_parent", false, out error);
            if (error != null) return null;
            request.Follow = GitParams.GetBool(parameters, "follow", false, out error);
            if (error != null) return null;
            request.IncludeFromCommit = GitParams.GetBool(parameters, "include_from_commit", false, out error);
            if (error != null) return null;
            request.Paths = ReadPaths(parameters, out error);
            if (error != null) return null;

            if (request.Follow && request.Paths.Count != 1)
            {
                error = "Parameter 'follow' requires exactly one entry in 'paths'.";
                return null;
            }

            request.From = ReadRevision(parameters, "from", out error);
            if (error != null) return null;
            request.To = ReadRevision(parameters, "to", out error);
            if (error != null) return null;
            if (request.To != null && request.From == null)
            {
                error = "Parameter 'to' requires 'from'.";
                return null;
            }

            if (request.IncludeFromCommit && request.From == null)
            {
                error = "Parameter 'include_from_commit' requires 'from'.";
                return null;
            }

            string token = ToolPagination.ReadToken(parameters);
            if (token != null)
            {
                int colon = token.LastIndexOf(':');
                request.TokenSha = colon > 0 ? token.Substring(0, colon) : null;
                if (!GitRevision.IsSha(request.TokenSha))
                {
                    error = "page_token must be the page_token returned by a previous git_read log call.";
                    return null;
                }

                request.Offset = ToolPagination.ParseOffset(token, request.MaxCount);
            }

            request.Since = GitParams.GetSearchText(parameters, "since", out error);
            if (error != null) return null;
            request.Until = GitParams.GetSearchText(parameters, "until", out error);
            if (error != null) return null;
            request.Author = GitParams.GetSearchText(parameters, "author", out error);
            if (error != null) return null;
            request.Message = GitParams.GetSearchText(parameters, "message_contains", out error);
            if (error != null) return null;
            request.Added = GitParams.GetSearchText(parameters, "content_added_or_removed", out error);
            if (error != null) return null;
            request.Regex = GitParams.GetSearchText(parameters, "content_regex", out error);
            if (error != null) return null;

            if (!ReadLineRange(parameters, request, out error))
                return null;

            return request;
        }

        public override async Task<GitReadResponse> ExecuteAsync(GitRepositoryContext context, object parsed, CancellationToken cancellationToken)
        {
            var request = (Request)parsed;
            List<string> paths = ResolvePaths(context, request.Paths);
            string toSha = request.TokenSha
                ?? await GitRevision.ResolveAsync(request.To ?? "HEAD", context, cancellationToken).ConfigureAwait(false);

            string range = toSha;
            if (request.From != null)
            {
                string fromSha = await GitRevision.ResolveAsync(request.From, context, cancellationToken).ConfigureAwait(false);
                string baseSha = await GitRevision.BaseAsync(fromSha, request.IncludeFromCommit, context, cancellationToken).ConfigureAwait(false);
                range = baseSha + ".." + toSha;
            }

            var options = new List<string> { "log", "--no-color", "--no-textconv", "--no-ext-diff", "-M" };
            string linePath = null;
            if (request.LinePath != null)
            {
                linePath = context.ResolveRepoPath(request.LinePath);
                options.Add("-L");
                options.Add(CommandLineEscaper.Escape(
                    request.LineStart.ToString(CultureInfo.InvariantCulture) + "," +
                    request.LineEnd.ToString(CultureInfo.InvariantCulture) + ":" + linePath));
            }
            else
            {
                options.Add("-z");
                options.Add(GitLogParser.Format);
                options.Add("--skip=" + request.Offset.ToString(CultureInfo.InvariantCulture));
                options.Add("-n");
                options.Add((request.MaxCount + 1).ToString(CultureInfo.InvariantCulture));
                if (request.IncludeFiles)
                    options.Add("--name-status");
            }

            AddValue(options, "--since=", request.Since);
            AddValue(options, "--until=", request.Until);
            AddValue(options, "--author=", request.Author);
            if (request.Message != null)
            {
                options.Add("--fixed-strings");
                options.Add("-i");
                AddValue(options, "--grep=", request.Message);
            }

            AddValue(options, "-S", request.Added);
            AddValue(options, "-G", request.Regex);
            if (request.NoMerges)
                options.Add("--no-merges");
            if (request.FirstParent)
                options.Add("--first-parent");
            if (request.Follow)
                options.Add("--follow");

            options.Add("--end-of-options");
            options.Add(range);
            options.Add(GitArguments.Paths(paths));

            GitProcessResult result = await RunAsync(context, GitArguments.Build(options.ToArray()), cancellationToken).ConfigureAwait(false);
            GitProcessGuard.EnsureSuccess(result, "Git log failed");

            GitReadResponse response = Ok(context);
            if (linePath != null)
            {
                response.Path = context.ToDisplayPath(linePath);
                bool cut;
                response.Patch = TruncateLines(result.StandardOutput, MaxPatchLines, out cut);
                response.HasMoreResults = cut;
                if (result.Truncated)
                    MarkTruncated(response);
                return response;
            }

            List<GitCommit> commits = GitLogParser.Parse(result.StandardOutput, request.IncludeFiles);
            bool hasMore = commits.Count > request.MaxCount;
            if (hasMore)
                commits.RemoveRange(request.MaxCount, commits.Count - request.MaxCount);

            // The last commit of cut output may be incomplete: drop it, the next page starts with it.
            if (result.Truncated && commits.Count > 0)
            {
                commits.RemoveAt(commits.Count - 1);
                hasMore = true;
            }

            foreach (GitCommit commit in commits)
            {
                if (commit.Files == null)
                    continue;
                foreach (GitFileEntry file in commit.Files)
                    file.Path = context.ToDisplayPath(file.Path);
            }

            response.Commits = commits;
            response.HasMoreResults = hasMore;
            response.PageToken = hasMore
                ? toSha + ":" + (request.Offset + commits.Count).ToString(CultureInfo.InvariantCulture)
                : null;
            if (result.Truncated)
                MarkTruncated(response);
            return response;
        }

        private static void AddValue(List<string> options, string prefix, string value)
        {
            if (value != null)
                options.Add(CommandLineEscaper.Escape(prefix + value));
        }

        private static bool ReadLineRange(Dictionary<string, object> parameters, Request request, out string error)
        {
            error = null;
            object raw;
            if (parameters == null || !parameters.TryGetValue("line_range", out raw) || raw == null ||
                (raw is JValue jNull && jNull.Type == JTokenType.Null))
                return true;

            JObject range = raw as JObject;
            if (range == null)
            {
                error = "Parameter 'line_range' must be an object with path, start_line and end_line.";
                return false;
            }

            string path = (string)range["path"];
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Parameter 'line_range.path' is required.";
                return false;
            }

            int start;
            int end;
            if (range["start_line"] == null || range["end_line"] == null ||
                !int.TryParse(range["start_line"].ToString(), out start) ||
                !int.TryParse(range["end_line"].ToString(), out end))
            {
                error = "Parameter 'line_range' needs integer start_line and end_line.";
                return false;
            }

            if (start < 1 || end < start || (long)end - start + 1 > MaxLineRange)
            {
                error = $"line_range must have 1 <= start_line <= end_line and span at most {MaxLineRange} lines.";
                return false;
            }

            // git: "-L<range>:<file> cannot be used with pathspec".
            if (request.Paths.Count > 0 || request.Follow)
            {
                error = "Parameter 'line_range' cannot be combined with 'paths' or 'follow'; the path goes into line_range.path.";
                return false;
            }

            request.LinePath = path.Trim();
            request.LineStart = start;
            request.LineEnd = end;
            return true;
        }

        private static string TruncateLines(string text, int maxLines, out bool cut)
        {
            cut = false;
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length <= maxLines)
                return text;

            cut = true;
            var result = new StringBuilder();
            for (int i = 0; i < maxLines; i++)
                result.AppendLine(lines[i]);
            result.Append($"[patch truncated at {maxLines} lines]");
            return result.ToString();
        }
    }
}
