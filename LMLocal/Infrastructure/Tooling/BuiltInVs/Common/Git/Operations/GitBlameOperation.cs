using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Parsers;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Operations
{
    internal sealed class GitBlameOperation : GitOperationBase
    {
        private const int MaxBlameLines = 200;
        private static readonly string[] WhitespaceModes = { "none", "all" };

        public override string Name => "blame";

        private sealed class Request
        {
            public string Path;
            public int Start;
            public int End;
            public string Revision;
            public string Whitespace;
        }

        public override object Parse(Dictionary<string, object> parameters, out string error)
        {
            var request = new Request { Path = GitParams.GetString(parameters, "path") };
            if (request.Path == null)
            {
                error = "Parameter 'path' is required.";
                return null;
            }

            request.Start = GitParams.GetInt(parameters, "start_line", 0, 1, int.MaxValue, out error);
            if (error != null) return null;
            request.End = GitParams.GetInt(parameters, "end_line", 0, 1, int.MaxValue, out error);
            if (error != null) return null;
            if (request.Start == 0 || request.End == 0)
            {
                error = "Parameters 'start_line' and 'end_line' are required.";
                return null;
            }

            if (request.End < request.Start || (long)request.End - request.Start + 1 > MaxBlameLines)
            {
                error = $"A blame range must be ordered and span at most {MaxBlameLines} lines.";
                return null;
            }

            request.Revision = ReadRevision(parameters, "revision", out error);
            if (error != null) return null;
            request.Whitespace = GitParams.GetEnum(parameters, "ignore_whitespace", "none", WhitespaceModes, out error);
            return error != null ? null : request;
        }

        public override async Task<GitReadResponse> ExecuteAsync(GitRepositoryContext context, object parsed, CancellationToken cancellationToken)
        {
            var request = (Request)parsed;
            string repositoryPath = context.ResolveRepoPath(request.Path);
            string sha = request.Revision == null
                ? null
                : await GitRevision.ResolveAsync(request.Revision, context, cancellationToken).ConfigureAwait(false);

            string arguments = GitArguments.Build(
                "blame", "--porcelain", "--no-textconv",
                request.Whitespace == "all" ? "-w" : null,
                "-L", request.Start.ToString(CultureInfo.InvariantCulture) + "," + request.End.ToString(CultureInfo.InvariantCulture),
                sha, // blame has no --end-of-options: only a validated sha may appear here
                GitArguments.Paths(new[] { repositoryPath }));

            GitProcessResult result = await RunAsync(context, arguments, cancellationToken).ConfigureAwait(false);
            GitProcessGuard.EnsureSuccess(result, "Git blame failed");

            GitBlameData data = GitBlameParser.Parse(result.StandardOutput);
            GitReadResponse response = Ok(context);
            response.Path = context.ToDisplayPath(repositoryPath);
            response.Ranges = data.Ranges;
            response.UncommittedLines = data.UncommittedLines;
            response.Boundary = data.Boundary;
            if (result.Truncated)
                MarkTruncated(response);
            return response;
        }
    }
}
