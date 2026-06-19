using System;
using System.Collections.Generic;
using System.IO;
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
    internal interface IBuildSolution : IBuiltInTool { }

    internal class BuildSolution : IBuildSolution
    {
        private readonly IVsDependencies _vsDependencies;
        private DTE2 _dte;

        public string ToolName => "Build_Solution";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.Execution;

        public BuildSolution(IVsDependencies vsDependencies)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
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

            if (dte.Solution.SolutionBuild.BuildState == vsBuildState.vsBuildStateInProgress)
                return ErrorResponse("Build is already in progress.");

            string solutionPath = dte.Solution.FullName;
            string solutionName = Path.GetFileNameWithoutExtension(solutionPath);

            var tcs = new TaskCompletionSource<bool>();
            BuildEvents buildEvents = dte.Events.BuildEvents;

            void buildDoneHandler(vsBuildScope vsBuildScope, vsBuildAction vsBuildAction)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                bool succeeded = dte.Solution.SolutionBuild.LastBuildInfo == 0;
                tcs.TrySetResult(succeeded);
                buildEvents.OnBuildDone -= buildDoneHandler;
            }

            buildEvents.OnBuildDone += buildDoneHandler;

            try
            {
                dte.Solution.SolutionBuild.Build(false);

                using (cancellationToken.Register(() => tcs.TrySetCanceled()))
                {
                    bool buildSucceeded = await tcs.Task;
                    if (cancellationToken.IsCancellationRequested)
                        return ErrorResponse("Build was cancelled.");

                    var messages = new List<BuildMessage>();
                    await CollectErrorMessagesAsync(messages);

                    return new BuildSolutionResponse
                    {
                        Success = buildSucceeded,
                        SolutionName = solutionName,
                        SolutionPath = solutionPath,
                        Messages = messages,
                        ErrorMessage = buildSucceeded ? null : "Build failed. See messages for details."
                    };
                }
            }
            catch (OperationCanceledException)
            {
                return ErrorResponse("Build was cancelled.");
            }
            catch (Exception ex)
            {
                return ErrorResponse($"Build failed: {ex.Message}");
            }
            finally
            {
                buildEvents.OnBuildDone -= buildDoneHandler;
            }
        }

        private async Task CollectErrorMessagesAsync(List<BuildMessage> messages)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = GetDTE();
            if (dte?.ToolWindows?.ErrorList == null) return;

            await Task.Delay(300);

            ErrorItems errorItems = dte.ToolWindows.ErrorList.ErrorItems;
            int count = errorItems.Count;
            for (int i = 1; i <= count; i++)
            {
                ErrorItem item = errorItems.Item(i);
                string description = item.Description;
                if (string.IsNullOrEmpty(description)) continue;

                messages.Add(new BuildMessage
                {
                    File = item.FileName ?? string.Empty,
                    Line = item.Line,
                    Column = item.Column,
                    Message = description
                });
            }
        }

        private static BuildSolutionResponse ErrorResponse(string message)
        {
            return new BuildSolutionResponse
            {
                Success = false,
                ErrorMessage = message
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters) => "Building solution...";

        public string GetCompletionMessage(object result)
        {
            if (result is BuildSolutionResponse resp)
                return resp.Success
                    ? $"Build of '{resp.SolutionName}' completed successfully."
                    : $"Build of '{resp.SolutionName}' failed.";
            return "Build finished.";
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Builds the currently opened solution in Visual Studio asynchronously. Response fields: success (bool), solution_name (string), solution_path (string), error_message (string or null), build_messages (array of {file (string), line (int), column (int), message (string)}).",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>(),
                    Required = new List<string>()
                }
            };
        }
    }

    public class BuildSolutionResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("solution_name")]
        public string SolutionName { get; set; }

        [JsonProperty("solution_path")]
        public string SolutionPath { get; set; }

        [JsonProperty("error_message")]
        public string ErrorMessage { get; set; }

        [JsonProperty("build_messages")]
        public List<BuildMessage> Messages { get; set; } = new List<BuildMessage>();
    }

    public class BuildMessage
    {
        [JsonProperty("file")]
        public string File { get; set; }

        [JsonProperty("line")]
        public int Line { get; set; }

        [JsonProperty("column")]
        public int Column { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }
}
