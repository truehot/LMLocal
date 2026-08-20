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
        /// Grace period (ms) after BuildDone before reading the Error List, letting it settle.
        /// </summary>
        private const int ErrorListSettleDelayMs = 300;

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
        /// Conventional fallback configuration name when none can be resolved.
        /// </summary>
        private const string FallbackConfigurationName = "Debug";

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
                var matches = FindProjects(dte.Solution, projectName);
                if (matches.Count == 0)
                    return ErrorResponse($"Project '{projectName}' not found in the open solution.", solutionName, solutionPath);
                if (matches.Count > 1)
                {
                    var names = string.Join(", ", matches.Select(p => SafeProjectName(p, ProjectField.Name)));
                    return ErrorResponse($"Project name '{projectName}' is ambiguous. Matches: {names}", solutionName, solutionPath);
                }

                selectedProject = matches[0];
                projectUniqueName = SafeProjectName(selectedProject, ProjectField.UniqueName);
                selectedProjectName = SafeProjectName(selectedProject, ProjectField.Name);
                if (string.IsNullOrEmpty(projectUniqueName))
                    return ErrorResponse($"Project '{projectName}' has no buildable UniqueName.", solutionName, solutionPath);
            }

            int initialErrorCount = 0;
            HashSet<string> preBuildErrorKeys = null;
            try
            {
                if (dte?.ToolWindows?.ErrorList?.ErrorItems != null)
                {
                    initialErrorCount = dte.ToolWindows.ErrorList.ErrorItems.Count;
                    preBuildErrorKeys = SnapshotErrorKeys(dte);
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"BuildSolution: Error List snapshot failed: {ex.Message}");
            }

            OutputWindowPane buildPane = null;
            EditPoint buildStartPoint = null;
            try
            {
                buildPane = LocateBuildPaneByName(dte);
                buildStartPoint = buildPane?.TextDocument?.EndPoint?.CreateEditPoint();
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"BuildSolution: Could not capture build pane start: {ex.Message}");
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
                        string configuration = ResolveBuildConfigurationName(dte);
                        dte.Solution.SolutionBuild.BuildProject(configuration, projectUniqueName, false);
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
                        string fullOutput = await StabilizeBuildOutputAsync(dte, ct, buildPane, buildStartPoint);

                        if (!string.IsNullOrEmpty(fullOutput))
                        {
                            messages = await Task.Run(() => ParseBuildOutput(fullOutput), ct).ConfigureAwait(false);

                            if (projectUniqueName != null)
                            {
                                foreach (var m in messages)
                                    if (m != null)
                                        m.Project = projectUniqueName;
                            }
                        }

                        await CollectErrorMessagesAsync(messages, ct, initialErrorCount, preBuildErrorKeys, projectUniqueName);

                        if (messages.Count == 0)
                        {
                            if (!string.IsNullOrEmpty(fullOutput))
                            {
                                var lines = fullOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
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

        internal static string NormalizeProjectName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return name.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');
        }

        internal static bool IsProjectNameMatch(string candidateName, string candidateUniqueName, string candidateFullName, string searchName)
        {
            string normalizedSearch = NormalizeProjectName(searchName);
            if (IsNameMatch(candidateName, normalizedSearch)) return true;
            if (IsNameMatch(candidateUniqueName, normalizedSearch)) return true;
            if (!string.IsNullOrEmpty(candidateFullName) &&
                IsNameMatch(Path.GetFileName(candidateFullName), normalizedSearch))
                return true;
            return false;
        }

        private static bool IsNameMatch(string candidate, string normalizedSearch)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(normalizedSearch)) return false;
            return string.Equals(NormalizeProjectName(candidate), normalizedSearch, StringComparison.OrdinalIgnoreCase);
        }

        private static List<Project> FindProjects(Solution solution, string searchName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var results = new List<Project>();
            if (solution == null || string.IsNullOrWhiteSpace(searchName)) return results;

            foreach (Project p in solution.Projects)
            {
                if (p == null) continue;
                CollectProjectMatches(p, searchName, results);
            }
            return results;
        }

        private const string SolutionFolderKind = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}";

        private static void CollectProjectMatches(Project project, string searchName, List<Project> results)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (project == null) return;

            string kind = SafeProjectName(project, ProjectField.Kind);
            if (string.Equals(kind, SolutionFolderKind, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    foreach (ProjectItem item in project.ProjectItems)
                    {
                        if (item?.SubProject == null) continue;
                        CollectProjectMatches(item.SubProject, searchName, results);
                    }
                }
                catch (Exception ex)
                {
                    InternalLogger.Debug($"BuildSolution: Could not enumerate solution folder contents: {ex.Message}");
                }
                return;
            }

            if (IsProjectNameMatch(
                    SafeProjectName(project, ProjectField.Name),
                    SafeProjectName(project, ProjectField.UniqueName),
                    SafeProjectName(project, ProjectField.FullName),
                    searchName))
            {
                results.Add(project);
            }
        }

        private enum ProjectField
        {
            Name,
            UniqueName,
            FullName,
            Kind
        }

        private static string SafeProjectName(Project project, ProjectField field)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                switch (field)
                {
                    case ProjectField.Name: return project.Name;
                    case ProjectField.UniqueName: return project.UniqueName;
                    case ProjectField.FullName: return project.FullName;
                    case ProjectField.Kind: return project.Kind;
                    default: return null;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Debug($"BuildSolution: Could not read project field {field}: {ex.Message}");
                return null;
            }
        }

        private static string GetActiveConfigurationName(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var config = dte?.Solution?.SolutionBuild?.ActiveConfiguration;
                return string.IsNullOrEmpty(config?.Name) ? string.Empty : config.Name;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"BuildSolution: Could not read active configuration: {ex.Message}");
                return string.Empty;
            }
        }

        private static string ResolveBuildConfigurationName(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string active = GetActiveConfigurationName(dte);
            if (!string.IsNullOrEmpty(active)) return active;

            try
            {
                var configs = dte?.Solution?.SolutionBuild?.SolutionConfigurations;
                if (configs != null)
                {
                    for (int i = 1; i <= configs.Count; i++)
                    {
                        var config = configs.Item(i);
                        if (!string.IsNullOrEmpty(config?.Name))
                            return config.Name;
                    }
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"BuildSolution: Could not enumerate solution configurations: {ex.Message}");
            }
            return FallbackConfigurationName;
        }

        private async Task CollectErrorMessagesAsync(
            List<BuildMessage> messages,
            CancellationToken ct,
            int initialErrorCount,
            HashSet<string> preBuildErrorKeys,
            string projectUniqueName = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
            var dte = _vsDependencies.GetDTE();
            if (dte?.ToolWindows?.ErrorList == null) return;

            await Task.Delay(ErrorListSettleDelayMs, ct).ConfigureAwait(false);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            var errorListMessages = new List<BuildMessage>();
            ErrorItems errorItems = dte.ToolWindows.ErrorList.ErrorItems;
            int count = errorItems.Count;

            for (int i = 1; i <= count; i++)
            {
                ErrorItem item;
                try
                {
                    item = errorItems.Item(i);
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"BuildSolution: Error item {i} unreadable: {ex.Message}");
                    continue;
                }
                if (item == null) continue;

                try
                {
                    if (item.ErrorLevel != EnvDTE80.vsBuildErrorLevel.vsBuildErrorLevelHigh)
                        continue;

                    if (projectUniqueName != null &&
                        !string.IsNullOrEmpty(item.Project) &&
                        !string.Equals(item.Project, projectUniqueName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string description = item.Description;
                    if (string.IsNullOrEmpty(description)) continue;

                    var buildMessage = new BuildMessage
                    {
                        File = item.FileName ?? string.Empty,
                        Line = item.Line,
                        Column = item.Column,
                        Project = item.Project,
                        Message = description
                    };

                    if (!ShouldIncludeError(initialErrorCount, count, preBuildErrorKeys, BuildMessageKey(buildMessage)))
                        continue;

                    errorListMessages.Add(buildMessage);
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"BuildSolution: Error item {i} failed to read: {ex.Message}");
                }
            }

            if (errorListMessages.Count > 0)
            {
                await Task.Run(() => MergeMessages(messages, errorListMessages), ct).ConfigureAwait(false);
            }
        }

        private static string BuildMessageKey(BuildMessage m)
        {
            string project = string.IsNullOrWhiteSpace(m.Project) ? string.Empty : NormalizeProjectName(m.Project);
            return $"{project}|{m.File}|{m.Line}|{m.Message}";
        }

        /// <summary>
        /// Collects the keys of all error-level entries currently visible in the Error List. 
        /// </summary>
        private static HashSet<string> SnapshotErrorKeys(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                ErrorItems items = dte?.ToolWindows?.ErrorList?.ErrorItems;
                if (items == null) return keys;

                int count = items.Count;
                for (int i = 1; i <= count; i++)
                {
                    try
                    {
                        ErrorItem item = items.Item(i);
                        if (item == null) continue;
                        if (item.ErrorLevel != EnvDTE80.vsBuildErrorLevel.vsBuildErrorLevelHigh) continue;
                        string description = item.Description;
                        if (string.IsNullOrEmpty(description)) continue;
                        keys.Add(BuildMessageKey(new BuildMessage
                        {
                            File = item.FileName ?? string.Empty,
                            Line = item.Line,
                            Project = item.Project,
                            Message = description
                        }));
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Debug($"BuildSolution: SnapshotErrorKeys: stale error entry {i} skipped: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"BuildSolution: SnapshotErrorKeys failed: {ex.Message}");
            }
            return keys;
        }

        /// <summary>
        /// Decides whether an Error List entry observed after the build belongs to this build or was already present before it.
        /// </summary>
        internal static bool ShouldIncludeError(int initialCount, int currentCount, HashSet<string> preBuildKeys, string key)
        {

            if (currentCount <= initialCount) return true;

            return preBuildKeys == null || !preBuildKeys.Contains(key);
        }

        internal static void MergeMessages(List<BuildMessage> target, IEnumerable<BuildMessage> source)
        {
            if (target == null || source == null) return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in target)
                if (m != null && !string.IsNullOrEmpty(m.Message))
                    seen.Add(BuildMessageKey(m));

            foreach (var m in source)
            {
                if (m == null || string.IsNullOrEmpty(m.Message)) continue;

                if (!seen.Add(BuildMessageKey(m)))
                    continue;

                var existing = FindByBaseKey(target, m);
                if (existing == null)
                {
                    target.Add(m);
                    continue;
                }

                bool existingHasProject = !string.IsNullOrEmpty(existing.Project);
                bool mHasProject = !string.IsNullOrEmpty(m.Project);

                if (mHasProject && !existingHasProject)
                {
                    existing.Project = m.Project;
                    existing.Column = m.Column;
                    seen.Add(BuildMessageKey(existing));
                    continue;
                }

                if (!mHasProject)
                {
                    continue;
                }

                target.Add(m);
            }
        }

        private static string BaseKey(BuildMessage m) => $"{m.File}|{m.Line}|{m.Message}";

        private static BuildMessage FindByBaseKey(List<BuildMessage> target, BuildMessage m)
        {
            string key = BaseKey(m);
            foreach (var t in target)
            {
                if (t == null) continue;
                if (string.Equals(BaseKey(t), key, StringComparison.OrdinalIgnoreCase))
                    return t;
            }
            return null;
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


        private static readonly string[] BuildPaneNames = { "Build", "Build Output" };

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

                    foreach (var candidate in BuildPaneNames)
                    {
                        if (name.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                            return pane;
                    }

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
                    string text = ReadPane(pane);
                    if (text != null && BuildPaneContentMarkers.Any(text.Contains))
                        return pane;
                }

                var active = dte.ToolWindows.OutputWindow.ActivePane;
                string probe = active?.TextDocument != null ? ReadPane(active) : null;
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

        private static string ReadPane(OutputWindowPane pane)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var doc = pane.TextDocument;
            return doc?.StartPoint.CreateEditPoint().GetText(doc.EndPoint.CreateEditPoint());
        }

        private static string ReadPaneFrom(OutputWindowPane pane, EditPoint start)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (start == null) return ReadPane(pane);
            try
            {
                var doc = pane?.TextDocument;
                if (doc == null) return null;

                return start.GetText(doc.EndPoint.CreateEditPoint());
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"BuildSolution: ReadPaneFrom failed, falling back to full read: {ex.Message}");
                try { return ReadPane(pane); }
                catch (Exception ex2)
                {
                    InternalLogger.Warn($"BuildSolution: ReadPane failed: {ex2.Message}");
                    return null;
                }
            }
        }

        private async Task<string> StabilizeBuildOutputAsync(
            DTE2 dte,
            CancellationToken ct,
            OutputWindowPane preBuildPane,
            EditPoint buildStartPoint)
        {
            await Task.Delay(BuildOutputInitialDelayMs, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            var pane = preBuildPane ?? LocateBuildPane(dte);
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
                    return ReadPaneFrom(pane, buildStartPoint);
                previous = current;
            }
            return ReadPaneFrom(pane, buildStartPoint);
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
                return resp.Success
                    ? $"Build completed."
                    : $"Build failed.";
            }
            return "Build completed.";
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Builds the currently opened VS solution asynchronously, or a single project when 'project_name' is specified. Use after making code changes to verify they compile. Fails if no solution is open, or a build is already in progress. Returns build status and any compilation errors with file/line/column details. ",
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

        [JsonProperty("error_message")]
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
