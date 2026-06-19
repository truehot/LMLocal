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
                        Assert.IsTrue(response.StartsWith("{"), $"Response should be JSON, but got: {response}");

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
                        Assert.IsTrue(results.Count > 0, "Expected at least one search result for 'LMLocal'.");
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
                        await Task.Delay(2000);

                        // Read first 5 lines of a known file inside the solution
                        response = await client.SendCommandAsync("RunTool|ReadFileLines|LMLocal\\LMLocalPackage.cs|1|5", cts.Token);
                        Assert.IsFalse(string.IsNullOrEmpty(response), "Response should not be empty");
                        Assert.IsTrue(response.StartsWith("{"), $"Response should be JSON, but got: {response}");

                        var obj = JObject.Parse(response);
                        Assert.IsTrue(obj.ContainsKey("file_path"), "Response should contain 'file_path' key");
                        Assert.IsTrue(obj.ContainsKey("lines"), "Response should contain 'lines' key");
                        var lines = obj["lines"] as JArray;
                        Assert.IsNotNull(lines);
                        Assert.IsTrue(lines.Count > 0, "Expected at least one line in the file range.");
                        Assert.IsTrue(lines[0]["line_number"] != null);
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
                        await Task.Delay(2000);

                        response = await client.SendCommandAsync("RunTool|GetSolutionOverview", cts.Token);
                        Assert.IsFalse(string.IsNullOrEmpty(response), "Response should not be empty");
                        Assert.IsTrue(response.StartsWith("{"), $"Response should be JSON, but got: {response}");

                        var obj = JObject.Parse(response);
                        Assert.IsTrue(obj.ContainsKey("solution_name"), "Response should contain 'solution_name' key");
                        Assert.IsTrue(obj.ContainsKey("total_projects"), "Response should contain 'total_projects' key");
                        Assert.IsTrue(obj.ContainsKey("total_files"), "Response should contain 'total_files' key");
                        Assert.IsTrue((int)obj["total_projects"] > 0, "Should have at least one project");
                        Assert.IsTrue((int)obj["total_files"] > 0, "Should have at least one file");
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
                        await Task.Delay(2000);

                        // Search for a file that should exist in the solution
                        response = await client.SendCommandAsync("RunTool|FindFilesByName|Package|.cs", cts.Token);
                        Assert.IsFalse(string.IsNullOrEmpty(response), "Response should not be empty");
                        Assert.IsTrue(response.StartsWith("{"), $"Response should be JSON object, but got: {response}");

                        var obj = JObject.Parse(response);
                        Assert.IsTrue(obj.ContainsKey("results"), "Response should contain 'results' key");
                        var results = obj["results"] as JArray;
                        Assert.IsNotNull(results, "'results' should be an array");
                        Assert.IsTrue(results.Count > 0, "Expected at least one file matching 'Package'");
                        Assert.IsTrue(results[0]["file_path"] != null, "Each result should have 'file_path' key");
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

                        await Task.Delay(2000);

                        response = await client.SendCommandAsync("RunTool|Find_Symbol_References|Dispose", cts.Token);
                        Assert.IsFalse(string.IsNullOrEmpty(response), "Response should not be empty");
                        Assert.IsTrue(response.StartsWith("{"), $"Response should be JSON object, but got: {response}");

                        var obj = JObject.Parse(response);
                        Assert.IsTrue(obj.ContainsKey("symbol_name"), "Response should contain 'symbol_name' key");
                        Assert.IsTrue(obj.ContainsKey("total_references"), "Response should contain 'total_references' key");
                        Assert.IsTrue(obj.ContainsKey("results"), "Response should contain 'results' key");


                        Assert.AreEqual("Dispose", (string)obj["symbol_name"], "Symbol name should match");

                        var results = obj["results"] as JArray;
                        Assert.IsNotNull(results, "'results' should be an array");
                        Assert.IsTrue(results.Count > 0, "Expected at least one reference for 'Dispose'");
                        Assert.IsTrue(results[0]["file_path"] != null, "Each result should have 'file_path' key");
                        Assert.IsTrue(results[0]["matches"] != null, "Each result should have 'matches' key");
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
                        await Task.Delay(2000);

                        // List the root directory of the solution
                        response = await client.SendCommandAsync("RunTool|ListDirectoryContents|.", cts.Token);
                        Assert.IsFalse(string.IsNullOrEmpty(response), "Response should not be empty");
                        Assert.IsTrue(response.StartsWith("{"), $"Response should be JSON object, but got: {response}");

                        var obj = JObject.Parse(response);
                        Assert.IsTrue(obj.ContainsKey("directory"), "Response should contain 'directory' key");
                        Assert.IsTrue(obj.ContainsKey("entries"), "Response should contain 'entries' key");
                        Assert.IsTrue(obj.ContainsKey("has_more_results"), "Response should contain 'has_more_results' key");

                        var entries = obj["entries"] as JArray;
                        Assert.IsNotNull(entries, "'entries' should be an array");
                        Assert.IsTrue(entries.Count > 0, "Expected at least one entry in root directory");
                        Assert.IsTrue(entries[0]["name"] != null, "Each entry should have 'name' key");
                        Assert.IsTrue(entries[0]["path"] != null, "Each entry should have 'path' key");
                        Assert.IsTrue(entries[0]["type"] != null, "Each entry should have 'type' key");
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
    }
}
