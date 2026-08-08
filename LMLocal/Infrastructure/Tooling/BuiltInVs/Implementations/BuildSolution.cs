using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
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
        public ToolAccessLevel AccessLevel => ToolAccessLevel.Execution;

        public string ToolName => "build_solution";

        public BuildSolution(IVsDependencies vsDependencies)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
        }


        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken ct = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            if (!_vsDependencies.IsSolutionOpen)
                return ErrorResponse("No solution is open.");

            var dte = _vsDependencies.GetDTE();
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
                try
                {
                    if (!ThreadHelper.CheckAccess())
                    {
                        ThreadHelper.JoinableTaskFactory.Run(async () =>
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            bool succeeded = dte.Solution.SolutionBuild.LastBuildInfo == 0;
                            tcs.TrySetResult(succeeded);
                        });
                    }
                    else
                    {
                        bool succeeded = dte.Solution.SolutionBuild.LastBuildInfo == 0;
                        tcs.TrySetResult(succeeded);
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    buildEvents.OnBuildDone -= buildDoneHandler;
                }
            }

            buildEvents.OnBuildDone += buildDoneHandler;

            try
            {
                dte.Solution.SolutionBuild.Build(false);

                using (ct.Register(() => tcs.TrySetCanceled()))
                {
                    bool buildSucceeded = await tcs.Task;
                    if (ct.IsCancellationRequested)
                        return ErrorResponse("Build was cancelled.");

                    var messages = new List<BuildMessage>();
                    string errorDetail = buildSucceeded ? null : "Build failed. See messages for details.";

                    if (!buildSucceeded)
                    {
                        string fullOutput = await StabilizeBuildOutputAsync(dte, ct);

                        if (!string.IsNullOrEmpty(fullOutput))
                        {
                            messages = ParseBuildOutput(fullOutput);
                        }

                        if (messages.Count == 0)
                        {
                            await CollectErrorMessagesAsync(messages, ct);
                        }

                        if (messages.Count == 0)
                        {
                            if (!string.IsNullOrEmpty(fullOutput))
                            {
                                var lines = fullOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                int start = Math.Max(0, lines.Length - 10);
                                var tail = new string[Math.Min(10, lines.Length)];
                                Array.Copy(lines, start, tail, 0, tail.Length);
                                errorDetail = "Build failed. Last output lines:\n" + string.Join(Environment.NewLine, tail);
                            }
                            else
                            {
                                errorDetail = "Build failed. No build output available.";
                            }
                        }
                    }

                    return new BuildSolutionResponse
                    {
                        Success = buildSucceeded,
                        SolutionName = solutionName,
                        SolutionPath = solutionPath,
                        Messages = messages,
                        ErrorMessage = errorDetail
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

        private async Task CollectErrorMessagesAsync(List<BuildMessage> messages, CancellationToken ct)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            var dte = _vsDependencies.GetDTE();
            if (dte?.ToolWindows?.ErrorList == null) return;

            await Task.Delay(300, ct).ConfigureAwait(false);


            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

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

        private static readonly Regex _buildErrorRegex = new Regex(
            @"^(?:\d+>)?" +
            @"(?:(?<file>[^(]+)(?:\((?<line>\d+)(?:,(?<col>\d+))?\))?\s*:\s*)?" +
            @"(?<kind>error|warning)\s+" +
            @"(?:(?<code>[A-Za-z]+\d+)\s*:\s*|:)" +
            @"(?<message>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal static List<BuildMessage> ParseBuildOutput(string output)
        {
            var messages = new List<BuildMessage>();
            if (string.IsNullOrEmpty(output)) return messages;

            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var m = _buildErrorRegex.Match(line);
                if (!m.Success) continue;
                if (!string.Equals(m.Groups["kind"].Value, "error", StringComparison.OrdinalIgnoreCase))
                    continue;

                string message = m.Groups["message"].Value.Trim();
                int bracket = message.LastIndexOf(" [");
                if (bracket >= 0) message = message.Substring(0, bracket).Trim();
                if (message.Length == 0) continue;

                messages.Add(new BuildMessage
                {
                    File = m.Groups["file"].Value.Trim(),
                    Line = int.TryParse(m.Groups["line"].Value, out int l) ? l : 0,
                    Column = int.TryParse(m.Groups["col"].Value, out int c) ? c : 0,
                    Message = message
                });
            }
            return messages;
        }


        private static readonly string[] BuildPaneNames = { "Build", "Build Output" };

        private static readonly string[] BuildPaneContentMarkers = { "Build started", "==========", "Build FAILED", "Build succeeded" };

        private static readonly string[] BuildActivePaneMarkers = { "==========", "Build FAILED", "Build succeeded", "error" };

        private string GetBuildOutputText(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (dte?.ToolWindows?.OutputWindow == null) return null;

            try
            {
                foreach (OutputWindowPane pane in dte.ToolWindows.OutputWindow.OutputWindowPanes)
                {
                    if (pane?.Name == null) continue;
                    string paneName = pane.Name;
                    if (BuildPaneNames.Any(n => paneName.Equals(n, StringComparison.OrdinalIgnoreCase)))
                    { return ReadPane(pane); }
                }

                foreach (OutputWindowPane pane in dte.ToolWindows.OutputWindow.OutputWindowPanes)
                {
                    if (pane?.TextDocument == null) continue;
                    string text = ReadPane(pane);
                    if (text != null && BuildPaneContentMarkers.Any(text.Contains))
                        return text;
                }

                var active = dte.ToolWindows.OutputWindow.ActivePane;
                string probe = active?.TextDocument != null ? ReadPane(active) : null;
                if (probe != null && BuildActivePaneMarkers.Any(probe.Contains))
                    return probe;

                return null;
            }
            catch { return null; }
        }

        private static string ReadPane(OutputWindowPane pane)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var doc = pane.TextDocument;
            return doc?.StartPoint.CreateEditPoint().GetText(doc.EndPoint.CreateEditPoint());
        }


        private async Task<string> StabilizeBuildOutputAsync(DTE2 dte, CancellationToken ct)
        {
            await Task.Delay(200, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            string previous = GetBuildOutputText(dte);
            if (string.IsNullOrEmpty(previous)) return null;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                await Task.Delay(500, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

                string current = GetBuildOutputText(dte);
                if (string.IsNullOrEmpty(current)) continue;
                if (previous.Length == current.Length)
                    return current;
                previous = current;
            }
            return previous;
        }

        private static BuildSolutionResponse ErrorResponse(string message)
        {
            return new BuildSolutionResponse
            {
                Success = false,
                ErrorMessage = message
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters) => "Building solution... ";

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
                Description = "Builds the currently opened Visual Studio solution asynchronously. Use after making code changes to verify they compile. Fails if no solution is open, or a build is already in progress. Returns build status and any compilation errors with file/line/column details. ",
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
