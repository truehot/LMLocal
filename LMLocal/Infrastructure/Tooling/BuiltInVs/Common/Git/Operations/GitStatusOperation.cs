using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Operations
{
    internal sealed class GitStatusOperation : GitOperationBase
    {
        private static readonly string[] UntrackedModes = { "normal", "all", "no" };

        public override string Name => "status";

        private sealed class Request
        {
            public string Untracked;
            public List<string> Paths;
        }

        public override object Parse(Dictionary<string, object> parameters, out string error)
        {
            var request = new Request();
            request.Untracked = GitParams.GetEnum(parameters, "untracked", "normal", UntrackedModes, out error);
            if (error != null) return null;
            request.Paths = ReadPaths(parameters, out error);
            return error != null ? null : request;
        }

        public override async Task<GitReadResponse> ExecuteAsync(GitRepositoryContext context, object parsed, CancellationToken cancellationToken)
        {
            var request = (Request)parsed;
            string arguments = GitArguments.Build(
                "status", "--porcelain=v2", "--branch", "-z", "--untracked-files=" + request.Untracked,
                GitArguments.Paths(ResolvePaths(context, request.Paths)));

            GitProcessResult result = await RunAsync(context, arguments, cancellationToken).ConfigureAwait(false);
            GitProcessGuard.EnsureSuccess(result, "Git status failed");

            GitStatusData data = GitStatusParser.Parse(result.StandardOutput);
            GitReadResponse response = Ok(context);
            response.Detached = context.Detached;
            response.Upstream = data.Upstream;
            response.Ahead = data.Ahead;
            response.Behind = data.Behind;
            response.InProgress = context.InProgress;
            response.Staged = MapEntries(data.Staged, context);
            response.Unstaged = MapEntries(data.Unstaged, context);
            response.Conflicted = MapEntries(data.Conflicted, context);

            response.Untracked = new List<string>();
            foreach (string path in data.Untracked)
                response.Untracked.Add(context.ToDisplayPath(path));

            response.Renamed = new List<GitRename>();
            foreach (GitRename rename in data.Renamed)
            {
                rename.From = context.ToDisplayPath(rename.From);
                rename.To = context.ToDisplayPath(rename.To);
                response.Renamed.Add(rename);
            }

            response.Submodules = new List<GitSubmodule>();
            foreach (GitSubmodule submodule in data.Submodules)
            {
                submodule.Path = context.ToDisplayPath(submodule.Path);
                response.Submodules.Add(submodule);
            }

            if (result.Truncated)
                MarkTruncated(response);
            return response;
        }

        private static List<GitStatusEntry> MapEntries(List<GitStatusEntry> source, GitRepositoryContext context)
        {
            var result = new List<GitStatusEntry>();
            foreach (GitStatusEntry entry in source)
            {
                entry.Path = context.ToDisplayPath(entry.Path);
                result.Add(entry);
            }

            return result;
        }
    }
}
