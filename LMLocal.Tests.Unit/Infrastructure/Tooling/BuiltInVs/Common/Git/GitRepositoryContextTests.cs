using System.Linq;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Git
{
    [TestFixture]
    public class GitRepositoryContextTests
    {
        [Test]
        public async Task CreateAsync_ReadsRepositoryFactsInOneCall()
        {
            var runner = GitTestContext.CreateRunner();
            var context = await GitTestContext.CreateAsync(runner);

            Assert.That(context.RepoRoot, Is.EqualTo(GitTestContext.RepoRoot));
            Assert.That(context.GitDir, Is.EqualTo(GitTestContext.GitDir));
            Assert.That(context.Head, Is.EqualTo(GitTestContext.HeadSha));
            Assert.That(context.Branch, Is.EqualTo("main"));
            Assert.That(context.Detached, Is.False);
            Assert.That(context.Shallow, Is.False);
            Assert.That(context.InProgress, Is.Null);
            Assert.That(runner.Calls.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task CreateAsync_DetachedHead_HasNoBranch()
        {
            var runner = GitTestContext.CreateRunner();
            runner.Handler = arguments => new GitProcessResult
            {
                ExitCode = 0,
                StandardOutput = GitTestContext.RepoRoot + "\n" + GitTestContext.GitDir + "\nfalse\n" + GitTestContext.HeadSha + "\nHEAD\n"
            };

            var context = await GitTestContext.CreateAsync(runner);

            Assert.That(context.Detached, Is.True);
            Assert.That(context.Branch, Is.Null);
            Assert.That(context.Head, Is.EqualTo(GitTestContext.HeadSha));
        }

        [Test]
        public async Task CreateAsync_RepositoryWithoutCommits_FallsBackWithoutHead()
        {
            var runner = GitTestContext.CreateRunner();
            runner.Handler = arguments => arguments.Contains("--abbrev-ref")
                ? new GitProcessResult { ExitCode = 128, StandardError = "fatal: ambiguous argument 'HEAD'" }
                : GitTestContext.Respond(arguments);

            var context = await GitTestContext.CreateAsync(runner);

            Assert.That(context.RepoRoot, Is.EqualTo(GitTestContext.RepoRoot));
            Assert.That(context.Head, Is.Null);
            Assert.That(context.Detached, Is.False);
            Assert.That(runner.Calls.Count, Is.EqualTo(2));
            Assert.That(runner.Calls.Last(), Does.Not.Contain("HEAD"));
        }

        [Test]
        public void CreateAsync_NotARepository_ThrowsModelFacingError()
        {
            var runner = new FakeGitProcessRunner { DefaultExitCode = 128, DefaultError = "fatal: not a git repository" };

            var exception = Assert.ThrowsAsync<GitToolException>(async () => await GitTestContext.CreateAsync(runner));

            Assert.That(exception.Message, Does.StartWith("Not a git repository"));
        }

        [Test]
        public async Task TryResolveRepoPath_AcceptsSolutionRelativePath()
        {
            var context = await GitTestContext.CreateAsync(GitTestContext.CreateRunner());

            string relative;
            string error;
            bool ok = context.TryResolveRepoPath(@"src\Foo.cs", out relative, out error);

            Assert.That(ok, Is.True, error);
            Assert.That(relative, Is.EqualTo("src/solution/src/Foo.cs"));
        }

        [Test]
        public async Task TryResolveRepoPath_RejectsPathOutsideRepository()
        {
            var context = await GitTestContext.CreateAsync(GitTestContext.CreateRunner());

            string relative;
            string error;
            bool ok = context.TryResolveRepoPath(@"C:\other\Foo.cs", out relative, out error);

            Assert.That(ok, Is.False);
            Assert.That(error, Does.Contain("outside the repository"));
        }

        [Test]
        public async Task TryResolveRepoPath_RejectsPathInsideGitDirectory()
        {
            var context = await GitTestContext.CreateAsync(GitTestContext.CreateRunner());

            string relative;
            string error;
            bool ok = context.TryResolveRepoPath(@"..\..\.git\config", out relative, out error);

            Assert.That(ok, Is.False);
            Assert.That(error, Does.Contain(".git"));
        }

        [Test]
        public async Task ToDisplayPath_UsesSolutionRelativePathWhenPossible()
        {
            var context = await GitTestContext.CreateAsync(GitTestContext.CreateRunner());

            Assert.That(context.ToDisplayPath("src/solution/src/Foo.cs"), Is.EqualTo("src/Foo.cs"));
            Assert.That(context.ToDisplayPath("docs/readme.md"), Is.EqualTo("docs/readme.md"));
        }
    }
}
