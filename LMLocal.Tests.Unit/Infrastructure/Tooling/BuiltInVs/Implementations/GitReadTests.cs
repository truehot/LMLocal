using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Git;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    [TestFixture]
    public class GitReadTests
    {
        private Mock<IVsDependencies> _vs;
        private FakeGitProcessRunner _runner;
        private GitRead _tool;

        [SetUp]
        public void SetUp()
        {
            _vs = new Mock<IVsDependencies>();
            _vs.Setup(v => v.IsSolutionOpen).Returns(true);
            _vs.Setup(v => v.GetSolutionDirectory()).Returns(GitTestContext.SolutionDir);

            _runner = GitTestContext.CreateRunner();
            var locator = new GitExecutableLocator(typeof(GitReadTests).Assembly.Location, new Version(2, 53, 0));
            _tool = new GitRead(_vs.Object, new PathResolver(), _runner, locator);
        }

        [Test]
        public void GetToolInfo_ExposesSingleReadOnlyToolWithOperationSchema()
        {
            ToolDefinition info = _tool.GetToolInfo();

            Assert.That(_tool.ToolName, Is.EqualTo("git_read"));
            Assert.That(_tool.AccessLevel, Is.EqualTo(ToolAccessLevel.ReadOnly));
            Assert.That(info.Name, Is.EqualTo("git_read"));
            Assert.That(info.Parameters.Required, Is.EqualTo(new[] { "operation" }));
            Assert.That(info.Parameters.Properties.Keys, Does.Contain("operation"));
            Assert.That(info.Parameters.Properties.Keys, Does.Contain("page_token"));
            Assert.That(info.Parameters.Properties.Keys, Does.Contain("line_range"));
            Assert.That(info.Description.ToLowerInvariant(), Does.Contain("read-only"));
            Assert.That(info.Description, Does.Contain("get_changes_diff"));
        }

        [Test]
        public async Task ExecuteAsync_WithoutOperation_ReturnsFailure()
        {
            var response = (GitReadResponse)await _tool.ExecuteAsync(new Dictionary<string, object>());

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("operation"));
            Assert.That(_runner.Calls, Is.Empty);
        }

        [Test]
        public async Task ExecuteAsync_UnknownOperation_ReturnsFailure()
        {
            var response = (GitReadResponse)await _tool.ExecuteAsync(
                new Dictionary<string, object> { ["operation"] = "commit" });

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("Unknown operation"));
            Assert.That(_runner.Calls, Is.Empty);
        }

        [Test]
        public async Task ExecuteAsync_NoSolutionOpen_ReturnsFailure()
        {
            _vs.Setup(v => v.IsSolutionOpen).Returns(false);

            var response = (GitReadResponse)await _tool.ExecuteAsync(
                new Dictionary<string, object> { ["operation"] = "status" });

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("No solution"));
        }

        [Test]
        public async Task ExecuteAsync_InvalidParameters_ReturnsFailureWithoutRunningGit()
        {
            var response = (GitReadResponse)await _tool.ExecuteAsync(
                new Dictionary<string, object> { ["operation"] = "blame", ["path"] = "src/Foo.cs" });

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("start_line"));
            Assert.That(_runner.Calls, Is.Empty);
        }

        [Test]
        public async Task ExecuteAsync_DispatchesToStatusAndFillsEnvelope()
        {
            var response = (GitReadResponse)await _tool.ExecuteAsync(
                new Dictionary<string, object> { ["operation"] = "status" });

            Assert.That(response.Success, Is.True, response.ErrorMessage);
            Assert.That(response.Operation, Is.EqualTo("status"));
            Assert.That(response.RepoRoot, Is.EqualTo(GitTestContext.RepoRoot));
            Assert.That(response.Head, Is.EqualTo(GitTestContext.HeadSha));
            Assert.That(response.Branch, Is.EqualTo("main"));
            Assert.That(_runner.Calls.Count, Is.EqualTo(2), "one rev-parse for the repository facts plus git status");
        }

        [Test]
        public async Task ExecuteAsync_WhenTheCallBudgetRunsOut_ReturnsTimedOutResponse()
        {
            _runner.HangOn = "status ";

            var response = (GitReadResponse)await _tool.ExecuteAsync(
                new Dictionary<string, object> { ["operation"] = "status" }, TimeSpan.FromMilliseconds(200), CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.TimedOut, Is.True);
            Assert.That(response.ErrorMessage, Does.Contain("Narrow paths"));
        }

        [Test]
        public void ExecuteAsync_WhenTheUserStops_ThrowsCancellation()
        {
            _runner.HangOn = "status ";
            using (var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
            {
                Assert.CatchAsync<OperationCanceledException>(async () => await _tool.ExecuteAsync(
                    new Dictionary<string, object> { ["operation"] = "status" }, TimeSpan.FromSeconds(30), stop.Token));
            }
        }

        [Test]
        public async Task ExecuteAsync_WhenGitIsTooOld_ReturnsFailureWithoutRunningGit()
        {
            var locator = new GitExecutableLocator(typeof(GitReadTests).Assembly.Location, new Version(2, 20, 1));
            var tool = new GitRead(_vs.Object, new PathResolver(), _runner, locator);

            var response = (GitReadResponse)await tool.ExecuteAsync(new Dictionary<string, object> { ["operation"] = "status" });

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorMessage, Does.Contain("2.24"));
            Assert.That(_runner.Calls, Is.Empty);
        }

        [Test]
        public void Messages_FollowThePlan()
        {
            Assert.That(_tool.GetProcessingMessage(new Dictionary<string, object> { ["operation"] = "status" }),
                Is.EqualTo("Git status"));
            Assert.That(_tool.GetProcessingMessage(new Dictionary<string, object>
            {
                ["operation"] = "blame", ["path"] = "Foo.cs", ["start_line"] = 10, ["end_line"] = 40
            }), Is.EqualTo("Git blame: Foo.cs:10-40"));

            var response = new GitReadResponse
            {
                Success = true,
                Operation = "status",
                Branch = "main",
                Ahead = 1,
                Behind = 0,
                Staged = new List<GitStatusEntry> { new GitStatusEntry() },
                Unstaged = new List<GitStatusEntry> { new GitStatusEntry(), new GitStatusEntry() },
                Untracked = new List<string> { "C.cs" }
            };

            Assert.That(_tool.GetCompletionMessage(response), Is.EqualTo("1 staged, 2 unstaged, 1 untracked • main +1/-0"));
        }
    }
}
