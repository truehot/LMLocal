using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Operations
{
    /// <summary>
    /// One git_read operation. Parameters are read and validated once by <see cref="Parse"/> (before git runs);
    /// <see cref="ExecuteAsync"/> gets the parsed request. Model-facing failures are thrown as <see cref="GitToolException"/>.
    /// </summary>
    internal abstract class GitOperationBase
    {
        public const int MaxPaths = 50;

        public abstract string Name { get; }

        /// <summary>Parsed request, or null with a model-facing error when the parameters are invalid.</summary>
        public abstract object Parse(Dictionary<string, object> parameters, out string error);

        public abstract Task<GitReadResponse> ExecuteAsync(GitRepositoryContext context, object request, CancellationToken cancellationToken);

        protected GitReadResponse Ok(GitRepositoryContext context)
        {
            return new GitReadResponse
            {
                Success = true,
                Operation = Name,
                RepoRoot = context.RepoRoot,
                Head = context.Head,
                Branch = context.Branch,
                Shallow = context.Shallow
            };
        }

        protected static Task<GitProcessResult> RunAsync(GitRepositoryContext context, string arguments, CancellationToken cancellationToken)
        {
            return context.Runner.RunAsync(arguments, context.RepoRoot, cancellationToken);
        }

        /// <summary>git output was cut (4 MB cap, or pipes held open after git exited): the answer is incomplete and says so.</summary>
        protected static void MarkTruncated(GitReadResponse response)
        {
            response.HasMoreResults = true;
            response.Warning = "git output was cut (over the 4 MB limit or not finished). Narrow paths or the revision range.";
        }

        protected static List<string> ReadPaths(Dictionary<string, object> parameters, out string error)
        {
            List<string> paths = GitParams.GetStringArray(parameters, "paths", out error);
            if (error == null && paths.Count > MaxPaths)
                error = $"Parameter 'paths' must contain at most {MaxPaths} entries.";
            return paths;
        }

        protected static List<string> ResolvePaths(GitRepositoryContext context, List<string> paths)
        {
            var result = new List<string>();
            foreach (string path in paths)
            {
                string repositoryPath = context.ResolveRepoPath(path);
                if (!result.Contains(repositoryPath))
                    result.Add(repositoryPath);
            }

            return result;
        }

        /// <summary>Reads a revision parameter; null when absent, error when malformed.</summary>
        protected static string ReadRevision(Dictionary<string, object> parameters, string name, out string error)
        {
            error = null;
            string value = GitParams.GetString(parameters, name);
            if (value == null)
                return null;

            string revisionError;
            GitRevision.Validate(value, out revisionError);
            if (revisionError != null)
                error = $"Invalid '{name}': {revisionError}";
            return value;
        }

        /// <summary>Diff parameters shared by diff and show.</summary>
        protected sealed class DiffSettings
        {
            private static readonly string[] WhitespaceModes = { "none", "cr_at_eol", "all" };

            public bool IncludeDiff;
            public int MaxFiles;
            public int MaxLines;
            public int ContextLines;
            public string Whitespace;
            public List<string> Paths;

            public static DiffSettings Read(Dictionary<string, object> parameters, out string error)
            {
                var settings = new DiffSettings();
                settings.IncludeDiff = GitParams.GetBool(parameters, "include_diff", false, out error);
                if (error != null) return null;
                settings.MaxFiles = GitParams.GetInt(parameters, "max_files", 50, 1, 100, out error);
                if (error != null) return null;
                settings.MaxLines = GitParams.GetInt(parameters, "max_lines", 2000, 100, 20000, out error);
                if (error != null) return null;
                settings.ContextLines = GitParams.GetInt(parameters, "context_lines", 3, 0, 10, out error);
                if (error != null) return null;
                settings.Whitespace = GitParams.GetEnum(parameters, "ignore_whitespace", "none", WhitespaceModes, out error);
                if (error != null) return null;
                settings.Paths = ReadPaths(parameters, out error);
                return error != null ? null : settings;
            }
        }

        /// <summary>Runs git diff for <paramref name="revisions"/> and fills files, totals, has_more_results and the truncation warning.</summary>
        protected static async Task FillDiffAsync(
            GitReadResponse response,
            GitRepositoryContext context,
            DiffSettings settings,
            string revisions,
            CancellationToken cancellationToken)
        {
            string options = GitArguments.DiffOptions(settings.IncludeDiff, settings.ContextLines, settings.Whitespace);
            string pathspec = GitArguments.Paths(ResolvePaths(context, settings.Paths));
            var files = new List<GitFileEntry>();
            bool hasMore;
            bool truncated;

            if (settings.IncludeDiff)
            {
                GitProcessResult result = await RunAsync(
                    context, GitArguments.Build("diff", options, revisions, pathspec), cancellationToken).ConfigureAwait(false);
                GitProcessGuard.EnsureSuccess(result, $"Git {response.Operation} failed");

                GitDiffData diff = GitDiffParser.Parse(result.StandardOutput, settings.MaxFiles, settings.MaxLines);
                files.AddRange(diff.Files);
                hasMore = diff.HasMoreResults;
                truncated = result.Truncated;
            }
            else
            {
                // git prints only the LAST of --numstat/--name-status when both are given, so the listings are two calls.
                GitProcessResult counts = await RunAsync(
                    context, GitArguments.Build("diff", options, "--numstat -z", revisions, pathspec), cancellationToken).ConfigureAwait(false);
                GitProcessGuard.EnsureSuccess(counts, $"Git {response.Operation} failed");

                GitProcessResult names = await RunAsync(
                    context, GitArguments.Build("diff", options, "--name-status -z", revisions, pathspec), cancellationToken).ConfigureAwait(false);
                GitProcessGuard.EnsureSuccess(names, $"Git {response.Operation} failed");

                List<GitFileEntry> all = GitFileListParser.Parse(counts.StandardOutput, names.StandardOutput);
                hasMore = all.Count > settings.MaxFiles;
                files.AddRange(hasMore ? all.GetRange(0, settings.MaxFiles) : all);
                truncated = counts.Truncated || names.Truncated;
            }

            // The last entry of cut output may be incomplete.
            if (truncated && files.Count > 0)
                files.RemoveAt(files.Count - 1);

            response.Files = new List<GitFileEntry>();
            response.TotalAdded = 0;
            response.TotalRemoved = 0;
            foreach (GitFileEntry file in files)
            {
                file.Path = context.ToDisplayPath(file.Path);
                if (file.From != null)
                    file.From = context.ToDisplayPath(file.From);
                response.Files.Add(file);
                response.TotalAdded += file.Added;
                response.TotalRemoved += file.Removed;
            }

            response.HasMoreResults = hasMore;
            if (truncated)
                MarkTruncated(response);
        }
    }
}
