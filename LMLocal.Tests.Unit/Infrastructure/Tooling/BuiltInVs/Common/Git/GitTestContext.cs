using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Git
{
    internal static class GitTestContext
    {
        public const string RepoRoot = @"C:\repo";
        public const string GitDir = @"C:\repo\.git";
        public const string SolutionDir = @"C:\repo\src\solution";
        public const string HeadSha = "863b43a071db1111111111111111111111111111";

        /// <summary>Canned answers of a repository with commits on branch main.</summary>
        public static GitProcessResult Respond(string arguments)
        {
            if (arguments.Contains("--show-toplevel"))
            {
                string facts = RepoRoot + "\n" + GitDir + "\nfalse\n";
                if (arguments.Contains("--abbrev-ref"))
                    facts += HeadSha + "\nmain\n";
                return new GitProcessResult { ExitCode = 0, StandardOutput = facts };
            }

            if (arguments.Contains("--verify"))
                return new GitProcessResult { ExitCode = 0, StandardOutput = HeadSha + "\n" };

            if (arguments.Contains("show -s"))
            {
                return new GitProcessResult
                {
                    ExitCode = 0,
                    StandardOutput = HeadSha + "\0" + HeadSha + "\0Author\02026-11-24T10:00:00+02:00\0" +
                                     "Committer\02026-11-24T10:00:00+02:00\0message\n"
                };
            }

            return new GitProcessResult { ExitCode = 0, StandardOutput = string.Empty };
        }

        public static FakeGitProcessRunner CreateRunner()
        {
            return new FakeGitProcessRunner { Handler = Respond };
        }

        public static async Task<GitRepositoryContext> CreateAsync(FakeGitProcessRunner runner)
        {
            return await GitRepositoryContext.CreateAsync(new PathResolver(), SolutionDir, runner, default).ConfigureAwait(false);
        }
    }
}
