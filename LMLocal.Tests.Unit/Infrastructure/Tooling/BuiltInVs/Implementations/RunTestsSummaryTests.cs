using System;
using System.Text;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Testing;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    [TestFixture]
    public class RunTestsSummaryTests
    {
        private static DotnetProcessResult Result(int exitCode, string stdout, bool timedOut = false)
        {
            return new DotnetProcessResult
            {
                ExitCode = exitCode,
                StdOut = stdout,
                StdErr = string.Empty,
                TimedOut = timedOut
            };
        }

        [TestCase(@"C:\s\App.Tests.csproj", true)]
        [TestCase(@"C:\s\App.Tests.vbproj", true)]
        [TestCase(@"C:\s\App.Tests.fsproj", true)]
        [TestCase(@"C:\s\App.Tests.FSPROJ", true)]
        [TestCase(@"C:\s\App.vcxproj", false)]
        [TestCase(@"C:\s\App.Tests.dll", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsSupportedProjectFile_AcceptsDotNetProjectsOnly(string path, bool expected)
        {
            Assert.That(RunTests.IsSupportedProjectFile(path), Is.EqualTo(expected));
        }

        [Test]
        public void Summarize_TimedOut_ReportsAppliedTimeout()
        {
            var summary = RunTests.Summarize(Result(-1, string.Empty, timedOut: true), false, TimeSpan.FromSeconds(900));

            Assert.That(summary.Success, Is.False);
            Assert.That(summary.ErrorText, Is.EqualTo("Test execution timed out after 900 s."));
            Assert.That(summary.Output, Is.EqualTo("Test execution timed out after 900 s."));
            Assert.That(summary.Total, Is.EqualTo(0));
        }

        [Test]
        public void Summarize_TimedOut_DefaultBudget_ReportsTenMinutes()
        {
            var summary = RunTests.Summarize(Result(-1, string.Empty, timedOut: true), false, TimeSpan.FromMinutes(10));

            Assert.That(summary.ErrorText, Is.EqualTo("Test execution timed out after 600 s."));
        }

        [Test]
        public void Summarize_SuccessfulRun_ReportsStatistics()
        {
            var output = "Passed!  - Failed:     0, Passed:     3, Skipped:     0, Total:     3";

            var summary = RunTests.Summarize(Result(0, output), false, TimeSpan.FromSeconds(600));

            Assert.That(summary.Success, Is.True);
            Assert.That(summary.Total, Is.EqualTo(3));
            Assert.That(summary.Passed, Is.EqualTo(3));
            Assert.That(summary.ErrorText, Is.Null);
            Assert.That(summary.Output, Does.Contain("All 3 tests passed"));
        }

        [Test]
        public void Summarize_FailedRun_KeepsFailureDetailsAndNoExplicitError()
        {
            var output = "Failed!  - Failed:     1, Passed:     2, Skipped:     0, Total:     3\n  [FAIL] MyTests.ShouldWork";

            var summary = RunTests.Summarize(Result(1, output), false, TimeSpan.FromSeconds(600));

            Assert.That(summary.Success, Is.False);
            Assert.That(summary.Failed, Is.EqualTo(1));
            // No explicit error text: the caller derives "Tests failed. Passed: ..." and the details stay in the output.
            Assert.That(summary.ErrorText, Is.Null);
            Assert.That(summary.Output, Does.Contain("[FAIL]"));
        }

        /// <summary>
        /// Reproduces the full E2E run of 2026-11-18: a passed test with "Failed" in its name came first, the old parser
        /// took it for a failure, swallowed the following "Passed" lines and the 6000-char cap cut off the real failure.
        /// </summary>
        [Test]
        public void Summarize_ManyPassedLinesAroundOneFailure_ReportsTheFailedTestAndItsMessage()
        {
            var sb = new StringBuilder();
            sb.Append("  Passed CopyAnswer_Collapsible_SurvivesIdleTransitionAfterFailedSummarize [2 s]\r\n");
            for (int i = 0; i < 200; i++)
                sb.Append("  Passed Ui.Passing_Test_Number_").Append(i).Append(" [1 s]\r\n");
            sb.Append("  Failed Ui.RealFailure_Test [3 s]\r\n");
            sb.Append("  Error Message:\r\n");
            sb.Append("   Locator expected to have class 'active'\r\n");
            sb.Append("  Stack Trace:\r\n");
            sb.Append("     at Ui.RealFailure_Test() in C:\\src\\ChatTests.cs:line 180\r\n");
            for (int i = 200; i < 360; i++)
                sb.Append("  Passed Ui.Passing_Test_Number_").Append(i).Append(" [1 s]\r\n");
            sb.Append("\r\nTest Run Failed.\r\nTotal tests: 361\r\n     Passed: 360\r\n     Failed: 1\r\n");

            var summary = RunTests.Summarize(Result(1, sb.ToString()), false, TimeSpan.FromSeconds(600));

            Assert.That(summary.Success, Is.False);
            Assert.That(summary.Failed, Is.EqualTo(1));
            Assert.That(summary.Output, Does.Contain("Failed Ui.RealFailure_Test"));
            Assert.That(summary.Output, Does.Contain("Locator expected to have class 'active'"));
            Assert.That(summary.Output, Does.Not.Contain("Passing_Test_Number_"));
            Assert.That(summary.Output, Does.Not.Contain("AfterFailedSummarize"));
            Assert.That(summary.Output, Does.Not.Contain("[output truncated"));
        }

        [Test]
        public void Summarize_FullOutputRequested_ReturnsFullLog()
        {
            var output = "Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1";

            var summary = RunTests.Summarize(Result(0, output), true, TimeSpan.FromSeconds(600));

            Assert.That(summary.Output, Does.Contain("===== STDOUT ====="));
        }

        private static RunTests CreateTool()
            => new RunTests(Mock.Of<IVsDependencies>(), Mock.Of<IPathResolver>(), Mock.Of<IFileSystem>());

        [Test]
        public void CompletionMessage_FilteredRun_NamesTheFilter()
        {
            // A filtered total (92) was once reported as if it were the whole project (3446).
            var resp = new RunProjectTestsResponse { Success = true, TotalTests = 92, PassedTests = 92, Filter = "PromptTextTests ChatLogSerializerTests" };

            Assert.That(CreateTool().GetCompletionMessage(resp),
                Is.EqualTo("Tests passed. Total: 92, Passed: 92 (filter: 'PromptTextTests ChatLogSerializerTests')"));
        }

        [Test]
        public void CompletionMessage_UnfilteredRun_HasNoFilterNote()
        {
            var resp = new RunProjectTestsResponse { Success = true, TotalTests = 3, PassedTests = 3 };

            Assert.That(CreateTool().GetCompletionMessage(resp), Is.EqualTo("Tests passed. Total: 3, Passed: 3"));
        }

        [Test]
        public void Response_FilterIsSerializedOnlyWhenSet()
        {
            string filtered = JsonConvert.SerializeObject(new RunProjectTestsResponse { Filter = "A B" });
            string unfiltered = JsonConvert.SerializeObject(new RunProjectTestsResponse());

            Assert.That(filtered, Does.Contain("\"filter\":\"A B\""));
            Assert.That(unfiltered, Does.Not.Contain("\"filter\""));
        }
    }
}
