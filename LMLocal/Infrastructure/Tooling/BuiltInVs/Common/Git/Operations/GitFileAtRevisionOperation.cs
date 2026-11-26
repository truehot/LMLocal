using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Operations
{
    internal sealed class GitFileAtRevisionOperation : GitOperationBase
    {
        private const int DefaultMaxLines = 200;
        private const int MaxMaxLines = 2000;

        public override string Name => "file_at_revision";

        private sealed class Request
        {
            public string Revision;
            public string Path;
            public int Start;
            public int End;
            public int MaxLines;
        }

        public override object Parse(Dictionary<string, object> parameters, out string error)
        {
            var request = new Request();
            request.Revision = ReadRevision(parameters, "revision", out error);
            if (error != null) return null;
            if (request.Revision == null)
            {
                error = "Parameter 'revision' is required.";
                return null;
            }

            request.Path = GitParams.GetString(parameters, "path");
            if (request.Path == null)
            {
                error = "Parameter 'path' is required.";
                return null;
            }

            request.Start = GitParams.GetInt(parameters, "start_line", 1, 1, int.MaxValue, out error);
            if (error != null) return null;
            request.End = GitParams.GetInt(parameters, "end_line", int.MaxValue, 1, int.MaxValue, out error);
            if (error != null) return null;
            if (request.End < request.Start)
            {
                error = "Parameter 'end_line' must be greater than or equal to 'start_line'.";
                return null;
            }

            request.MaxLines = GitParams.GetInt(parameters, "max_lines", DefaultMaxLines, 1, MaxMaxLines, out error);
            return error != null ? null : request;
        }

        public override async Task<GitReadResponse> ExecuteAsync(GitRepositoryContext context, object parsed, CancellationToken cancellationToken)
        {
            var request = (Request)parsed;
            string repositoryPath = context.ResolveRepoPath(request.Path);
            string sha = await GitRevision.ResolveAsync(request.Revision, context, cancellationToken).ConfigureAwait(false);

            // No pathspec separator here: the argument is a single <rev>:<path> object name.
            string arguments = GitArguments.Build("show", "--no-textconv", CommandLineEscaper.Escape(sha + ":" + repositoryPath));
            GitProcessResult result = await RunAsync(context, arguments, cancellationToken).ConfigureAwait(false);
            GitProcessGuard.EnsureSuccess(result, "Git file_at_revision failed");

            if (result.StandardOutput.IndexOf('\0') >= 0)
                throw new GitToolException($"File '{request.Path}' at {request.Revision} is binary and cannot be returned as lines.");

            List<string> allLines = new List<string>(result.StandardOutput.Replace("\r\n", "\n").Split('\n'));
            // A cut file ends with a partial line; a complete file ends with an empty element after the final newline.
            if (allLines.Count > 0 && (result.Truncated || allLines[allLines.Count - 1].Length == 0))
                allLines.RemoveAt(allLines.Count - 1);

            int available = allLines.Count;
            long wantedLast = Math.Min((long)request.End, (long)request.Start + request.MaxLines - 1);
            int last = (int)Math.Min(wantedLast, available);

            var lines = new List<string[]>();
            for (int line = request.Start; line <= last; line++)
                lines.Add(new[] { line.ToString(CultureInfo.InvariantCulture), allLines[line - 1] });

            GitReadResponse response = Ok(context);
            response.Path = context.ToDisplayPath(repositoryPath);
            response.Revision = sha;
            response.Lines = lines;
            response.HasMoreResults = last < Math.Min((long)request.End, available);
            if (result.Truncated && wantedLast > available)
                MarkTruncated(response);
            return response;
        }
    }
}
