using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Git
{
    /// <summary>Exercises the real process plumbing with cmd.exe standing in for git (no git install needed).</summary>
    [TestFixture]
    public class GitProcessRunnerTests
    {
        private GitProcessRunner _runner;
        private string _workingDirectory;

        [SetUp]
        public void SetUp()
        {
            string cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            _runner = new GitProcessRunner(new GitExecutableLocator(cmd, new Version(2, 53, 0)));
            _workingDirectory = Path.GetTempPath();
        }

        [Test]
        public async Task RunAsync_ReturnsTheRealExitCode()
        {
            GitProcessResult result = await _runner.RunAsync("/c exit 3", _workingDirectory, CancellationToken.None);

            Assert.That(result.ExitCode, Is.EqualTo(3));
        }

        [Test]
        public async Task RunAsync_OutputBeyondTheCap_IsTruncated()
        {
            GitProcessResult result = await _runner.RunAsync(
                "/c for /L %i in (1,1,500) do @echo 0123456789", _workingDirectory, CancellationToken.None, 100);

            Assert.That(result.Truncated, Is.True);
            Assert.That(result.StandardOutput.Length, Is.EqualTo(100));
            Assert.That(result.ExitCode, Is.EqualTo(0));
        }

        [Test]
        public async Task RunAsync_OutputExactlyAtTheCap_IsNotTruncated()
        {
            GitProcessResult result = await _runner.RunAsync("/c echo abc", _workingDirectory, CancellationToken.None, 5);

            Assert.That(result.StandardOutput, Is.EqualTo("abc\r\n"));
            Assert.That(result.Truncated, Is.False);
        }

        [Test]
        public void RunAsync_Cancellation_KillsTheProcessPromptly()
        {
            var watch = Stopwatch.StartNew();
            using (var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
            {
                Assert.CatchAsync<OperationCanceledException>(async () =>
                    await _runner.RunAsync("/c ping -n 30 127.0.0.1 >nul", _workingDirectory, stop.Token));
            }

            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        }

        [Test]
        public async Task RunAsync_PipeHeldByAnOrphan_ReturnsAfterTheDrainLimitAsTruncated()
        {
            // cmd exits at once; the detached ping inherits stdout and keeps it open for ~20 s.
            var watch = Stopwatch.StartNew();
            GitProcessResult result = await _runner.RunAsync("/c start /b ping -n 20 127.0.0.1", _workingDirectory, CancellationToken.None);

            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
            Assert.That(result.ExitCode, Is.EqualTo(0));
            Assert.That(result.Truncated, Is.True);
        }

        [Test]
        public void RunAsync_CancelWhileAnOrphanHoldsThePipe_ThrowsPromptly()
        {
            // The inner cmd exits at once, so its ping is an orphan outside the tree that taskkill /T reaches,
            // while the outer cmd keeps running until it is cancelled.
            var watch = Stopwatch.StartNew();
            using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            {
                Assert.CatchAsync<OperationCanceledException>(async () =>
                    await _runner.RunAsync("/c start /b cmd /c start /b ping -n 20 127.0.0.1 & ping -n 20 127.0.0.1 >nul", _workingDirectory, stop.Token));
            }

            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        }

        [Test]
        public void CreateStartInfo_DropsInheritedGitVariablesAndSetsReadOnlyEnvironment()
        {
            string previous = Environment.GetEnvironmentVariable("GIT_DIR");
            Environment.SetEnvironmentVariable("GIT_DIR", @"C:\elsewhere\.git");
            try
            {
                ProcessStartInfo info = GitProcessRunner.CreateStartInfo("git.exe", "status", _workingDirectory);

                Assert.That(info.EnvironmentVariables.ContainsKey("GIT_DIR"), Is.False);
                Assert.That(info.EnvironmentVariables["GIT_OPTIONAL_LOCKS"], Is.EqualTo("0"));
                Assert.That(info.EnvironmentVariables["GIT_TERMINAL_PROMPT"], Is.EqualTo("0"));
                Assert.That(info.EnvironmentVariables["GIT_LITERAL_PATHSPECS"], Is.EqualTo("1"));
                Assert.That(info.UseShellExecute, Is.False);
                Assert.That(info.RedirectStandardInput, Is.True);
            }
            finally
            {
                Environment.SetEnvironmentVariable("GIT_DIR", previous);
            }
        }
    }
}
