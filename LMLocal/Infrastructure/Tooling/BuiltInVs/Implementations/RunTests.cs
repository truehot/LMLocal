using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Projects;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Testing;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    internal interface IRunTests : IBuiltInTool { }

    /// <summary>
    /// Runs tests for a single .NET project. Always builds/runs in the project's own Debug configuration.
    internal class RunTests : IRunTests
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IFileSystem _fileSystem;

        private static readonly TimeSpan TestRunTimeout = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(5);

        private const int MaxSummaryOutputChars = 6000;
        private const int CompletionErrorMaxChars = 200;

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
            cancellationToken.ThrowIfCancellationRequested();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

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

            bool restore = false;
            if (parameters?.TryGetValue("restore", out var restoreObj) == true && restoreObj is bool restoreVal)
                restore = restoreVal;

            string filter = null;
            if (parameters?.TryGetValue("filter", out var filterObj) == true && filterObj is string filterStr && !string.IsNullOrWhiteSpace(filterStr))
                filter = TestArgumentsBuilder.SanitizeFilter(filterStr.Trim());

            string solutionDir = _vsDependencies.GetSolutionDirectory();
            if (!_pathResolver.TryResolveFilePath(projectPathParam, solutionDir, out string absoluteProjectPath))
                return ErrorResponse($"Cannot resolve project path: {projectPathParam}");

            if (!_pathResolver.IsPathInsideDirectory(absoluteProjectPath, solutionDir))
                return ErrorResponse($"File '{absoluteProjectPath}' is outside the solution directory.");

            if (!_fileSystem.FileExists(absoluteProjectPath))
                return ErrorResponse($"Project file not found: {absoluteProjectPath}");

            if (!absoluteProjectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                return ErrorResponse($"Specified file is not a .csproj: {absoluteProjectPath}");

            bool isSdk = await SdkProjectDetector.IsSdkStyleAsync(_fileSystem, absoluteProjectPath, cancellationToken).ConfigureAwait(false);

            var (Success, Output, Total, Passed, Failed, Skipped) = isSdk
                ? await RunSdkTestsAsync(absoluteProjectPath, solutionDir, filter, includeFullOutput, restore, cancellationToken).ConfigureAwait(false)
                : await RunLegacyTestsAsync(absoluteProjectPath, solutionDir, filter, includeFullOutput, restore, cancellationToken).ConfigureAwait(false);

            string errorMessage = null;
            if (!Success)
            {
                errorMessage = Total == 0
                    ? "No tests were executed or dotnet failed."
                    : $"Tests failed. Passed: {Passed}, Failed: {Failed}, Skipped: {Skipped}.";
            }

            return new RunProjectTestsResponse
            {
                Success = Success,
                TestRunOutput = Output,
                ErrorMessage = errorMessage,
                TotalTests = Total,
                PassedTests = Passed,
                FailedTests = Failed,
                SkippedTests = Skipped
            };
        }

        private async Task<(bool Success, string Output, int Total, int Passed, int Failed, int Skipped)> RunSdkTestsAsync(
            string projectPath,
            string workingDirectory,
            string filter,
            bool includeFullOutput,
            bool restore,
            CancellationToken cancellationToken)
        {
            string arguments = TestArgumentsBuilder.BuildSdkTestArguments(projectPath, filter, restore);
            return await RunAndSummarizeAsync(arguments, workingDirectory, includeFullOutput, cancellationToken).ConfigureAwait(false);
        }

        private async Task<(bool Success, string Output, int Total, int Passed, int Failed, int Skipped)> RunLegacyTestsAsync(
            string projectPath,
            string workingDirectory,
            string filter,
            bool includeFullOutput,
            bool restore,
            CancellationToken cancellationToken)
        {
            string projectDir = Path.GetDirectoryName(projectPath);
            string dllPath = Path.Combine(projectDir, "bin", "Debug", Path.GetFileNameWithoutExtension(projectPath) + ".dll");

            if (!_fileSystem.FileExists(dllPath))
            {
                var buildResult = await DotnetProcessRunner.RunAsync(
                    TestArgumentsBuilder.BuildBuildArguments(projectPath, restore),
                    workingDirectory,
                    BuildTimeout,
                    cancellationToken).ConfigureAwait(false);

                if (buildResult.Cancelled)
                    return (false, "Build cancelled.", 0, 0, 0, 0);
                if (buildResult.TimedOut)
                    return (false, $"Build timed out after {BuildTimeout.TotalMinutes:0} minute(s).", 0, 0, 0, 0);
                if (buildResult.ExitCode != 0)
                {
                    string detail = TestOutputParser.ExtractDiagnosticSummary(buildResult.StdErr)
                        ?? TestOutputParser.ExtractDiagnosticSummary(buildResult.StdOut)
                        ?? (string.IsNullOrWhiteSpace(buildResult.StdErr) ? buildResult.StdOut : buildResult.StdErr);
                    string errorMsg = $"Build failed (exit code {buildResult.ExitCode}).\n{LimitOutput(detail, MaxSummaryOutputChars)}";
                    return (false, errorMsg, 0, 0, 0, 0);
                }

                if (!_fileSystem.FileExists(dllPath))
                    return (false, $"Test DLL not found after build: {dllPath}", 0, 0, 0, 0);
            }

            string arguments = TestArgumentsBuilder.BuildLegacyVstestArguments(dllPath, filter);
            return await RunAndSummarizeAsync(arguments, workingDirectory, includeFullOutput, cancellationToken).ConfigureAwait(false);
        }

        private async Task<(bool Success, string Output, int Total, int Passed, int Failed, int Skipped)> RunAndSummarizeAsync(
            string arguments,
            string workingDirectory,
            bool includeFullOutput,
            CancellationToken cancellationToken)
        {
            var runResult = await DotnetProcessRunner.RunAsync(arguments, workingDirectory, TestRunTimeout, cancellationToken).ConfigureAwait(false);
            return Summarize(runResult, includeFullOutput);
        }

        private (bool Success, string Output, int Total, int Passed, int Failed, int Skipped) Summarize(DotnetProcessResult runResult, bool includeFullOutput)
        {
            if (runResult.Cancelled)
                return (false, "Test execution cancelled by user.", 0, 0, 0, 0);
            if (runResult.TimedOut)
                return (false, $"Test execution timed out ({TestRunTimeout.TotalMinutes:0} minutes).", 0, 0, 0, 0);

            string fullOutput = runResult.StdOut + (string.IsNullOrEmpty(runResult.StdErr) ? "" : "\n" + runResult.StdErr);
            var (total, passed, failed, skipped) = TestOutputParser.ParseStatisticsUniversal(fullOutput);
            bool success = (runResult.ExitCode == 0) && total > 0 && failed == 0;

            string resultOutput;
            if (includeFullOutput)
            {
                var sb = new StringBuilder();
                sb.AppendLine("===== STDOUT =====");
                sb.AppendLine(runResult.StdOut);
                if (!string.IsNullOrEmpty(runResult.StdErr))
                {
                    sb.AppendLine("===== STDERR =====");
                    sb.AppendLine(runResult.StdErr);
                }
                sb.AppendLine($"===== Exit code: {runResult.ExitCode} =====");
                resultOutput = sb.ToString();
            }
            else if (success)
            {
                resultOutput = $"All {total} tests passed. Passed: {passed}, Skipped: {skipped}.";
            }
            else
            {
                resultOutput = TestOutputParser.ExtractFailedDetails(fullOutput);
                if (string.IsNullOrWhiteSpace(resultOutput))
                {
                    resultOutput = TestOutputParser.ExtractDiagnosticSummary(runResult.StdErr)
                        ?? TestOutputParser.ExtractDiagnosticSummary(runResult.StdOut);

                    if (string.IsNullOrWhiteSpace(resultOutput))
                    {
                        resultOutput = !string.IsNullOrWhiteSpace(runResult.StdErr)
                            ? runResult.StdErr
                            : runResult.StdOut;
                    }

                    if (string.IsNullOrWhiteSpace(resultOutput))
                        resultOutput = "No detailed failure information captured. Check test logs.";
                }

                resultOutput = LimitOutput(resultOutput, MaxSummaryOutputChars);
            }

            if (total == 0 && !includeFullOutput)
                resultOutput += "\n[WARNING] No tests were executed. Check test adapter and project references.";

            return (success, resultOutput, total, passed, failed, skipped);
        }


        private static string LimitOutput(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
                return value;

            return value.Substring(0, maxChars) + Environment.NewLine + $"[output truncated to {maxChars} chars]";
        }

        private static string Shorten(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
                return value;

            return value.Substring(0, maxChars) + "...";
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
            var restore = false;
            if (parameters?.TryGetValue("restore", out var restoreObj) == true && restoreObj is bool restoreVal)
                restore = restoreVal;
            var filter = parameters?.TryGetValue("filter", out var f) == true ? f?.ToString() : null;

            var msg = $"Running tests for '{proj}'";
            if (fullOutput)
                msg += " (full output enabled)";
            if (restore)
                msg += " (restore enabled)";
            if (!string.IsNullOrWhiteSpace(filter))
                msg += $", filter: '{filter}'";
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

                string stats;
                if (parts.Count > 0)
                    stats = string.Join(", ", parts);
                else if (resp.Success)
                    stats = "no tests executed";
                else
                    stats = string.IsNullOrWhiteSpace(resp.ErrorMessage)
                        ? "no test statistics available"
                        : Shorten(resp.ErrorMessage, CompletionErrorMaxChars);

                return (resp.Success ? "Tests passed." : "Tests failed.") + " " + stats;
            }
            return "Test passed.";
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Runs tests for a .NET project in Debug configuration and returns summary statistics plus failure details (not the full log). Use 'include_full_output': true to get the complete console log, and 'filter' to run only tests whose name matches.",
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
                        ["filter"] = new ToolDetails
                        {
                            Type = "string",
                            Description = "Optional test name substring. Only tests whose fully qualified name contains it are run (VSTest --filter / --TestCaseFilter)."
                        },
                        ["include_full_output"] = new ToolDetails
                        {
                            Type = "boolean",
                            Description = "Defaults to false. If true, returns the full stdout/stderr log instead of just summary/failures."
                        },
                        ["restore"] = new ToolDetails
                        {
                            Type = "boolean",
                            Description = "Defaults to false (uses --no-restore). If true, lets 'dotnet test'/'dotnet build' run NuGet restore implicitly (omit --no-restore). Use it when project.assets.json is missing or out of date."
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

        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
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
