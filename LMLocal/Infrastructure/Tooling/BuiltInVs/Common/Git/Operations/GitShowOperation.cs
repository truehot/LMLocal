using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Operations
{
    internal sealed class GitShowOperation : GitOperationBase
    {
        // NUL-separated: git cuts names and messages at NUL, so their text cannot shift the fields.
        private const string MetadataFormat = "--format=%H%x00%P%x00%an%x00%aI%x00%cn%x00%cI%x00%B";

        public override string Name => "show";

        private sealed class Request
        {
            public string Revision;
            public DiffSettings Diff;
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

            request.Diff = DiffSettings.Read(parameters, out error);
            return error != null ? null : request;
        }

        public override async Task<GitReadResponse> ExecuteAsync(GitRepositoryContext context, object parsed, CancellationToken cancellationToken)
        {
            var request = (Request)parsed;
            string sha = await GitRevision.ResolveAsync(request.Revision, context, cancellationToken).ConfigureAwait(false);

            GitProcessResult metadata = await RunAsync(
                context, GitArguments.Build("show", "-s", MetadataFormat, sha), cancellationToken).ConfigureAwait(false);
            GitProcessGuard.EnsureSuccess(metadata, "Git show failed");

            string[] fields = metadata.StandardOutput.Split(new[] { '\0' }, 7);
            if (fields.Length < 7 || !GitRevision.IsSha(fields[0]))
                throw new GitToolException("Cannot read commit metadata.");

            GitReadResponse response = Ok(context);
            response.Revision = fields[0];
            response.Parents = new List<string>(fields[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            response.Author = fields[2];
            response.AuthorDate = fields[3];
            response.Committer = fields[4];
            response.CommitterDate = fields[5];
            response.Message = fields[6].Trim('\r', '\n');

            // A merge commit is shown against its first parent; a root commit against the empty tree.
            string baseSha = await GitRevision.BaseAsync(sha, true, context, cancellationToken).ConfigureAwait(false);
            await FillDiffAsync(response, context, request.Diff, baseSha + " " + sha, cancellationToken).ConfigureAwait(false);
            if (metadata.Truncated)
                MarkTruncated(response);
            return response;
        }
    }
}
