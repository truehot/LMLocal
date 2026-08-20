using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
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


            bool includeFullOutput = false;
            if (parameters?.TryGetValue("include_full_output", out var fullOutObj) == true && fullOutObj is bool fullOut)
                includeFullOutput = fullOut;

            string solutionDir = _vsDependencies.GetSolutionDirectory();
            if (!_pathResolver.TryResolveFilePath(projectPathParam, solutionDir, out string absoluteProjectPath))
                return ErrorResponse($"Cannot resolve project path: {projectPathParam}");

            if (!_pathResolver.IsPathInsideDirectory(absoluteProjectPath, solutionDir))
                return ErrorResponse($"File '{absoluteProjectPath}' is outside the solution directory.");

            if (!_fileSystem.FileExists(absoluteProjectPath))
                return ErrorResponse($"Project file not found: {absoluteProjectPath}");

            if (!absoluteProjectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                return ErrorResponse($"Specified file is not a .csproj: {absoluteProjectPath}");

            var (Success, Output, Total, Passed, Failed, Skipped) = await RunDotnetTestAsync(absoluteProjectPath, solutionDir, includeFullOutput, cancellationToken).ConfigureAwait(false);

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
            bool includeFullOutput,
            CancellationToken cancellationToken)
        {
            bool isSdk = await IsSdkStyleProjectAsync(projectPath, cancellationToken);
            return isSdk
                ? await RunSdkProjectTestsAsync(projectPath, workingDirectory, includeFullOutput, cancellationToken)
                : await RunLegacyProjectTestsAsync(projectPath, workingDirectory, includeFullOutput, cancellationToken);
        }

        private async Task<(bool Success, string Output, int Total, int Passed, int Failed, int Skipped)> RunSdkProjectTestsAsync(
            string projectPath,
            string workingDirectory,
            bool includeFullOutput,
            CancellationToken cancellationToken)
        {
            string arguments = $"test \"{projectPath}\" --no-restore --verbosity normal";
            return await RunDotNetProcessAsync(arguments, workingDirectory, includeFullOutput, cancellationToken);
        }

        private async Task<(bool Success, string Output, int Total, int Passed, int Failed, int Skipped)> RunLegacyProjectTestsAsync(
            string projectPath,
            string workingDirectory,
            bool includeFullOutput,
            CancellationToken cancellationToken)
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

            string arguments = $"vstest \"{dllPath}\" --logger:console;verbosity=normal";
            return await RunDotNetProcessAsync(arguments, workingDirectory, includeFullOutput, cancellationToken);
        }

        private async Task<(bool Success, string Output, int Total, int Passed, int Failed, int Skipped)> RunDotNetProcessAsync(
            string arguments,
            string workingDirectory,
            bool includeFullOutput,
            CancellationToken cancellationToken)
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
                    var cancellationTokenForWait = linkedCts.Token;

                    try
                    {
                        await Task.Run(
                            () =>
                            {
                                while (!process.WaitForExit(3000))
                                {
                                    cancellationTokenForWait.ThrowIfCancellationRequested();
                                }
                            }
                            , cancellationTokenForWait);
                    }
                    catch (OperationCanceledException)
                    {
                        if (!process.HasExited)
                        {
                            try { process.Kill(); } catch { }
                            process.WaitForExit(3000);
                        }

                        if (cancellationToken.IsCancellationRequested) { 
                            return (false, "Test execution cancelled by user.", 0, 0, 0, 0);
                        }
                        else
                        {
                            return (false, "Test execution timed out (10 minutes).", 0, 0, 0, 0);
                        }
                            
                    }
                }

                string output = await outputTask;
                string error = await errorTask;

                string fullOutput = output + (string.IsNullOrEmpty(error) ? "" : "\n" + error);
                var (total, passed, failed, skipped) = ParseTestStatisticsUniversal(fullOutput);
                bool success = (process.ExitCode == 0) && total > 0 && failed == 0;

                string resultOutput;
                if (includeFullOutput)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("===== STDOUT =====");
                    sb.AppendLine(output);
                    if (!string.IsNullOrEmpty(error))
                    {
                        sb.AppendLine("===== STDERR =====");
                        sb.AppendLine(error);
                    }
                    sb.AppendLine($"===== Exit code: {process.ExitCode} =====");
                    resultOutput = sb.ToString();
                }
                else if (success)
                {
                    resultOutput = $"All {total} tests passed. Passed: {passed}, Skipped: {skipped}.";
                }
                else
                {
                    resultOutput = ExtractFailedDetails(fullOutput);
                    if (string.IsNullOrWhiteSpace(resultOutput))
                        resultOutput = "No detailed failure information captured. Check test logs.";
                }

                if (total == 0 && !includeFullOutput)
                    resultOutput += "\n[WARNING] No tests were executed. Check test adapter and project references.";

                return (success, resultOutput, total, passed, failed, skipped);
            }
        }

        /// <summary>
        /// Extracts from the full output only the blocks related to failed tests (lines with [FAIL] and following details).
        /// </summary>
        private string ExtractFailedDetails(string fullOutput)
        {
            if (string.IsNullOrWhiteSpace(fullOutput))
                return null;

            var lines = fullOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var errorBlocks = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Contains("[FAIL]") || line.Contains("Failed:") || line.Contains("  [FAIL]"))
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(line);
                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        string next = lines[j];
                        if (next.Contains("[FAIL]") || next.Contains("[PASS]") || next.Contains("[SKIP]") ||
                            next.Contains("  [FAIL]") || next.Contains("  [PASS]") || next.Contains("  [SKIP]") ||
                            next.Contains("Passed:") || next.Contains("Failed:") || next.Contains("Skipped:"))
                            break;

                        if (string.IsNullOrWhiteSpace(next))
                        {
                            if (j + 1 < lines.Length && string.IsNullOrWhiteSpace(lines[j + 1]))
                                break;
                            sb.AppendLine();
                            continue;
                        }
                        sb.AppendLine(next);
                    }
                    errorBlocks.Add(sb.ToString().TrimEnd());
                }
            }

            if (errorBlocks.Count == 0)
                return null;

            return string.Join("\n\n", errorBlocks);
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
                    var cancellationTokenForWait = cts.Token;
                    try
                    {
                        await Task.Run(
                            () =>
                            {
                                while (!buildProcess.WaitForExit(3000))
                                {
                                    cancellationTokenForWait.ThrowIfCancellationRequested();
                                }
                            }
                            , cancellationTokenForWait);
                    }
                    catch (OperationCanceledException)
                    {
                        if (!buildProcess.HasExited)
                        {
                            try { buildProcess.Kill(); } catch { }
                            buildProcess.WaitForExit(3000);
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
            var fullOutput = false;
            if (parameters?.TryGetValue("include_full_output", out var fullObj) == true && fullObj is bool full)
                fullOutput = full;
            var msg = $"Running tests for '{proj}'";
            if (fullOutput)
                msg += " (full output enabled)";
            return msg + "... ";
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
            return "Test passed.";
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Runs tests for a .NET project. Returns only summary statistics and failure details (if any). Use 'include_full_output': true to get the complete console log.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        ["project_path"] = new ToolDetails
                        {
                            Type = "string",
                            Description = "Relative or absolute path to the .csproj file to test."
                        },
                        ["include_full_output"] = new ToolDetails
                        {
                            Type = "boolean",
                            Description = "If true, returns the full stdout/stderr log instead of just summary/failures."
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
