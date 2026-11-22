using EnvDTE;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.Abstractions;
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
    /// Runs tests for a single .NET project in the configuration the active solution configuration maps the project
    /// to - the same configuration <c>build_solution</c> builds.
    /// </summary>
    internal class RunTests : IRunTests, IBuiltInToolAppliedTimeout
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IFileSystem _fileSystem;

        private static readonly TimeSpan TestRunTimeout = TimeSpan.FromMinutes(10);

        /// <summary>
        /// .NET project files that <c>dotnet test</c> / <c>dotnet vstest</c> run the same way regardless of language.
        /// </summary>
        private static readonly string[] SupportedProjectExtensions = { ".csproj", ".vbproj", ".fsproj" };

        private const int MaxSummaryOutputChars = 6000;
        private const int CompletionErrorMaxChars = 200;

        public string ToolName => "run_tests";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.Execution;

        /// <summary>
        /// Default run budget. A caller with less time (SubAgent) passes a smaller value through the budget-aware overload.
        /// </summary>
        public ToolExecutionLimits TimeoutLimits { get; } = new ToolExecutionLimits(TestRunTimeout);

        public RunTests(IVsDependencies vsDependencies, IPathResolver pathResolver, IFileSystem fileSystem)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
            => ExecuteAsync(parameters, TimeoutLimits.DefaultTimeout, cancellationToken);

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, TimeSpan appliedTimeout, CancellationToken cancellationToken)
        {
            int appliedTimeoutSeconds = Math.Max(1, ToolTimeoutPolicy.ToWholeSeconds(appliedTimeout));
            TimeSpan customTimeout = TimeSpan.FromSeconds(appliedTimeoutSeconds);

            cancellationToken.ThrowIfCancellationRequested();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            if (!_vsDependencies.IsSolutionOpen)
                return ErrorResponse("No solution is open.");

            var dte = _vsDependencies.GetDTE();
            if (dte == null)
                return ErrorResponse("DTE service not available.");

            if (parameters?.TryGetValue("project_path", out var pp) != true || !(pp is string projectPathParam) || string.IsNullOrEmpty(projectPathParam))
                return ErrorResponse("Parameter 'project_path' is required (relative or absolute path to .csproj, .vbproj or .fsproj).");

            bool includeFullOutput = false;
            if (parameters?.TryGetValue("include_full_output", out var fullOutObj) == true && fullOutObj is bool fullOut)
                includeFullOutput = fullOut;

            string filter = null;
            if (parameters?.TryGetValue("filter", out var filterObj) == true && filterObj is string filterStr && !string.IsNullOrWhiteSpace(filterStr))
                filter = TestArgumentsBuilder.NormalizeFilter(filterStr.Trim());

            bool fullOutputIgnored = includeFullOutput && string.IsNullOrEmpty(filter);
            if (fullOutputIgnored)
                includeFullOutput = false;

            string solutionDir = _vsDependencies.GetSolutionDirectory();
            if (!_pathResolver.TryResolveFilePath(projectPathParam, solutionDir, out string absoluteProjectPath))
                return ErrorResponse($"Cannot resolve project path: {projectPathParam}");

            if (!_pathResolver.IsPathInsideDirectory(absoluteProjectPath, solutionDir))
                return ErrorResponse($"File '{absoluteProjectPath}' is outside the solution directory.");

            if (!_fileSystem.FileExists(absoluteProjectPath))
                return ErrorResponse($"Project file not found: {absoluteProjectPath}");

            if (!IsSupportedProjectFile(absoluteProjectPath))
                return ErrorResponse($"Specified file is not a .NET project file (.csproj, .vbproj, .fsproj): {absoluteProjectPath}");

            Project buildProject = null;
            try
            {
                buildProject = ProjectFinder.FindByPath(dte.Solution, absoluteProjectPath);
            }
            catch (Exception ex)
            {
                InternalLogger.Debug($"RunTests: cannot find project '{absoluteProjectPath}' in the solution: {ex.Message}");
            }

            var buildConfiguration = ProjectConfigurationReader.ReadActive(dte, buildProject);

            string configurationProblem = buildProject == null
                ? $"project '{absoluteProjectPath}' is not loaded in the open solution, so its configuration cannot be read"
                : buildConfiguration.ResolutionError;

            bool isSdk = await SdkProjectDetector.IsSdkStyleAsync(_fileSystem, absoluteProjectPath, cancellationToken).ConfigureAwait(false);

            string testAssembly;
            string testAssemblyModified = null;
            TestRunSummary summary;

            if (isSdk)
            {
                testAssembly = absoluteProjectPath;
                summary = await RunSdkTestsAsync(absoluteProjectPath, solutionDir, filter, buildConfiguration.ProjectConfigurationName, buildConfiguration.ProjectPlatformName, includeFullOutput, customTimeout, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var assemblyCandidates = TestAssemblyLocator.ResolveAssemblyCandidates(
                    Path.GetDirectoryName(absoluteProjectPath),
                    Path.GetFileName(absoluteProjectPath),
                    buildConfiguration.OutputPath,
                    buildConfiguration.ProjectConfigurationName);

                testAssembly = null;
                foreach (string candidate in assemblyCandidates)
                {
                    if (!_fileSystem.FileExists(candidate))
                        continue;

                    testAssembly = candidate;
                    break;
                }

                if (testAssembly == null)
                    return ErrorResponse(MissingAssemblyMessage(buildConfiguration, configurationProblem, assemblyCandidates), buildConfiguration);

                testAssemblyModified = GetAssemblyWriteTime(testAssembly);
                summary = await RunLegacyTestsAsync(testAssembly, solutionDir, filter, includeFullOutput, customTimeout, cancellationToken).ConfigureAwait(false);
            }

            string output = summary.Output;
            if (!buildConfiguration.IsProjectConfigurationResolved)
            {
                output = "[NOTE] The project configuration could not be determined (" + (configurationProblem ?? "reason unknown") +
                    "); the project's default configuration was used. Build and test the same solution configuration to test the binaries that were just built.\n" + output;
            }

            if (fullOutputIgnored)
                output = "[NOTE] include_full_output was ignored because 'filter' is not set; summary returned. To get the full log, rerun with 'filter' matching the tests to diagnose.\n" + output;

            // An explicit error (own timeout) wins over the generic text: "No tests were executed" would hide the real cause.
            string errorMessage = summary.ErrorText;
            if (!summary.Success && errorMessage == null)
            {
                errorMessage = summary.Total == 0
                    ? "No tests were executed or dotnet failed."
                    : $"Tests failed. Passed: {summary.Passed}, Failed: {summary.Failed}, Skipped: {summary.Skipped}.";
            }

            return new RunProjectTestsResponse
            {
                Success = summary.Success,
                TestRunOutput = output,
                ErrorMessage = errorMessage,
                TimeoutSecondsApplied = appliedTimeoutSeconds,
                Filter = TestArgumentsBuilder.DescribeFilter(filter),
                Configuration = buildConfiguration.ProjectConfigurationName,
                Platform = buildConfiguration.ProjectPlatformName,
                TestAssembly = testAssembly,
                TestAssemblyModified = testAssemblyModified,
                TotalTests = summary.Total,
                PassedTests = summary.Passed,
                FailedTests = summary.Failed,
                SkippedTests = summary.Skipped
            };
        }

        private async Task<TestRunSummary> RunSdkTestsAsync(
            string projectPath,
            string workingDirectory,
            string filter,
            string configuration,
            string platform,
            bool includeFullOutput,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            string arguments = TestArgumentsBuilder.BuildSdkTestArguments(projectPath, filter, configuration, platform);
            return await RunAndSummarizeAsync(arguments, workingDirectory, includeFullOutput, timeout, cancellationToken).ConfigureAwait(false);
        }

        private async Task<TestRunSummary> RunLegacyTestsAsync(
            string assemblyPath,
            string workingDirectory,
            string filter,
            bool includeFullOutput,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            string arguments = TestArgumentsBuilder.BuildLegacyVstestArguments(assemblyPath, filter);
            return await RunAndSummarizeAsync(arguments, workingDirectory, includeFullOutput, timeout, cancellationToken).ConfigureAwait(false);
        }

        private async Task<TestRunSummary> RunAndSummarizeAsync(
            string arguments,
            string workingDirectory,
            bool includeFullOutput,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            TimeSpan effectiveTimeout = timeout ?? TestRunTimeout;
            var runResult = await DotnetProcessRunner.RunAsync(arguments, workingDirectory, effectiveTimeout, cancellationToken).ConfigureAwait(false);

            if (runResult.Cancelled)
            {
                throw new OperationCanceledException("Test execution was cancelled before completion.", cancellationToken);
            }

            return Summarize(runResult, includeFullOutput, effectiveTimeout);
        }

        internal static TestRunSummary Summarize(DotnetProcessResult runResult, bool includeFullOutput, TimeSpan effectiveTimeout)
        {
            if (runResult.TimedOut)
            {
                int seconds = (int)Math.Round(effectiveTimeout.TotalSeconds, MidpointRounding.AwayFromZero);
                string timeoutText = $"Test execution timed out after {seconds} s.";
                return TestRunSummary.FailureWithError(timeoutText, timeoutText);
            }

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
                resultOutput = TestOutputParser.ExtractFailedDetails(fullOutput, MaxSummaryOutputChars);
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

            return new TestRunSummary(success, resultOutput, total, passed, failed, skipped, null);
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

        /// <summary>
        /// True for a C#, VB or F# project file.
        /// </summary>
        internal static bool IsSupportedProjectFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            foreach (string extension in SupportedProjectExtensions)
            {
                if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Failure detail for a missing build output: names the configuration that was expected and every path that was
        /// checked, so the model runs build_solution instead of testing binaries that do not exist.
        /// </summary>
        private static string MissingAssemblyMessage(ProjectBuildConfiguration configuration, string configurationProblem, List<string> candidates)
        {
            var sb = new StringBuilder();
            sb.Append("Test assembly not found. run_tests does not build: run build_solution first.");

            if (!string.IsNullOrEmpty(configuration.ProjectConfigurationName))
            {
                sb.Append($" Expected project configuration: {configuration.ProjectConfigurationName}");
                if (!string.IsNullOrEmpty(configuration.ProjectPlatformName))
                    sb.Append($", platform: {configuration.ProjectPlatformName}");
                sb.Append('.');
            }

            if (candidates != null && candidates.Count > 0)
                sb.Append("\nLooked for: " + string.Join(", ", candidates));
            else
                sb.Append("\nBuild output directory is unknown: " +
                          (configurationProblem ?? "the project configuration has no 'OutputPath' property") + ".");

            return sb.ToString();
        }

        /// <summary>
        /// Write time of the assembly that is about to be run: a stale build is visible in the response instead of being
        /// silently re-tested.
        /// </summary>
        private string GetAssemblyWriteTime(string assemblyPath)
        {
            try
            {
                var (Length, LastWriteTimeUtc) = _fileSystem.GetFileInfo(assemblyPath);
                return LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
            }
            catch (Exception ex)
            {
                InternalLogger.Debug($"RunTests: cannot read the write time of '{assemblyPath}': {ex.Message}");
                return null;
            }
        }

        private static RunProjectTestsResponse ErrorResponse(string message, ProjectBuildConfiguration configuration = null)
        {
            return new RunProjectTestsResponse
            {
                Success = false,
                ErrorMessage = message,
                Configuration = configuration?.ProjectConfigurationName,
                Platform = configuration?.ProjectPlatformName
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var proj = parameters?.TryGetValue("project_path", out var p) == true ? p?.ToString() : "project";
            var fullOutput = false;
            if (parameters?.TryGetValue("include_full_output", out var fullObj) == true && fullObj is bool full)
                fullOutput = full;
            var filter = parameters?.TryGetValue("filter", out var f) == true ? f?.ToString() : null;

            var msg = $"Running tests for '{proj}'";
            if (fullOutput && !string.IsNullOrWhiteSpace(filter))
                msg += " (full output enabled)";
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

                if (!string.IsNullOrEmpty(resp.Filter))
                    stats += $" (filter: '{resp.Filter}')";

                return (resp.Success ? "Tests passed." : "Tests failed.") + " " + stats;
            }
            return "Test passed.";
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Runs the project's pre-built test assembly in the configuration the active solution configuration maps the project to - the same configuration build_solution builds - and returns summary statistics plus failure details (not the full log). It does not build the project; run build_solution first when you change code, otherwise the existing build is tested. Use 'filter' to run only tests whose name matches; together with 'filter', 'include_full_output': true returns the complete console log. The response's 'filter' is the filter that was applied; when it is absent, the whole project ran.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        ["project_path"] = new ToolDetails
                        {
                            Type = "string",
                            Description = "Relative or absolute path to the test project file (.csproj, .vbproj or .fsproj)."
                        },
                        ["filter"] = new ToolDetails
                        {
                            Type = "string",
                            Description = "Optional plain substring matched against the test's fully qualified name, such as 'ClearChat' or 'PersistenceOrderingProbeTests'. Several substrings separated by spaces run the tests matching any of them: 'ClearChatTests RegenerateTests'. Do not use VSTest filter syntax such as Category=, AND, or wildcards inside a name."
                        },
                        ["include_full_output"] = new ToolDetails
                        {
                            Type = "boolean",
                            Description = "Defaults to false. Works only together with 'filter': if true, returns the full stdout/stderr log of the filtered run instead of just summary/failures. Without 'filter' it is ignored and the summary is returned."
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

        [JsonProperty("timeout_seconds_applied", NullValueHandling = NullValueHandling.Ignore)]
        public int? TimeoutSecondsApplied { get; set; }

        /// <summary>
        /// Filter the run actually used (names separated by spaces); absent for a full unfiltered run, so a filtered
        /// total is never mistaken for the whole project.
        /// </summary>
        [JsonProperty("filter", NullValueHandling = NullValueHandling.Ignore)]
        public string Filter { get; set; }

        /// <summary>
        /// Project configuration the run used (<c>Debug</c>, <c>Release</c>) - the one the active solution configuration
        /// maps the project to.
        /// </summary>
        [JsonProperty("configuration", NullValueHandling = NullValueHandling.Ignore)]
        public string Configuration { get; set; }

        /// <summary>
        /// Platform of the project configuration (<c>Any CPU</c>, <c>x64</c>).
        /// </summary>
        [JsonProperty("platform", NullValueHandling = NullValueHandling.Ignore)]
        public string Platform { get; set; }

        /// <summary>
        /// What was actually run: the assembly for a legacy project, the project file for an SDK one.
        /// </summary>
        [JsonProperty("test_assembly", NullValueHandling = NullValueHandling.Ignore)]
        public string TestAssembly { get; set; }

        /// <summary>
        /// Write time (UTC) of the assembly that was run, so a stale build is visible instead of silently re-tested.
        /// </summary>
        [JsonProperty("test_assembly_modified", NullValueHandling = NullValueHandling.Ignore)]
        public string TestAssemblyModified { get; set; }

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
