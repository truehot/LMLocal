using System.Collections.Generic;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Operations;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Git.Operations
{
    [TestFixture]
    public class GitOperationArgvTests
    {
        private FakeGitProcessRunner _runner;
        private GitRepositoryContext _context;

        [SetUp]
        public async Task SetUp()
        {
            _runner = GitTestContext.CreateRunner();
            _context = await GitTestContext.CreateAsync(_runner);
        }

        private async Task<GitReadResponse> RunAsync(GitOperationBase operation, Dictionary<string, object> parameters)
        {
            string error;
            object request = operation.Parse(parameters, out error);
            Assert.That(error, Is.Null);
            return await operation.ExecuteAsync(_context, request, default);
        }

        private static string ParseError(GitOperationBase operation, Dictionary<string, object> parameters)
        {
            string error;
            object request = operation.Parse(parameters, out error);
            Assert.That(request, Is.Null);
            return error;
        }

        [Test]
        public async Task Status_UsesPorcelainV2AndPathspecSeparator()
        {
            var response = await RunAsync(new GitStatusOperation(),
                new Dictionary<string, object> { ["untracked"] = "all", ["paths"] = new[] { "src/Foo.cs" } });

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(response.Operation, Is.EqualTo("status"));
            Assert.That(_runner.LastCall, Does.Contain("status --porcelain=v2 --branch -z --untracked-files=all"));
            Assert.That(_runner.LastCall, Does.Contain(" -- src/solution/src/Foo.cs"));
            Assert.That(_runner.LastCall, Does.Contain("-c core.fsmonitor=false"));
            Assert.That(_runner.LastCall, Does.Contain("-c log.showSignature=false"));
        }

        [Test]
        public async Task Diff_ByDefault_ComparesWorkingTreeWithHead()
        {
            var response = await RunAsync(new GitDiffOperation(), new Dictionary<string, object>());

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(_runner.LastCall, Does.Contain("--name-status -z " + GitTestContext.HeadSha));
            Assert.That(_runner.LastCall, Does.Not.Contain("--cached"));
        }

        [Test]
        public async Task Diff_WithoutIncludeDiff_UsesNumstatAndNameStatus()
        {
            var response = await RunAsync(new GitDiffOperation(), new Dictionary<string, object> { ["from"] = "HEAD~1" });

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(_runner.LastCall, Does.Contain("diff "));
            Assert.That(_runner.Calls.Exists(c => c.Contains("--numstat -z")), Is.True);
            Assert.That(_runner.Calls.Exists(c => c.Contains("--name-status -z")), Is.True);
            Assert.That(_runner.LastCall, Does.Contain(GitTestContext.HeadSha));
            Assert.That(_runner.LastCall, Does.Contain(" --"));
        }

        [Test]
        public async Task Diff_WithIncludeDiff_UsesUnifiedDiffAndNoTextconv()
        {
            var response = await RunAsync(new GitDiffOperation(),
                new Dictionary<string, object>
                {
                    ["scope"] = "staged", ["include_diff"] = true, ["context_lines"] = 5, ["ignore_whitespace"] = "cr_at_eol"
                });

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(_runner.LastCall, Does.Contain("--cached"));
            Assert.That(_runner.LastCall, Does.Contain("-U5"));
            Assert.That(_runner.LastCall, Does.Contain("--no-textconv"));
            Assert.That(_runner.LastCall, Does.Contain("--ignore-cr-at-eol"));
            Assert.That(_runner.LastCall, Does.Not.Contain("--numstat"));
        }

        [Test]
        public void Diff_ScopeTogetherWithFrom_IsRejectedBeforeGitRuns()
        {
            string error = ParseError(new GitDiffOperation(),
                new Dictionary<string, object> { ["scope"] = "staged", ["from"] = "HEAD~1" });

            Assert.That(error, Does.Contain("either 'scope' or 'from'"));
        }

        [Test]
        public async Task Diff_TruncatedOutput_DropsLastFileAndWarns()
        {
            _runner.Handler = arguments => arguments.Contains("--numstat")
                ? new GitProcessResult { ExitCode = 0, StandardOutput = "1\t0\ta.cs\u00002\t0\tb.cs\u0000", Truncated = true }
                : arguments.Contains("--name-status")
                    ? new GitProcessResult { ExitCode = 0, StandardOutput = "M\u0000a.cs\u0000M\u0000b.cs\u0000" }
                    : GitTestContext.Respond(arguments);

            var response = await RunAsync(new GitDiffOperation(), new Dictionary<string, object>());

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(response.Files.Count, Is.EqualTo(1));
            Assert.That(response.HasMoreResults, Is.True);
            Assert.That(response.Warning, Does.Contain("4 MB"));
        }

        [Test]
        public async Task Log_UsesFixedStringSearchAndEndOfOptions()
        {
            var response = await RunAsync(new GitLogOperation(),
                new Dictionary<string, object>
                {
                    ["message_contains"] = "fix bug",
                    ["content_added_or_removed"] = "Foo()",
                    ["max_count"] = 5
                });

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(_runner.LastCall, Does.Contain("-z --format=%H%x00%h%x00%an%x00%aI%x00%s"));
            Assert.That(_runner.LastCall, Does.Contain("--fixed-strings"));
            Assert.That(_runner.LastCall, Does.Contain("\"--grep=fix bug\""));
            Assert.That(_runner.LastCall, Does.Contain("-SFoo()"));
            Assert.That(_runner.LastCall, Does.Contain("--end-of-options"));
            Assert.That(_runner.LastCall, Does.Contain(" --"));
        }

        [Test]
        public async Task Log_SearchText_KeepsSurroundingSpaces()
        {
            var response = await RunAsync(new GitLogOperation(),
                new Dictionary<string, object> { ["content_added_or_removed"] = " var ", ["message_contains"] = " fix" });

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(_runner.LastCall, Does.Contain("\"-S var \""));
            Assert.That(_runner.LastCall, Does.Contain("\"--grep= fix\""));
        }

        [TestCase("message_contains")]
        [TestCase("content_added_or_removed")]
        [TestCase("content_regex")]
        [TestCase("author")]
        [TestCase("since")]
        [TestCase("until")]
        public void Log_SearchTextWithNul_IsRejectedBeforeGitRuns(string name)
        {
            string error = ParseError(new GitLogOperation(), new Dictionary<string, object> { [name] = "abc\0def" });

            Assert.That(error, Does.Contain("NUL"));
        }

        [Test]
        public async Task Log_WithPageToken_AnchorsToTokenShaAndSkipsOffset()
        {
            string sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var response = await RunAsync(new GitLogOperation(),
                new Dictionary<string, object> { ["page_token"] = sha + ":20", ["max_count"] = 10 });

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(_runner.LastCall, Does.Contain("--skip=20"));
            Assert.That(_runner.LastCall, Does.Contain(sha));
            Assert.That(_runner.LastCall, Does.Not.Contain(GitTestContext.HeadSha));
        }

        [Test]
        public async Task Log_TruncatedOutput_DropsLastCommitAndContinuesFromIt()
        {
            string Commit(char c) => new string(c, 40) + "\0" + new string(c, 7) + "\0A\02026-11-24T10:00:00+02:00\0s\0";
            _runner.Handler = arguments => arguments.Contains("log ")
                ? new GitProcessResult { ExitCode = 0, StandardOutput = Commit('a') + Commit('b') + Commit('c'), Truncated = true }
                : GitTestContext.Respond(arguments);

            var response = await RunAsync(new GitLogOperation(), new Dictionary<string, object> { ["max_count"] = 10 });

            Assert.That(response.Commits.Count, Is.EqualTo(2));
            Assert.That(response.HasMoreResults, Is.True);
            Assert.That(response.PageToken, Is.EqualTo(GitTestContext.HeadSha + ":2"));
            Assert.That(response.Warning, Is.Not.Null);
        }

        [Test]
        public async Task Log_LineRange_IsOneQuotedArgument()
        {
            var response = await RunAsync(new GitLogOperation(),
                new Dictionary<string, object>
                {
                    ["line_range"] = JObject.FromObject(new { path = "src/My File.cs", start_line = 3, end_line = 9 })
                });

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(_runner.LastCall, Does.Contain("-L \"3,9:src/solution/src/My File.cs\""));
        }

        [Test]
        public void Log_LineRangeWithPaths_IsRejected()
        {
            string error = ParseError(new GitLogOperation(),
                new Dictionary<string, object>
                {
                    ["paths"] = new[] { "src/Foo.cs" },
                    ["line_range"] = JObject.FromObject(new { path = "src/Foo.cs", start_line = 1, end_line = 2 })
                });

            Assert.That(error, Does.Contain("cannot be combined"));
        }

        [Test]
        public async Task Show_ReadsMetadataAndDiffsAgainstFirstParent()
        {
            var response = await RunAsync(new GitShowOperation(), new Dictionary<string, object> { ["revision"] = "HEAD" });

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(_runner.Calls.Exists(c => c.Contains("show -s --format=")), Is.True);
            Assert.That(_runner.LastCall, Does.Contain("diff "));
            Assert.That(_runner.LastCall, Does.Contain("--no-textconv"));
        }

        [Test]
        public async Task Blame_PassesOnlyValidatedShaAndPathAfterSeparator()
        {
            var response = await RunAsync(new GitBlameOperation(),
                new Dictionary<string, object>
                {
                    ["path"] = "src/Foo.cs", ["start_line"] = 10, ["end_line"] = 20, ["revision"] = "HEAD"
                });

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(_runner.LastCall, Does.Contain("blame --porcelain --no-textconv"));
            Assert.That(_runner.LastCall, Does.Contain("-L 10,20"));
            Assert.That(_runner.LastCall, Does.Contain(GitTestContext.HeadSha + " -- src/solution/src/Foo.cs"));
            Assert.That(_runner.LastCall, Does.Not.Contain("--end-of-options"));
        }

        [Test]
        public async Task FileAtRevision_UsesRevColonPathAsSingleArgument()
        {
            var response = await RunAsync(new GitFileAtRevisionOperation(),
                new Dictionary<string, object> { ["revision"] = "HEAD", ["path"] = "src/Foo.cs" });

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(_runner.LastCall, Does.Contain("show --no-textconv"));
            Assert.That(_runner.LastCall, Does.Contain(GitTestContext.HeadSha + ":src/solution/src/Foo.cs"));
        }

        [Test]
        public async Task FileAtRevision_TruncatedOutput_DropsPartialLineAndWarns()
        {
            _runner.Handler = arguments => arguments.Contains("show --no-textconv")
                ? new GitProcessResult { ExitCode = 0, StandardOutput = "one\ntwo\nthr", Truncated = true }
                : GitTestContext.Respond(arguments);

            var response = await RunAsync(new GitFileAtRevisionOperation(),
                new Dictionary<string, object> { ["revision"] = "HEAD", ["path"] = "src/Foo.cs" });

            Assert.That(response.Lines.Count, Is.EqualTo(2));
            Assert.That(response.Lines[1][1], Is.EqualTo("two"));
            Assert.That(response.HasMoreResults, Is.True);
            Assert.That(response.Warning, Does.Contain("4 MB"));
        }

        [Test]
        public async Task FileAtRevision_HugeStartLine_DoesNotOverflow()
        {
            _runner.Handler = arguments => arguments.Contains("show --no-textconv")
                ? new GitProcessResult { ExitCode = 0, StandardOutput = "one\n" }
                : GitTestContext.Respond(arguments);

            var response = await RunAsync(new GitFileAtRevisionOperation(),
                new Dictionary<string, object> { ["revision"] = "HEAD", ["path"] = "src/Foo.cs", ["start_line"] = int.MaxValue - 1 });

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(response.Lines, Is.Empty);
            Assert.That(response.HasMoreResults, Is.False);
        }
    }
}
