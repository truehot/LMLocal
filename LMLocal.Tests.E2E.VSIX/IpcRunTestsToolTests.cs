using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace LMLocal.Tests.E2E.VSIX
{
    /// <summary>
    /// build_solution + run_tests through the 'RunTool' IPC pass-through, without a model: the failure summary
    /// run_tests returns and its filter. Runs on small fixture solutions, never on LMLocal.sln (building it inside
    /// the experimental instance would rebuild the extension that runs there).
    /// The pass-through calls the tool provider directly, so EnableAiWriteTools (checked by ToolRouter and
    /// ToolQueueProvider) does not apply here.
    /// </summary>
    [TestClass]
    public class IpcRunTestsToolTests
    {
        private const string PipeName = "LMLocal.Ipc";

        private static readonly TimeSpan ToolTimeout = TimeSpan.FromMinutes(10);

        private const string TestRunProject = "TestRunFixture.Tests\\TestRunFixture.Tests.csproj";
        private const string BuildFixtureTestsProject = "BuildFixture.Tests\\BuildFixture.Tests.csproj";

        /// <summary>
        /// One failure among passing tests, one of which has "Failed" in its name: the summary names the failed test
        /// and its message and holds no passed results (the 2026-11-18 run returned 6000 chars of "Passed" lines and
        /// cut the real failure off).
        /// </summary>
        [TestMethod]
        public async Task RunTests_OneFailure_SummaryHasTheFailedTestAndItsMessageOnlyAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20)))
            {
                await WithFixtureAsync(GetTestRunFixtureSolutionPath(), cts.Token, async client =>
                {
                    var obj = await RunTestsAsync(client, TestRunProject, null, cts.Token);

                    Assert.IsFalse((bool)obj["success"], "a run with a failing test is not a success: " + obj);
                    Assert.AreEqual(3, (int)obj["total_tests"], "total_tests: " + obj);
                    Assert.AreEqual(1, (int)obj["failed_tests"], "failed_tests: " + obj);
                    Assert.AreEqual(2, (int)obj["passed_tests"], "passed_tests: " + obj);

                    string output = (string)obj["test_run_output"] ?? string.Empty;
                    StringAssert.Contains(output, "Failed Fails_WithKnownMessage", "the failed test must be named");
                    StringAssert.Contains(output, "expected-failure-marker", "the failure message must be in the summary");
                    Assert.IsFalse(output.Contains("Retry_AfterFailedSave_Passes"),
                        "a passed test with 'Failed' in its name must not be reported: " + output);
                    Assert.IsFalse(Regex.IsMatch(output, @"^\s*Passed\s", RegexOptions.Multiline),
                        "passed results must not be in the failure summary: " + output);
                });
            }
        }

        /// <summary>
        /// Filters models actually sent: several names separated by spaces or OR, and a VSTest prefix the tool adds
        /// itself. BuildFixture.Tests has CalculatorTests (2 tests) and FormatTests (1 test).
        /// </summary>
        [TestMethod]
        public async Task RunTests_Filter_SeveralNamesAndVsTestPrefixAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20)))
            {
                await WithFixtureAsync(SubAgentBenchmarkTests.GetBuildFixtureSolutionPath(), cts.Token, async client =>
                {
                    await AssertTotalAsync(client, "CalculatorTests", 2, cts.Token);
                    await AssertTotalAsync(client, "CalculatorTests FormatTests", 3, cts.Token);
                    await AssertTotalAsync(client, "CalculatorTests OR FormatTests", 3, cts.Token);
                    await AssertTotalAsync(client, "FullyQualifiedName~CalculatorTests", 2, cts.Token);
                    await AssertTotalAsync(client, "FullyQualifiedName~BuildFixture.Tests.FormatTests", 1, cts.Token);
                });
            }
        }

        private static async Task AssertTotalAsync(IpcClient client, string filter, int expectedTotal, CancellationToken ct)
        {
            var obj = await RunTestsAsync(client, BuildFixtureTestsProject, filter, ct);

            Assert.IsTrue((bool)obj["success"], $"filter '{filter}' should run passing tests: {obj}");
            Assert.AreEqual(expectedTotal, (int)obj["total_tests"], $"filter '{filter}': {obj}");
        }

        /// <summary>
        /// Starts the experimental instance, opens <paramref name="solutionPath"/>, waits for it to load, builds it
        /// with build_solution and runs <paramref name="body"/>.
        /// </summary>
        private static async Task WithFixtureAsync(string solutionPath, CancellationToken ct, Func<IpcClient, Task> body)
        {
            var vs = await VsLauncher.StartExperimentalInstanceAsync(ct);
            try
            {
                using (var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(2), ct))
                {
                    Assert.AreEqual("OK", await client.SendCommandAsync("OpenSolution|" + solutionPath, TimeSpan.FromMinutes(2), ct),
                        "OpenSolution failed for " + Path.GetFileName(solutionPath));

                    await WaitForSolutionLoadedAsync(client, ct);

                    var build = JObject.Parse(await client.SendCommandAsync("RunTool|build_solution", ToolTimeout, ct));
                    Assert.IsTrue((bool)build["success"], "the fixture must build: " + build);

                    await body(client);
                }
            }
            finally
            {
                TryKill(vs);
            }
        }

        private static async Task<JObject> RunTestsAsync(IpcClient client, string projectPath, string filter, CancellationToken ct)
        {
            string command = "RunTool|run_tests|" + Uri.EscapeDataString(projectPath);
            if (filter != null)
                command += "|" + Uri.EscapeDataString(filter);

            string resp = await client.SendCommandAsync(command, ToolTimeout, ct);
            Assert.IsTrue(resp != null && resp.StartsWith("{"), "run_tests should return JSON: " + resp);
            return JObject.Parse(resp);
        }

        private static async Task WaitForSolutionLoadedAsync(IpcClient client, CancellationToken ct)
        {
            for (int i = 0; i < 90; i++)
            {
                var resp = await client.SendCommandAsync("RunTool|GetSolutionOverview", TimeSpan.FromSeconds(20), ct);
                if (!string.IsNullOrWhiteSpace(resp) && resp.StartsWith("{"))
                {
                    try
                    {
                        var obj = JObject.Parse(resp);
                        if (obj["success"]?.Value<bool>() == true && !string.IsNullOrWhiteSpace((string)obj["solution_name"]))
                            return;
                    }
                    catch (Exception)
                    {
                        // Not loaded yet.
                    }
                }
                await Task.Delay(2000, ct);
            }
            throw new TimeoutException("Solution did not load within 180s.");
        }

        private static string GetTestRunFixtureSolutionPath()
        {
            var path = Path.Combine(Path.GetDirectoryName(SubAgentBenchmarkTests.GetSolutionPath()),
                "LMLocal.Tests.E2E.VSIX", "Fixtures", "TestRunFixture", "TestRunFixture.sln");
            if (!File.Exists(path))
                throw new InvalidOperationException("run_tests fixture not found at '" + path + "'");
            return path;
        }

        private static void TryKill(System.Diagnostics.Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                    process.Kill();

                process?.Dispose();
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }
}
