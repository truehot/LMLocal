using System;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Operations;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Git
{
    [TestFixture]
    public class GitRevisionTests
    {
        [TestCase("HEAD~1")]
        [TestCase("stash@{0}")]
        [TestCase("3.3.0")]
        [TestCase("634a8ae")]
        [TestCase("feature/fix-1")]
        public void Validate_AcceptsSupportedRevisions(string revision)
        {
            string error;
            Assert.That(GitRevision.Validate(revision, out error), Is.EqualTo(revision));
            Assert.That(error, Is.Null);
        }

        [TestCase("-x")]
        [TestCase("a..b")]
        [TestCase("HEAD\nfoo")]
        [TestCase("bad name")]
        public void Validate_RejectsUnsafeRevisions(string revision)
        {
            string error;
            Assert.That(GitRevision.Validate(revision, out error), Is.Null);
            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void Validate_RejectsTooLongRevision()
        {
            string error;
            Assert.That(GitRevision.Validate(new string('a', 201), out error), Is.Null);
            Assert.That(error, Does.Contain("too long"));
        }

        [Test]
        public async Task ResolveAsync_UsesEndOfOptionsAndPassesOnlyShaToCallers()
        {
            var runner = GitTestContext.CreateRunner();
            var context = await GitTestContext.CreateAsync(runner);

            string sha = await GitRevision.ResolveAsync("HEAD~1", context, default);

            Assert.That(sha, Is.EqualTo(GitTestContext.HeadSha));
            Assert.That(runner.LastCall, Does.Contain("--end-of-options"));
            Assert.That(runner.LastCall, Does.Contain("HEAD~1^{commit}"));
            Assert.That(runner.LastCall, Does.Contain("-c core.fsmonitor=false"));
        }

        [Test]
        public async Task ResolveAsync_WhenNotFound_ThrowsModelFacingError()
        {
            var runner = GitTestContext.CreateRunner();
            runner.Handler = arguments => arguments.Contains("--show-toplevel")
                ? GitTestContext.Respond(arguments)
                : new GitProcessResult { ExitCode = 1, StandardError = "fatal: ambiguous argument" };
            var context = await GitTestContext.CreateAsync(runner);

            var exception = Assert.ThrowsAsync<GitToolException>(async () =>
                await GitRevision.ResolveAsync("nope", context, default));

            Assert.That(exception.Message, Is.EqualTo("Revision not found: nope"));
        }

        [Test]
        public async Task BaseAsync_WithIncludeFromCommit_UsesParent()
        {
            var runner = GitTestContext.CreateRunner();
            runner.Handler = arguments =>
            {
                if (arguments.Contains("--show-toplevel"))
                    return GitTestContext.Respond(arguments);
                if (arguments.Contains("^"))
                    return new GitProcessResult { ExitCode = 0, StandardOutput = "1111111111111111111111111111111111111111\n" };
                return new GitProcessResult { ExitCode = 0, StandardOutput = GitTestContext.HeadSha + "\n" };
            };
            var context = await GitTestContext.CreateAsync(runner);

            string sha = await GitRevision.BaseAsync(GitTestContext.HeadSha, true, context, default);

            Assert.That(sha, Is.EqualTo("1111111111111111111111111111111111111111"));
            Assert.That(runner.LastCall, Does.Contain(GitTestContext.HeadSha + "^"));
        }

        [Test]
        public async Task BaseAsync_ForRootCommit_UsesEmptyTree()
        {
            var runner = GitTestContext.CreateRunner();
            runner.Handler = arguments => arguments.Contains("--show-toplevel")
                ? GitTestContext.Respond(arguments)
                : new GitProcessResult { ExitCode = 1, StandardError = "fatal: unknown revision" };
            var context = await GitTestContext.CreateAsync(runner);

            string sha = await GitRevision.BaseAsync(GitTestContext.HeadSha, true, context, default);

            Assert.That(sha, Is.EqualTo(GitRevision.EmptyTreeSha));
        }
    }
}
