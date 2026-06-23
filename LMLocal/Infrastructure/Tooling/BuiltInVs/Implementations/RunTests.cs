using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    internal interface IRunTests : IBuiltInTool { }

    internal class RunTests : IRunTests
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IFileSystem _fileSystem;

        public string ToolName => "run_tests";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.Execution;

        public RunTests(IVsDependencies vsDependencies, IPathResolver pathResolver, IFileSystem fileSystem)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (!_vsDependencies.IsSolutionOpen)
                return ErrorResponse("No solution is open.");

            var dte = _vsDependencies.GetDTE();
            if (dte == null)
                return ErrorResponse("DTE service not available.");

            if (parameters?.TryGetValue("project_path", out var pp) != true || !(pp is string projectPathParam) || string.IsNullOrEmpty(projectPathParam))
                return ErrorResponse("Parameter 'project_path' is required (relative or absolute path to .csproj).");

            string solutionDir = _vsDependencies.GetSolutionDirectory();
            if (!_pathResolver.TryResolveFilePath(projectPathParam, solutionDir, out string absoluteProjectPath))
                return ErrorResponse($"Cannot resolve project path: {projectPathParam}");

            if (!_fileSystem.FileExists(absoluteProjectPath))
                return ErrorResponse($"Project file not found: {absoluteProjectPath}");

            if (!absoluteProjectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                return ErrorResponse($"Specified file is not a .csproj: {absoluteProjectPath}");

            var (Success, Output, Total, Passed, Failed, Skipped) = await RunDotnetTestAsync(absoluteProjectPath, solutionDir, cancellationToken);

            return new RunProjectTestsResponse
            {
                Success = Success,
                TestRunOutput = Output,
                ErrorMessage = Success ? null : $"Tests failed. Passed: {Passed}, Failed: {Failed}, Skipped: {Skipped}.",
                TotalTests = Total,
                PassedTests = Passed,
                FailedTests = Failed,
                SkippedTests = Skipped
            };
        }

        private async Task<bool> IsSdkStyleProjectAsync(string projectPath, CancellationToken cancellationToken)
        {
            try
            {
                var lines = await _fileSystem.ReadLinesRangeAsync(projectPath, 1, 1, cancellationToken);
                string firstLine = lines.Count > 0 ? lines[0] : null;
                return !string.IsNullOrEmpty(firstLine) && firstLine.Contains("Sdk=\"Microsoft.NET.Sdk\"");
            }
            catch
            {
                return false;
            }
        }

        private async Task<(bool Success, string Output, int Total, int Passed, int Failed, int Skipped)> RunDotnetTestAsync(
            string projectPath,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            bool isSdk = await IsSdkStyleProjectAsync(projectPath, cancellationToken);
            return isSdk
                ? await RunSdkProjectTestsAsync(projectPath, workingDirectory, cancellationToken)
                : await RunLegacyProjectTestsAsync(projectPath, workingDirectory, cancellationToken);
        }

        private async Task<(bool Success, string Output, int Total, int Passed, int Failed, int Skipped)> RunSdkProjectTestsAsync(string projectPath, string workingDirectory, CancellationToken cancellationToken)
        {
            string arguments = $"test \"{projectPath}\" --no-build --no-restore --logger \"console;verbosity=detailed\"";
            return await RunDotNetProcessAsync(arguments, workingDirectory, cancellationToken);
        }

        private async Task<(bool Success, string Output, int Total, int Passed, int Failed, int Skipped)> RunLegacyProjectTestsAsync(string projectPath, string workingDirectory, CancellationToken cancellationToken)
        {
            string projectDir = Path.GetDirectoryName(projectPath);
            string dllPath = Path.Combine(projectDir, "bin", "Debug", Path.GetFileNameWithoutExtension(projectPath) + ".dll");

            if (!_fileSystem.FileExists(dllPath))
            {
                var (Success, Output) = await BuildProjectAsync(projectPath, workingDirectory, cancellationToken);
                if (!Success)
                    return (false, Output, 0, 0, 0, 0);

                if (!_fileSystem.FileExists(dllPath))
                    return (false, $"Test DLL not found after build: {dllPath}", 0, 0, 0, 0);
            }

            string arguments = $"vstest \"{dllPath}\" --logger:console;verbosity=detailed";
            return await RunDotNetProcessAsync(arguments, workingDirectory, cancellationToken);
        }

        private async Task<(bool Success, string Output, int Total, int Passed, int Failed, int Skipped)> RunDotNetProcessAsync(string arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = new System.Diagnostics.Process { StartInfo = startInfo })
            {
                process.Start();
                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();

                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                {
                    try
                    {
                        await Task.Run(() => process.WaitForExit(), linkedCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        if (!process.HasExited)
                        {
                            try { process.Kill(); } catch { }
                            process.WaitForExit();
                        }
                        return (false, "Test execution cancelled or timed out.", 0, 0, 0, 0);
                    }
                }

                string output = await outputTask;
                string error = await errorTask;

                var fullOutput = new StringBuilder();
                fullOutput.AppendLine("===== STDOUT =====");
                fullOutput.AppendLine(output);
                if (!string.IsNullOrEmpty(error))
                {
                    fullOutput.AppendLine("===== STDERR =====");
                    fullOutput.AppendLine(error);
                }
                fullOutput.AppendLine($"===== Exit code: {process.ExitCode} =====");

                string fullOutputStr = fullOutput.ToString();

                var (total, passed, failed, skipped) = ParseTestStatisticsUniversal(fullOutputStr);
                bool success = (process.ExitCode == 0) && total > 0 && failed == 0;

                if (total == 0)
                    fullOutputStr += "\n[WARNING] No tests were executed. Check test adapter and project references.";

                return (success, fullOutputStr, total, passed, failed, skipped);
            }
        }

        private async Task<(bool Success, string Output)> BuildProjectAsync(string projectPath, string workingDirectory, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{projectPath}\" --no-restore --configuration Debug",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var buildProcess = new System.Diagnostics.Process { StartInfo = startInfo })
            {
                buildProcess.Start();
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromMinutes(5));
                    try
                    {
                        await Task.Run(() => buildProcess.WaitForExit(), cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        if (!buildProcess.HasExited)
                        {
                            try { buildProcess.Kill(); } catch { }
                            buildProcess.WaitForExit();
                        }
                        return (false, "Build cancelled or timed out.");
                    }
                }
                string buildOutput = await buildProcess.StandardOutput.ReadToEndAsync();
                string buildError = await buildProcess.StandardError.ReadToEndAsync();
                if (buildProcess.ExitCode != 0)
                {
                    string errorMsg = $"Build failed (exit code {buildProcess.ExitCode}).\n[STDOUT]\n{buildOutput}\n[STDERR]\n{buildError}";
                    return (false, errorMsg);
                }
                return (true, "Build succeeded.");
            }
        }

        private (int total, int passed, int failed, int skipped) ParseTestStatisticsUniversal(string output)
        {
            int total = 0, passed = 0, failed = 0, skipped = 0;

            var totalMatch = System.Text.RegularExpressions.Regex.Match(output, @"(?:Total tests:|total:)\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.RightToLeft);
            if (totalMatch.Success) int.TryParse(totalMatch.Groups[1].Value, out total);

            var passedMatch = System.Text.RegularExpressions.Regex.Match(output, @"(?:Passed:|succeeded:)\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.RightToLeft);
            if (passedMatch.Success) int.TryParse(passedMatch.Groups[1].Value, out passed);

            var failedMatch = System.Text.RegularExpressions.Regex.Match(output, @"(?:Failed:|failed:)\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.RightToLeft);
            if (failedMatch.Success) int.TryParse(failedMatch.Groups[1].Value, out failed);

            var skippedMatch = System.Text.RegularExpressions.Regex.Match(output, @"(?:Skipped:|skipped:)\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.RightToLeft);
            if (skippedMatch.Success) int.TryParse(skippedMatch.Groups[1].Value, out skipped);

            if (total == 0 && (passed > 0 || failed > 0 || skipped > 0))
                total = passed + failed + skipped;

            return (total, passed, failed, skipped);
        }

        private static RunProjectTestsResponse ErrorResponse(string message)
        {
            return new RunProjectTestsResponse
            {
                Success = false,
                ErrorMessage = message
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var proj = parameters?.TryGetValue("project_path", out var p) == true ? p?.ToString() : "project";
            return $"Running tests for '{proj}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is RunProjectTestsResponse resp)
            {
                var parts = new List<string>();
                if (resp.TotalTests > 0)
                    parts.Add($"Total: {resp.TotalTests}");
                if (resp.PassedTests > 0)
                    parts.Add($"Passed: {resp.PassedTests}");
                if (resp.FailedTests > 0)
                    parts.Add($"Failed: {resp.FailedTests}");
                if (resp.SkippedTests > 0)
                    parts.Add($"Skipped: {resp.SkippedTests}");

                string stats = parts.Count > 0 ? string.Join(", ", parts) : "No test statistics available.";

                if (resp.Success)
                {
                    return $"Tests passed. {stats}";
                }
                else
                {
                    return $"Tests failed. {stats}";
                }
            }
            return "Test run finished.";
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Runs tests for a .NET project. Does NOT build the test project first for SDK-style projects — call build_solution before this if you made code changes. Has a 10-minute timeout per test run. Returns full console output in test_run_output plus parsed statistics: total_tests, passed_tests, failed_tests, skipped_tests. The success field is true only if the process exits with code 0 AND at least one test was found AND no tests failed. Fails if the .csproj does not exist or is not a valid project file. Example: {\"project_path\":\"tests/MyApp.Tests.csproj\"} → {\"success\":true,\"test_run_output\":\"...stdout...\",\"error_message\":null,\"total_tests\":42,\"passed_tests\":40,\"failed_tests\":0,\"skipped_tests\":2}.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        ["project_path"] = new ToolDetails
                        {
                            Type = "string",
                            Description = "Relative or absolute path to the .csproj file to test."
                        }
                    },
                    Required = new List<string> { "project_path" }
                }
            };
        }
    }

    public class RunProjectTestsResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("test_run_output")]
        public string TestRunOutput { get; set; }

        [JsonProperty("error_message")]
        public string ErrorMessage { get; set; }

        [JsonProperty("total_tests")]
        public int TotalTests { get; set; }

        [JsonProperty("passed_tests")]
        public int PassedTests { get; set; }

        [JsonProperty("failed_tests")]
        public int FailedTests { get; set; }

        [JsonProperty("skipped_tests")]
        public int SkippedTests { get; set; }
    }
}