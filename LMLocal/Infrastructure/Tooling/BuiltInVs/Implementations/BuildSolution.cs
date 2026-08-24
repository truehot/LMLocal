using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Projects;
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

        /// <summary>
        /// Default cap (seconds) for waiting on a build result. Protects against hung builds / modal dialogs blocking the tool forever.
        /// </summary>
        private const int DefaultBuildTimeoutSeconds = 600;

        /// <summary>
        /// Initial wait (ms) before the first build-output stabilization poll.
        /// </summary>
        private const int BuildOutputInitialDelayMs = 200;

        /// <summary>
        /// Interval (ms) between build-output stabilization polls.
        /// </summary>
        private const int BuildOutputPollDelayMs = 500;

        /// <summary>
        /// Max stabilization polls before giving up and reading what we have.
        /// </summary>
        private const int BuildOutputMaxPollAttempts = 6;

        /// <summary>
        /// How many trailing output lines to show in the fallback error detail.
        /// </summary>
        private const int ErrorDetailTailLineCount = 10;

        /// <summary>
        /// How many trailing lines of the build pane to read (single bounded COM read).
        /// </summary>
        private const int BuildOutputTailLines = 300;

        /// <summary>
        /// Max number of error messages reported. Taken from the tail of the build output (most recent last).
        /// </summary>
        private const int MaxReportedErrors = 25;


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

            string projectName = null;
            if (parameters != null && parameters.TryGetValue("project_name", out var v) && v != null)
                projectName = Convert.ToString(v);

            int timeoutSeconds = DefaultBuildTimeoutSeconds;
            if (parameters != null && parameters.TryGetValue("timeout_seconds", out var t) && t != null)
            {
                if (int.TryParse(Convert.ToString(t), out int parsed) && parsed > 0)
                    timeoutSeconds = parsed;
            }

            Project selectedProject = null;
            string projectUniqueName = null;
            string selectedProjectName = null;
            if (!string.IsNullOrWhiteSpace(projectName))
            {
                var matches = ProjectFinder.FindByName(dte.Solution, projectName);
                if (matches.Count == 0)
                    return ErrorResponse($"Project '{projectName}' not found in the open solution.", solutionName, solutionPath);
                if (matches.Count > 1)
                {
                    var names = string.Join(", ", matches.Select(p => ProjectFinder.SafeName(p, ProjectField.Name)));
                    return ErrorResponse($"Project name '{projectName}' is ambiguous. Matches: {names}", solutionName, solutionPath);
                }

                selectedProject = matches[0];
                projectUniqueName = ProjectFinder.SafeName(selectedProject, ProjectField.UniqueName);
                selectedProjectName = ProjectFinder.SafeName(selectedProject, ProjectField.Name);
                if (string.IsNullOrEmpty(projectUniqueName))
                    return ErrorResponse($"Project '{projectName}' has no buildable UniqueName.", solutionName, solutionPath);
            }

            var tcs = new TaskCompletionSource<bool>();
            BuildEvents buildEvents = dte.Events.BuildEvents;

            void buildDoneHandler(vsBuildScope scope, vsBuildAction action)
            {
                if (action != vsBuildAction.vsBuildActionBuild)
                    return;

                try
                {
                    bool succeeded = dte.Solution.SolutionBuild.LastBuildInfo == 0;
                    tcs.TrySetResult(succeeded);
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
                using (ct.Register(() => tcs.TrySetCanceled()))
                {
                    if (ct.IsCancellationRequested)
                        return ErrorResponse("Build was cancelled.", solutionName, solutionPath);

                    if (projectUniqueName == null)
                    {
                        dte.Solution.SolutionBuild.Build(false);
                    }
                    else
                    {
                        string solutionConfiguration = dte.Solution.SolutionBuild.ActiveConfiguration?.Name;
                        if (string.IsNullOrEmpty(solutionConfiguration))
                            return ErrorResponse("No active solution configuration is available.", solutionName, solutionPath);

                        dte.Solution.SolutionBuild.BuildProject(solutionConfiguration, projectUniqueName, false);
                    }

                    Task<bool> buildTask = tcs.Task;

                    if (timeoutSeconds > 0)
                    {
                        var timeout = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct);
                        var completed = await Task.WhenAny(buildTask, timeout);
                        if (completed != buildTask)
                        {
                            if (ct.IsCancellationRequested)
                                return ErrorResponse("Build was cancelled.", solutionName, solutionPath);
                            return ErrorResponse(
                                $"Build timed out after {timeoutSeconds} second(s). It may still be running in Visual Studio.",
                                solutionName, solutionPath);
                        }
                    }

                    bool buildSucceeded = await buildTask;
                    if (ct.IsCancellationRequested)
                        return ErrorResponse("Build was cancelled.", solutionName, solutionPath);

                    var messages = new List<BuildMessage>();
                    string errorDetail = buildSucceeded ? null : "Build failed. See messages for details.";

                    if (!buildSucceeded)
                    {
                        string tailOutput = await StabilizeBuildOutputAsync(dte, ct);

                        if (!string.IsNullOrEmpty(tailOutput))
                        {
                            string section = TrimToLastBuildSection(tailOutput);
                            messages = await Task.Run(() => ParseBuildOutput(section), ct).ConfigureAwait(false);

                            if (messages.Count > MaxReportedErrors)
                                messages.RemoveRange(MaxReportedErrors, messages.Count - MaxReportedErrors);

                            if (projectUniqueName != null)
                            {
                                foreach (var m in messages)
                                    if (m != null)
                                        m.Project = projectUniqueName;
                            }
                        }

                        if (messages.Count == 0)
                        {
                            if (!string.IsNullOrEmpty(tailOutput))
                            {
                                var lines = tailOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                                int start = Math.Max(0, lines.Length - ErrorDetailTailLineCount);
                                var tail = new string[Math.Min(ErrorDetailTailLineCount, lines.Length)];
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
                        ProjectName = selectedProjectName,
                        ProjectUniqueName = projectUniqueName,
                        Messages = messages,
                        ErrorMessage = errorDetail
                    };
                }
            }
            catch (OperationCanceledException)
            {
                return ErrorResponse("Build was cancelled.", solutionName, solutionPath);
            }
            catch (Exception ex)
            {
                return ErrorResponse($"Build failed: {ex.Message}", solutionName, solutionPath);
            }
            finally
            {
                buildEvents.OnBuildDone -= buildDoneHandler;
            }
        }

        private static readonly Regex _buildErrorRegex = new Regex(
            @"^(?:\d+>)?" +
            @"(?:(?<file>.+?)(?:\((?<line>\d+)(?:,(?<col>\d+))?\))?\s*:\s*)?" +
            @"(?:(?:fatal\s+)?(?<kind>error|warning)\s+)" +
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

        /// <summary>
        /// Keeps only the lines belonging to the most recent build section.
        /// </summary>
        internal static string TrimToLastBuildSection(string output)
        {
            if (string.IsNullOrEmpty(output)) return output;

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            int summaryIndex = -1;
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (lines[i].IndexOf("==========", StringComparison.Ordinal) >= 0 &&
                    lines[i].IndexOf("Build:", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    summaryIndex = i;
                    break;
                }
            }
            int upperExclusive = summaryIndex >= 0 ? summaryIndex : lines.Length;

            int lowerInclusive = 0;
            for (int i = upperExclusive - 1; i >= 0; i--)
            {
                if (lines[i].IndexOf("Build started", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    lowerInclusive = i + 1;
                    break;
                }
            }

            if (lowerInclusive >= upperExclusive) return output;
            return string.Join(Environment.NewLine, lines, lowerInclusive, upperExclusive - lowerInclusive);
        }


        private static readonly string[] BuildPaneContentMarkers = { "Build started", "==========", "Build FAILED", "Build succeeded" };

        private static readonly string[] BuildActivePaneMarkers = { "==========", "Build FAILED", "Build succeeded", "error" };

        private readonly struct BuildPaneEnd
        {
            public readonly bool Found;
            public readonly int Line;
            public readonly int LineLength;

            public BuildPaneEnd(bool found, int line, int lineLength)
            {
                Found = found;
                Line = line;
                LineLength = lineLength;
            }
        }

        private static OutputWindowPane LocateBuildPaneByName(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (dte?.ToolWindows?.OutputWindow == null) return null;

            try
            {
                foreach (OutputWindowPane pane in dte.ToolWindows.OutputWindow.OutputWindowPanes)
                {
                    string name = pane?.Name;
                    if (string.IsNullOrEmpty(name)) continue;

                    if (name.IndexOf("build", StringComparison.OrdinalIgnoreCase) >= 0)
                        return pane;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"BuildSolution: LocateBuildPaneByName failed: {ex.Message}");
            }
            return null;
        }

        private static OutputWindowPane LocateBuildPane(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (dte?.ToolWindows?.OutputWindow == null) return null;

            try
            {
                var byName = LocateBuildPaneByName(dte);
                if (byName != null) return byName;

                foreach (OutputWindowPane pane in dte.ToolWindows.OutputWindow.OutputWindowPanes)
                {
                    if (pane?.TextDocument == null) continue;
                    string text = ReadPaneTail(pane, BuildOutputTailLines);
                    if (text != null && BuildPaneContentMarkers.Any(text.Contains))
                        return pane;
                }

                var active = dte.ToolWindows.OutputWindow.ActivePane;
                string probe = active?.TextDocument != null ? ReadPaneTail(active, BuildOutputTailLines) : null;
                if (probe != null && BuildActivePaneMarkers.Any(probe.Contains))
                    return active;

                return null;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"BuildSolution: LocateBuildPane failed: {ex.Message}");
                return null;
            }
        }

        private static BuildPaneEnd GetPaneEnd(OutputWindowPane pane)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var end = pane?.TextDocument?.EndPoint;
            return end == null
                ? new BuildPaneEnd(false, 0, 0)
                : new BuildPaneEnd(true, end.Line, end.LineLength);
        }

        /// <summary>
        /// Reads at most the last <paramref name="maxLines"/> lines of the pane in a single COM call.
        /// </summary>
        private static string ReadPaneTail(OutputWindowPane pane, int maxLines)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var doc = pane?.TextDocument;
            if (doc == null) return null;
            try
            {
                var end = doc.EndPoint.CreateEditPoint();
                var start = doc.EndPoint.CreateEditPoint();
                start.StartOfLine();
                start.LineUp(maxLines);
                return start.GetText(end);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"BuildSolution: ReadPaneTail failed: {ex.Message}");
                return null;
            }
        }

        private async Task<string> StabilizeBuildOutputAsync(DTE2 dte, CancellationToken ct)
        {
            await Task.Delay(BuildOutputInitialDelayMs, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            var pane = LocateBuildPane(dte);
            if (pane?.TextDocument == null) return null;

            var previous = GetPaneEnd(pane);
            if (!previous.Found) return null;

            for (int attempt = 0; attempt < BuildOutputMaxPollAttempts; attempt++)
            {
                await Task.Delay(BuildOutputPollDelayMs, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

                var current = GetPaneEnd(pane);
                if (!current.Found) continue;
                if (current.Line == previous.Line && current.LineLength == previous.LineLength)
                    return ReadPaneTail(pane, BuildOutputTailLines);
                previous = current;
            }
            return ReadPaneTail(pane, BuildOutputTailLines);
        }

        private static BuildSolutionResponse ErrorResponse(string message, string solutionName = null, string solutionPath = null)
        {
            return new BuildSolutionResponse
            {
                Success = false,
                ErrorMessage = message,
                SolutionName = solutionName,
                SolutionPath = solutionPath
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            string projectName = null;
            if (parameters != null && parameters.TryGetValue("project_name", out var v) && v != null)
                projectName = Convert.ToString(v);
            return string.IsNullOrWhiteSpace(projectName)
                ? "Building solution... "
                : $"Building project '{projectName}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is BuildSolutionResponse resp)
            {
                if (resp.Success)
                    return "Build completed.";

                string firstLine = FirstLineOf(resp.ErrorMessage);
                return string.IsNullOrWhiteSpace(firstLine)
                    ? "Build failed."
                    : $"Build failed. {firstLine}";
            }
            return "Build completed.";
        }

        private static string FirstLineOf(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            int index = text.IndexOfAny(new[] { '\r', '\n' });
            return index < 0 ? text : text.Substring(0, index);
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Builds the currently opened VS solution asynchronously, or a single project when 'project_name' is specified. Use after making code changes to verify they compile. Fails if no solution is open, or a build is already in progress. Returns build status and any compilation errors with file/line/column details.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        ["project_name"] = new ToolDetails
                        {
                            Type = "string",
                            Description = "Name of the project to build instead of the whole solution. Accepts project name, unique name, or project file name."
                        },
                        ["timeout_seconds"] = new ToolDetails
                        {
                            Type = "integer",
                            Description = "Optional maximum time (seconds) to wait for the build before returning a timeout error. Default: 600."
                        }
                    },
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

        [JsonProperty("project_name")]
        public string ProjectName { get; set; }

        [JsonProperty("project_unique_name")]
        public string ProjectUniqueName { get; set; }

        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }

        [JsonProperty("build_messages")]
        public List<BuildMessage> Messages { get; set; } = new List<BuildMessage>();
    }

    public class BuildMessage
    {
        [JsonProperty("project")]
        public string Project { get; set; }

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
