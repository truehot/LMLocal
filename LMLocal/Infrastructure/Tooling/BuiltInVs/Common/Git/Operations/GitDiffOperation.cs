using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Operations
{
    internal sealed class GitDiffOperation : GitOperationBase
    {
        private static readonly string[] Scopes = { "unstaged", "staged", "all" };

        public override string Name => "diff";

        private sealed class Request
        {
            public string Scope;
            public string From;
            public string To;
            public bool IncludeFromCommit;
            public DiffSettings Diff;
        }

        public override object Parse(Dictionary<string, object> parameters, out string error)
        {
            var request = new Request();
            request.From = ReadRevision(parameters, "from", out error);
            if (error != null) return null;
            request.To = ReadRevision(parameters, "to", out error);
            if (error != null) return null;
            request.Scope = GitParams.GetEnum(parameters, "scope", null, Scopes, out error);
            if (error != null) return null;

            if (request.Scope != null && (request.From != null || request.To != null))
            {
                error = "Use either 'scope' or 'from'/'to', not both.";
                return null;
            }

            if (request.To != null && request.From == null)
            {
                error = "Parameter 'to' requires 'from'.";
                return null;
            }

            request.IncludeFromCommit = GitParams.GetBool(parameters, "include_from_commit", false, out error);
            if (error != null) return null;
            if (request.IncludeFromCommit && request.From == null)
            {
                error = "Parameter 'include_from_commit' requires 'from'.";
                return null;
            }

            if (request.Scope == null && request.From == null)
                request.Scope = "all";

            request.Diff = DiffSettings.Read(parameters, out error);
            return error != null ? null : request;
        }

        public override async Task<GitReadResponse> ExecuteAsync(GitRepositoryContext context, object parsed, CancellationToken cancellationToken)
        {
            var request = (Request)parsed;
            string revisions;
            switch (request.Scope)
            {
                case "unstaged":
                    revisions = null;
                    break;
                case "staged":
                    revisions = "--cached";
                    break;
                case "all":
                    // Without commits the working tree is compared to the empty tree.
                    revisions = context.Head ?? GitRevision.EmptyTreeSha;
                    break;
                default:
                    string fromSha = await GitRevision.ResolveAsync(request.From, context, cancellationToken).ConfigureAwait(false);
                    string baseSha = await GitRevision.BaseAsync(fromSha, request.IncludeFromCommit, context, cancellationToken).ConfigureAwait(false);
                    revisions = request.To == null
                        ? baseSha // from .. working tree
                        : baseSha + " " + await GitRevision.ResolveAsync(request.To, context, cancellationToken).ConfigureAwait(false);
                    break;
            }

            GitReadResponse response = Ok(context);
            await FillDiffAsync(response, context, request.Diff, revisions, cancellationToken).ConfigureAwait(false);
            return response;
        }
    }
}
