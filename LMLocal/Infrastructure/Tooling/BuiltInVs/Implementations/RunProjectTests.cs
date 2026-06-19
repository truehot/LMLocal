using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    internal interface IRunProjectTests : IBuiltInTool { }

    internal class RunProjectTests : IRunProjectTests
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private DTE2 _dte;

        public string ToolName => "Run_Project_Tests";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.Execution;

        public RunProjectTests(IVsDependencies vsDependencies, IPathResolver pathResolver)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        }

        private DTE2 GetDTE()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_dte == null)
                _dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
            return _dte;
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (!_vsDependencies.IsSolutionOpen)
                return ErrorResponse("No solution is open.");

            var dte = GetDTE();
            if (dte == null)
                return ErrorResponse("DTE service not available.");

            if (parameters?.TryGetValue("project_path", out var pp) != true || !(pp is string projectPathParam) || string.IsNullOrEmpty(projectPathParam))
                return ErrorResponse("Parameter 'project_path' is required (relative or absolute path to .csproj).");

            string solutionDir = _vsDependencies.GetSolutionDirectory();
            if (!_pathResolver.TryResolveFilePath(projectPathParam, solutionDir, out string absoluteProjectPath))
                return ErrorResponse($"Cannot resolve project path: {projectPathParam}");

            if (!File.Exists(absoluteProjectPath))
                return ErrorResponse($"Project file not found: {absoluteProjectPath}");

            if (!absoluteProjectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                return ErrorResponse($"Specified file is not a .csproj: {absoluteProjectPath}");

            var (Success, Output) = await RunDotnetTestAsync(absoluteProjectPath, solutionDir, cancellationToken);

            return new RunProjectTestsResponse
            {
                Success = Success,
                TestRunOutput = Output,
                ErrorMessage = Success ? null : "Tests failed. See output for details."
            };
        }

        private async Task<(bool Success, string Output)> RunDotnetTestAsync(string projectPath, string workingDirectory, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"test \"{projectPath}\" --logger \"console;verbosity=normal\"",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = new System.Diagnostics.Process { StartInfo = startInfo })
            {
                var output = new StringBuilder();
                var error = new StringBuilder();

                process.OutputDataReceived += (sender, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (sender, e) => { if (e.Data != null) error.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

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
                        return (false, $"Test execution cancelled or timed out after 10 minutes.\nCaptured output:\n{output}");
                    }
                }

                bool success = process.ExitCode == 0;
                string fullOutput = output.ToString();
                if (error.Length > 0)
                    fullOutput += Environment.NewLine + "ERRORS:" + Environment.NewLine + error.ToString();

                return (success, fullOutput);
            }
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
            return $"Running tests for '{proj}'...";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is RunProjectTestsResponse resp)
                return resp.Success ? "Tests passed." : "Tests failed. Check output.";
            return "Test run finished.";
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Runs tests for a specific project using 'dotnet test'. Requires 'project_path' parameter (relative to solution root or absolute). Returns success status and full console output.Response fields: success (bool), error_message (string or null), test_run_output (string).",
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
    }
}
