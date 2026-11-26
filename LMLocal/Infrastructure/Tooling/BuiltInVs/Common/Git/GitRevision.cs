using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git
{
    /// <summary>
    /// The single place where a model-supplied revision is validated and resolved to a full sha.
    /// Only the resulting sha is ever placed on a git command line (blame has no --end-of-options).
    /// </summary>
    internal static class GitRevision
    {
        /// <summary>Empty tree of a SHA-1 repository: the base of a root commit's diff.</summary>
        public const string EmptyTreeSha = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

        private const int MaxLength = 200;

        public static string Validate(string revision, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(revision))
            {
                error = "Revision must not be empty.";
                return null;
            }

            string value = revision.Trim();
            if (value.Length > MaxLength)
            {
                error = $"Revision is too long (maximum {MaxLength} characters).";
                return null;
            }

            if (value[0] == '-')
            {
                error = "Revision must not start with '-'.";
                return null;
            }

            if (value.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                error = "Revision must not contain '..'; use 'from' and 'to' for a range.";
                return null;
            }

            foreach (char c in value)
            {
                if (char.IsControl(c))
                {
                    error = "Revision must not contain control characters.";
                    return null;
                }

                bool allowed = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                               c == '.' || c == '_' || c == '/' || c == '@' || c == '{' || c == '}' ||
                               c == '~' || c == '^' || c == ':' || c == '-';
                if (!allowed)
                {
                    error = $"Revision contains a forbidden character: '{c}'.";
                    return null;
                }
            }

            return value;
        }

        public static async Task<string> ResolveAsync(string revision, GitRepositoryContext context, CancellationToken cancellationToken)
        {
            string error;
            string value = Validate(revision, out error);
            if (error != null)
                throw new GitToolException("Invalid revision: " + error);

            GitProcessResult result = await context.Runner.RunAsync(
                GitArguments.Build("rev-parse", "--verify", "--quiet", "--end-of-options", value + "^{commit}"),
                context.RepoRoot, cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0)
                throw new GitToolException($"Revision not found: {value}");

            string sha = result.StandardOutput.Trim();
            if (!IsSha(sha))
                throw new GitToolException($"Revision '{value}' did not resolve to a commit sha.");

            return sha;
        }

        /// <summary>Base of a diff/log range: the parent of <paramref name="sha"/>, or the empty tree for a root commit.</summary>
        public static async Task<string> BaseAsync(string sha, bool includeFromCommit, GitRepositoryContext context, CancellationToken cancellationToken)
        {
            if (!includeFromCommit)
                return sha;

            GitProcessResult result = await context.Runner.RunAsync(
                GitArguments.Build("rev-parse", "--verify", "--quiet", sha + "^"),
                context.RepoRoot, cancellationToken).ConfigureAwait(false);

            return result.ExitCode == 0 && IsSha(result.StandardOutput.Trim())
                ? result.StandardOutput.Trim()
                : EmptyTreeSha;
        }

        public static bool IsSha(string value)
        {
            if (value == null || (value.Length != 40 && value.Length != 64))
                return false;

            foreach (char c in value)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            }

            return true;
        }
    }
}
