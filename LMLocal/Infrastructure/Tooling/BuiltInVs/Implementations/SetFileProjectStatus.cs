using LMLocal.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Projects;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    internal interface ISetFileProjectStatus : IBuiltInTool { }

    internal class SetFileProjectStatus : ISetFileProjectStatus
    {
        private const string DiscardedChangesWarning =
            "The project had unsaved changes in Visual Studio; they were discarded because the project was reloaded from disk before this change.";

        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly ISnapshotManager _snapshotManager;
        private readonly IFileSystem _fileSystem;

        public string ToolName => "set_file_project_status";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.FullAccess;

        public SetFileProjectStatus(IVsDependencies vsDependencies, IPathResolver pathResolver, ISnapshotManager snapshotManager, IFileSystem fileSystem)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (!FilePathList.TryParse(parameters, out List<string> filePaths, out string pathsError, ExistingFileCheck()))
                return ErrorResponse(pathsError);

            if (parameters?.TryGetValue("project_path", out var pp) != true || !(pp is string projectPathParam) || string.IsNullOrEmpty(projectPathParam))
                return ErrorResponse("Parameter 'project_path' is required.");

            bool include = true;
            if (parameters.TryGetValue("include", out var includeObj))
            {
                if (includeObj is bool includeBool)
                {
                    include = includeBool;
                }
                else if (includeObj is string includeStr && bool.TryParse(includeStr, out bool parsed))
                {
                    include = parsed;
                }
            }

            string solutionDir = _vsDependencies.GetSolutionDirectory();
            if (string.IsNullOrEmpty(solutionDir))
                return ErrorResponse("Solution directory not available.");

            if (!_pathResolver.TryResolveFilePath(projectPathParam, solutionDir, out string absoluteProjectPath))
                return ErrorResponse($"Cannot resolve project path: {projectPathParam}");

            if (!_pathResolver.IsPathInsideDirectory(absoluteProjectPath, solutionDir))
                return ErrorResponse($"Project '{projectPathParam}' is outside the solution directory '{solutionDir}'.");

            if (!_fileSystem.FileExists(absoluteProjectPath))
                return ErrorResponse($"Project file not found: {absoluteProjectPath}");

            var dte = _vsDependencies.GetDTE();
            if (dte == null)
                return ErrorResponse("DTE service not available.");

            Project targetProject = TryFindProject(dte, absoluteProjectPath);
            if (targetProject == null)
                return ErrorResponse($"Project not found in solution: {absoluteProjectPath}");

            var results = new IncludeExcludeResponse[filePaths.Count];
            var absolutePaths = new string[filePaths.Count];
            for (int i = 0; i < filePaths.Count; i++)
            {
                string filePath = filePaths[i];
                if (!_pathResolver.TryResolveFilePath(filePath, solutionDir, out string absolutePath))
                    results[i] = ErrorResponse($"Cannot resolve file path: {filePath}", filePath);
                else if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                    results[i] = ErrorResponse($"File '{filePath}' is outside the solution directory '{solutionDir}'.", filePath);
                else if (include && !_fileSystem.FileExists(absolutePath))
                    results[i] = ErrorResponse($"File not found: {absolutePath}", filePath);
                else
                    absolutePaths[i] = absolutePath;
            }

            if (Array.TrueForAll(absolutePaths, p => p == null))
                return Combine(results);

            try
            {
                await _snapshotManager.SnapshotFileAsync(absoluteProjectPath, SnapshotChangeStatus.BeforeModify, cancellationToken).ConfigureAwait(false);

                string filtersPath = absoluteProjectPath + ".filters";
                if (_fileSystem.FileExists(filtersPath))
                    await _snapshotManager.SnapshotFileAsync(filtersPath, SnapshotChangeStatus.BeforeModify, cancellationToken).ConfigureAwait(false);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            }
            catch (Exception ex)
            {
                return ErrorResponse($"Operation failed: {ex.Message}");
            }

            bool hadUnsavedChanges = IsProjectDirty(targetProject);
            if (!TryReloadProject(targetProject))
                return ErrorResponse($"The project could not be reloaded from disk, so nothing was changed: {absoluteProjectPath}");

            string reloadWarning = hadUnsavedChanges ? DiscardedChangesWarning : null;

            targetProject = TryFindProject(dte, absoluteProjectPath);
            if (targetProject == null)
            {
                var notFound = ErrorResponse($"Project not found in solution after reload: {absoluteProjectPath}");
                notFound.Warning = reloadWarning;
                return notFound;
            }

            var changed = new List<int>();
            for (int i = 0; i < absolutePaths.Length; i++)
            {
                if (absolutePaths[i] == null)
                    continue;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                try
                {
                    results[i] = include
                        ? IncludeOne(targetProject, absolutePaths[i], changed, i)
                        : await ExcludeOneAsync(targetProject, absolutePaths[i], changed, i, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    results[i] = ErrorResponse($"Operation failed: {ex.Message}", absolutePaths[i]);
                }
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (changed.Count > 0)
            {
                try
                {
                    targetProject.Save();

                    TryReloadProject(targetProject);
                }
                catch (Exception ex)
                {
                    foreach (int i in changed)
                        results[i] = ErrorResponse($"The change was applied in Visual Studio but the project could not be saved to disk: {ex.Message}", absolutePaths[i]);
                }
            }

            return Combine(results, reloadWarning);
        }

        /// <summary>
        /// Lets <see cref="FilePathList"/> keep a single path that contains a comma when that path is an existing file.
        /// </summary>
        private Func<string, bool> ExistingFileCheck()
        {
            string solutionDir = _vsDependencies.GetSolutionDirectory();
            if (string.IsNullOrEmpty(solutionDir))
                return null;

            return path => _pathResolver.TryResolveFilePath(path, solutionDir, out string absolutePath)
                           && _fileSystem.FileExists(absolutePath);
        }

        private IncludeExcludeResponse IncludeOne(Project project, string absolutePath, List<int> changed, int index)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (FindProjectItem(project, absolutePath) != null)
            {
                return new IncludeExcludeResponse
                {
                    Success = true,
                    FilePath = absolutePath,
                    Message = "File already included in the project."
                };
            }

            project.ProjectItems.AddFromFile(absolutePath);
            changed.Add(index);

            return new IncludeExcludeResponse
            {
                Success = true,
                FilePath = absolutePath,
                Message = "File included successfully."
            };
        }

        /// <summary>
        /// Removes the file from the project.
        /// </summary>
        private async Task<IncludeExcludeResponse> ExcludeOneAsync(Project project, string absolutePath, List<int> changed, int index, CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            ProjectItem item = FindProjectItem(project, absolutePath);
            if (item == null)
                return ErrorResponse("File is not part of the project.", absolutePath);

            bool existedOnDisk = _fileSystem.FileExists(absolutePath);
            try
            {
                await _snapshotManager.SnapshotFileAsync(absolutePath, SnapshotChangeStatus.BeforeDelete, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return ErrorResponse($"File was not removed from the project because it could not be snapshotted: {ex.Message}", absolutePath);
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            item.Delete();
            changed.Add(index);

            return new IncludeExcludeResponse
            {
                Success = true,
                FilePath = absolutePath,
                Message = existedOnDisk ? "File deleted from disk." : "File removed from the project; it was already missing on disk."
            };
        }

        /// <summary>
        /// One file keeps the single-file response shape; several files are reported per file.
        /// </summary>
        private static object Combine(IncludeExcludeResponse[] results, string warning = null)
        {
            if (results.Length == 1)
            {
                if (warning != null)
                    results[0].Warning = warning;
                return results[0];
            }

            var batch = new FileBatchResponse<IncludeExcludeResponse> { TotalFiles = results.Length, Warning = warning };
            foreach (var result in results)
            {
                batch.Files.Add(result);
                if (result.Success)
                    batch.Succeeded++;
            }

            batch.Success = batch.Succeeded > 0;
            if (!batch.Success)
                batch.ErrorMessage = "None of the files could be processed; see 'files' for each error.";
            return batch;
        }

        private ProjectItem FindProjectItem(Project project, string filePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (project == null || string.IsNullOrEmpty(filePath))
                return null;

            try
            {
                return FindProjectItemRecursive(project.ProjectItems, filePath);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"SetFileProjectStatus: could not search '{filePath}' in the project: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Depth-first search by full path through folders and through the child items of files.
        /// </summary>
        private ProjectItem FindProjectItemRecursive(ProjectItems items, string fullPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (items == null) return null;

            foreach (ProjectItem item in items)
            {
                try
                {
                    if (item.Kind == EnvDTE.Constants.vsProjectItemKindPhysicalFile
                        && string.Equals(item.FileNames[1], fullPath, StringComparison.OrdinalIgnoreCase))
                        return item;

                    var found = FindProjectItemRecursive(item.ProjectItems, fullPath);
                    if (found != null) return found;
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"SetFileProjectStatus: could not inspect a project item while looking for '{fullPath}': {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>
        /// Reloads the project from disk, so that a later <see cref="Project.Save"/> writes the disk version plus this change
        /// instead of a stale in-memory copy that would revert edits made outside Visual Studio.
        /// </summary>
        private bool TryReloadProject(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (project == null)
                return false;

            string projectPath = ProjectFinder.SafeName(project, ProjectField.FullName) ?? "?";
            try
            {
                var solution = _vsDependencies.GetSolution();
                if (!(solution is IVsSolution4 solution4))
                {
                    InternalLogger.Warn("SetFileProjectStatus: IVsSolution4 is not available, the project was not reloaded from disk.");
                    return false;
                }

                if (solution.GetProjectOfUniqueName(project.UniqueName, out IVsHierarchy hierarchy) != VSConstants.S_OK || hierarchy == null)
                {
                    InternalLogger.Warn($"SetFileProjectStatus: project '{projectPath}' is not part of the open solution, it was not reloaded from disk.");
                    return false;
                }

                if (solution.GetGuidOfProject(hierarchy, out Guid projectGuid) != VSConstants.S_OK)
                {
                    InternalLogger.Warn($"SetFileProjectStatus: the project GUID of '{projectPath}' is not available, the project was not reloaded from disk.");
                    return false;
                }

                int hr = solution4.ReloadProject(projectGuid);
                if (ErrorHandler.Failed(hr))
                {
                    InternalLogger.Warn($"SetFileProjectStatus: reloading '{projectPath}' from disk failed with HRESULT 0x{hr:X8}.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"SetFileProjectStatus: failed to reload project '{projectPath}' from disk: {ex.Message}");
                return false;
            }
        }

        /// <summary>Unsaved project changes in Visual Studio; a project system that does not report it counts as saved.</summary>
        private static bool IsProjectDirty(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return project.IsDirty;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"SetFileProjectStatus: IsDirty is not available for '{ProjectFinder.SafeName(project, ProjectField.FullName)}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// <see cref="ProjectFinder.FindByPath"/> with a last line of defence: a project whose name is not a usable file
        /// path must not take the whole call down.
        /// </summary>
        private static Project TryFindProject(EnvDTE80.DTE2 dte, string absoluteProjectPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return ProjectFinder.FindByPath(dte.Solution, absoluteProjectPath);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"SetFileProjectStatus: could not search the solution for project '{absoluteProjectPath}': {ex.Message}");
                return null;
            }
        }

        private static IncludeExcludeResponse ErrorResponse(string message, string filePath = null)
        {
            return new IncludeExcludeResponse
            {
                Success = false,
                ErrorMessage = message,
                FilePath = filePath
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            FilePathList.TryParse(parameters, out List<string> paths, out _);
            var path = paths.Count > 1 ? $"{paths.Count} files" : (paths.Count == 1 ? paths[0] : "file");
            bool include = true;
            if (parameters?.TryGetValue("include", out var inc) == true)
            {
                if (inc is bool incBool)
                    include = incBool;
                else if (inc is string incStr && bool.TryParse(incStr, out bool parsed))
                    include = parsed;
            }
            string action = include ? "Include" : "Delete";
            return paths.Count > 1 ? $"{action} {path}... " : $"{action} file '{path}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is FileBatchResponse<IncludeExcludeResponse> batch)
            {
                int failed = batch.TotalFiles - batch.Succeeded;
                return failed == 0
                    ? $"Processed {batch.TotalFiles} files."
                    : $"Processed {batch.Succeeded} of {batch.TotalFiles} files, {failed} failed.";
            }

            if (result is IncludeExcludeResponse resp)
                return resp.Success ? resp.Message : $"Operation failed: {resp.ErrorMessage}";
            return "Operation completed.";
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Adds an existing file to the specified .csproj or .vcxproj project. REQUIRED when a file must be registered in a project. If the task requires adding a new .cs file to a project, first use create_file if the file does not exist, then MUST use this tool to add it to the project. Do NOT manually edit the .csproj or .vcxproj XML, ItemGroup, Compile Include, or project file entries to accomplish this. This rule applies especially for legacy .csproj projects that use explicit <Compile Include=\"...\" /> entries. Use include=false only when removing the file from the project and deleting it from disk. Both file_path and project_path are required; the project file must exist. Several files for the same project: one call with comma-separated paths in file_path.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        ["file_path"] = new ToolDetails
                        {
                            Type = "string",
                            Description = "Relative path to the file to include/exclude, or several comma-separated paths (\"A.cs, B.cs\")."
                        },
                        ["project_path"] = new ToolDetails
                        {
                            Type = "string",
                            Description = "Relative path to the project file (.csproj or .vcxproj)."
                        },
                        ["include"] = new ToolDetails
                        {
                            Type = "boolean",
                            Description = "True to include (add) the file, false to exclude and delete the file. Default: true."
                        },
                        ["file_paths"] = new ToolDetails
                        {
                            Type = "array",
                            Description = "Optional: more paths as an array.",
                            Items = new ToolDetails { Type = "string" }
                        }
                    },
                    Required = new List<string> { "file_path", "project_path" }
                }
            };
        }
    }

    public class IncludeExcludeResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }

        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        [JsonProperty("message", NullValueHandling = NullValueHandling.Ignore)]
        public string Message { get; set; }

        [JsonProperty("warning", NullValueHandling = NullValueHandling.Ignore)]
        public string Warning { get; set; }
    }
}
