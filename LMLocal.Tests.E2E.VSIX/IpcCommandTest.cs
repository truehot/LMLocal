using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace LMLocal.Tests.E2E.VSIX
{
    //There tests might be a bit flaky due to the nature of launching VS and IPC, but we will try to make them as robust as possible with retries and timeouts.
    [TestClass]
    public class IpcCommandTest
    {
        private const string PipeName = "LMLocal.Ipc";

        [TestMethod]
        public async Task Ping_WorksAsync()
        {

            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                System.Diagnostics.Process vsProcess = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);

                try
                {
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        string response = await client.SendCommandAsync("Ping", cts.Token);
                        Assert.AreEqual("Pong", response);
                    }
                }
                finally
                {
                    TryKill(vsProcess);
                }
            }
        }

        [TestMethod]
        public async Task RunTool_GetActiveDocument_ReturnsJsonAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                System.Diagnostics.Process vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var solutionPath = GetSolutionPath();
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        string response = await client.SendCommandAsync($"OpenSolution|{solutionPath}", cts.Token);
                        Assert.AreEqual("OK", response);

                        response = await client.SendCommandAsync("RunTool|GetActiveDocument", cts.Token);
                        Assert.IsFalse(string.IsNullOrEmpty(response), "Response should not be empty");
                        Assert.StartsWith("{", response, $"Response should be JSON, but got: {response}");

                        var obj = JObject.Parse(response);
                        Assert.IsTrue(obj.ContainsKey("file_path"), "Response should contain 'file_path' key");
                        Assert.IsTrue(obj.ContainsKey("content"), "Response should contain 'content' key");
                    }
                }
                finally
                {
                    TryKill(vs);
                }
            }
        }

        [TestMethod]
        public async Task RunTool_SearchInFiles_FindsMatchesAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                System.Diagnostics.Process vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var solutionPath = GetSolutionPath();
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        string response = await client.SendCommandAsync($"OpenSolution|{solutionPath}", cts.Token);
                        Assert.AreEqual("OK", response);

                        // search for project root name which should exist in solution files
                        response = await client.SendCommandAsync("RunTool|SearchInFiles|LMLocal", cts.Token);
                        var obj = JObject.Parse(response);
                        Assert.IsTrue(obj.ContainsKey("results"), "Response should contain 'results' key");
                        var results = obj["results"] as JArray;
                        Assert.IsNotNull(results, "'results' should be an array");
                        Assert.IsGreaterThan(0, results.Count, "Expected at least one search result for 'LMLocal'.");
                    }
                }
                finally
                {
                    TryKill(vs);
                }
            }
        }

        [TestMethod]
        public async Task RunTool_ReadFileLines_ReturnsLinesAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                System.Diagnostics.Process vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var solutionPath = GetSolutionPath();
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        string response = await client.SendCommandAsync($"OpenSolution|{solutionPath}", cts.Token);
                        Assert.AreEqual("OK", response);

                        // Wait a bit for solution to fully load
                        await Task.Delay(2000, TestContext.CancellationToken);

                        // Read first 5 lines of a known file inside the solution
                        response = await client.SendCommandAsync("RunTool|ReadFileLines|LMLocal\\LMLocalPackage.cs|1|5", cts.Token);
                        Assert.IsFalse(string.IsNullOrEmpty(response), "Response should not be empty");
                        Assert.StartsWith("{", response, $"Response should be JSON, but got: {response}");

                        var obj = JObject.Parse(response);
                        Assert.IsTrue(obj.ContainsKey("file_path"), "Response should contain 'file_path' key");
                        Assert.IsTrue(obj.ContainsKey("text"), "Response should contain 'text' key");
                        Assert.IsTrue(obj.ContainsKey("start_line"), "Response should contain 'start_line' key");
                        Assert.IsTrue(obj.ContainsKey("end_line"), "Response should contain 'end_line' key");
                        Assert.IsTrue(obj.ContainsKey("has_more"), "Response should contain 'has_more' key");
                        Assert.IsTrue(obj.ContainsKey("success"), "Response should contain 'success' key");
                        var text = obj["text"]?.ToString();
                        Assert.IsFalse(string.IsNullOrEmpty(text), "Expected non-empty text content");
                        Assert.AreEqual(1, (int)obj["start_line"], "start_line should be 1");
                        Assert.IsGreaterThanOrEqualTo(1, (int)obj["end_line"], "end_line should be >= 1");
                    }
                }
                finally
                {
                    TryKill(vs);
                }
            }
        }

        [TestMethod]
        public async Task RunTool_GetSolutionOverview_ReturnsMetadataAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                System.Diagnostics.Process vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var solutionPath = GetSolutionPath();
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        string response = await client.SendCommandAsync($"OpenSolution|{solutionPath}", cts.Token);
                        Assert.AreEqual("OK", response);

                        // Wait a bit for solution to fully load
                        await Task.Delay(2000, TestContext.CancellationToken);

                        response = await client.SendCommandAsync("RunTool|GetSolutionOverview", cts.Token);
                        Assert.IsFalse(string.IsNullOrEmpty(response), "Response should not be empty");
                        Assert.StartsWith("{", response, $"Response should be JSON, but got: {response}");

                        var obj = JObject.Parse(response);
                        Assert.IsTrue(obj.ContainsKey("solution_name"), "Response should contain 'solution_name' key");
                        Assert.IsTrue(obj.ContainsKey("total_projects"), "Response should contain 'total_projects' key");
                        Assert.IsTrue(obj.ContainsKey("total_files"), "Response should contain 'total_files' key");
                        Assert.IsGreaterThan(0, (int)obj["total_projects"], "Should have at least one project");
                        Assert.IsGreaterThan(0, (int)obj["total_files"], "Should have at least one file");
                    }
                }
                finally
                {
                    TryKill(vs);
                }
            }
        }

        [TestMethod]
        public async Task RunTool_FindFilesByName_ReturnsMatchesAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                System.Diagnostics.Process vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var solutionPath = GetSolutionPath();
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        string response = await client.SendCommandAsync($"OpenSolution|{solutionPath}", cts.Token);
                        Assert.AreEqual("OK", response);

                        // Wait a bit for solution to fully load
                        await Task.Delay(2000, TestContext.CancellationToken);

                        // Search for a file that should exist in the solution
                        response = await client.SendCommandAsync("RunTool|FindFilesByName|Package|.cs", cts.Token);
                        Assert.IsFalse(string.IsNullOrEmpty(response), "Response should not be empty");
                        Assert.StartsWith("{", response, $"Response should be JSON object, but got: {response}");

                        var obj = JObject.Parse(response);
                        Assert.IsTrue(obj.ContainsKey("results"), "Response should contain 'results' key");
                        var results = obj["results"] as JArray;
                        Assert.IsNotNull(results, "'results' should be an array");
                        Assert.IsGreaterThan(0, results.Count, "Expected at least one file matching 'Package'");
                        Assert.IsNotNull(results[0]["file_path"], "Each result should have 'file_path' key");
                    }
                }
                finally
                {
                    TryKill(vs);
                }
            }
        }

        [TestMethod]
        public async Task RunTool_FindSymbolReferences_ReturnsMatchesAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                System.Diagnostics.Process vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var solutionPath = GetSolutionPath();
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        string response = await client.SendCommandAsync($"OpenSolution|{solutionPath}", cts.Token);
                        Assert.AreEqual("OK", response);

                        await Task.Delay(2000, TestContext.CancellationToken);

                        response = await client.SendCommandAsync("RunTool|find_symbol_references|Dispose", cts.Token);
                        Assert.IsFalse(string.IsNullOrEmpty(response), "Response should not be empty");
                        Assert.StartsWith("{", response, $"Response should be JSON object, but got: {response}");

                        var obj = JObject.Parse(response);
                        Assert.IsTrue(obj.ContainsKey("symbol_name"), "Response should contain 'symbol_name' key");
                        Assert.IsTrue(obj.ContainsKey("total_references"), "Response should contain 'total_references' key");
                        Assert.IsTrue(obj.ContainsKey("results"), "Response should contain 'results' key");


                        Assert.AreEqual("Dispose", (string)obj["symbol_name"], "Symbol name should match");

                        var results = obj["results"] as JArray;
                        Assert.IsNotNull(results, "'results' should be an array");
                        Assert.IsGreaterThan(0, results.Count, "Expected at least one reference for 'Dispose'");
                        Assert.IsNotNull(results[0]["file_path"], "Each result should have 'file_path' key");
                        Assert.IsNotNull(results[0]["matches"], "Each result should have 'matches' key");
                    }
                }
                finally
                {
                    TryKill(vs);
                }
            }
        }

        [TestMethod]
        public async Task RunTool_ListDirectoryContents_ReturnsDirEntriesAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                System.Diagnostics.Process vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var solutionPath = GetSolutionPath();
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        string response = await client.SendCommandAsync($"OpenSolution|{solutionPath}", cts.Token);
                        Assert.AreEqual("OK", response);

                        // Wait a bit for solution to fully load
                        await Task.Delay(2000, TestContext.CancellationToken);

                        // List the root directory of the solution
                        response = await client.SendCommandAsync("RunTool|ListDirectoryContents|.", cts.Token);
                        Assert.IsFalse(string.IsNullOrEmpty(response), "Response should not be empty");
                        Assert.StartsWith("{", response, $"Response should be JSON object, but got: {response}");

                        var obj = JObject.Parse(response);
                        Assert.IsTrue(obj.ContainsKey("directory"), "Response should contain 'directory' key");
                        Assert.IsTrue(obj.ContainsKey("entries"), "Response should contain 'entries' key");
                        Assert.IsTrue(obj.ContainsKey("has_more_results"), "Response should contain 'has_more_results' key");

                        var entries = obj["entries"] as JArray;
                        Assert.IsNotNull(entries, "'entries' should be an array");
                        Assert.IsGreaterThan(0, entries.Count, "Expected at least one entry in root directory");
                        Assert.IsNotNull(entries[0]["name"], "Each entry should have 'name' key");
                        Assert.IsNotNull(entries[0]["path"], "Each entry should have 'path' key");
                        Assert.IsNotNull(entries[0]["type"], "Each entry should have 'type' key");
                    }
                }
                finally
                {
                    TryKill(vs);
                }
            }
        }

        private string GetSolutionPath()
        {
            var solutionPath = Path.GetFullPath(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "LMLocal.sln")
            );

            if (!System.IO.File.Exists(solutionPath))
                throw new InvalidOperationException($"Test solution not found at '{solutionPath}'");
            return solutionPath;
        }

        private static void TryKill(System.Diagnostics.Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                    process.Kill();

                process.Dispose();
            }
            catch
            {
                throw;
            }
        }

        public TestContext TestContext { get; set; }
    }
}
