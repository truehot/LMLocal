using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    internal interface ISetFileProjectStatus : IBuiltInTool { }

    internal class SetFileProjectStatus : ISetFileProjectStatus
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly ISnapshotManager _snapshotManager;

        public string ToolName => "set_file_project_status";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.FullAccess;

        public SetFileProjectStatus(IVsDependencies vsDependencies, IPathResolver pathResolver, ISnapshotManager snapshotManager)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (parameters?.TryGetValue("file_path", out var fp) != true || !(fp is string filePathParam) || string.IsNullOrEmpty(filePathParam))
                return ErrorResponse("Parameter 'file_path' is required.");

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

            if (!_pathResolver.TryResolveFilePath(filePathParam, solutionDir, out string absoluteFilePath))
                return ErrorResponse($"Cannot resolve file path: {filePathParam}");

            if (!_pathResolver.TryResolveFilePath(projectPathParam, solutionDir, out string absoluteProjectPath))
                return ErrorResponse($"Cannot resolve project path: {projectPathParam}");

            if (!File.Exists(absoluteFilePath))
                return ErrorResponse($"File not found: {absoluteFilePath}");

            if (!File.Exists(absoluteProjectPath))
                return ErrorResponse($"Project file not found: {absoluteProjectPath}");

            var dte = _vsDependencies.GetDTE();
            if (dte == null)
                return ErrorResponse("DTE service not available.");

            Project targetProject = null;
            foreach (Project proj in dte.Solution.Projects)
            {
                string projFile = proj.FullName;
                if (string.Equals(projFile, absoluteProjectPath, StringComparison.OrdinalIgnoreCase))
                {
                    targetProject = proj;
                    break;
                }
            }
            if (targetProject == null)
                return ErrorResponse($"Project not found in solution: {absoluteProjectPath}");

            try
            {
                await _snapshotManager.SnapshotFileAsync(absoluteProjectPath, SnapshotChangeStatus.BeforeModify, cancellationToken).ConfigureAwait(false);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (include)
                {
                    ProjectItem existing = FindProjectItem(targetProject, absoluteFilePath);
                    if (existing != null)
                    {
                        return new IncludeExcludeResponse
                        {
                            Success = true,
                            FilePath = absoluteFilePath,
                            Message = "File already included in the project."
                        };
                    }

                    targetProject.ProjectItems.AddFromFile(absoluteFilePath);
                    targetProject.Save();

                    return new IncludeExcludeResponse
                    {
                        Success = true,
                        FilePath = absoluteFilePath,
                        Message = "File included successfully."
                    };
                }
                else
                {
                    ProjectItem item = FindProjectItem(targetProject, absoluteFilePath);
                    if (item == null)
                        return ErrorResponse("File is not part of the project.");

                    item.Delete();
                    targetProject.Save();

                    return new IncludeExcludeResponse
                    {
                        Success = true,
                        FilePath = absoluteFilePath,
                        Message = "File excluded successfully."
                    };
                }
            }
            catch (Exception ex)
            {
                return ErrorResponse($"Operation failed: {ex.Message}");
            }
        }

        private ProjectItem FindProjectItem(Project project, string filePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (project == null || string.IsNullOrEmpty(filePath))
                return null;

            try
            {
                return FindProjectItemRecursive(project.ProjectItems, Path.GetFileName(filePath), filePath);
            }
            catch
            {
                return null;
            }
        }

        private ProjectItem FindProjectItemRecursive(ProjectItems items, string fileName, string fullPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (items == null) return null;

            foreach (ProjectItem item in items)
            {
                try
                {
                    if (item.Kind == EnvDTE.Constants.vsProjectItemKindPhysicalFile)
                    {
                        string itemPath = item.FileNames[1];
                        if (string.Equals(itemPath, fullPath, StringComparison.OrdinalIgnoreCase))
                            return item;
                    }
                    else if (item.Kind == EnvDTE.Constants.vsProjectItemKindPhysicalFolder)
                    {
                        var found = FindProjectItemRecursive(item.ProjectItems, fileName, fullPath);
                        if (found != null) return found;
                    }
                }
                catch { }
            }
            return null;
        }

        private static IncludeExcludeResponse ErrorResponse(string message)
        {
            return new IncludeExcludeResponse
            {
                Success = false,
                ErrorMessage = message
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var path = parameters?.TryGetValue("file_path", out var p) == true ? p?.ToString() : "file";
            var include = parameters?.TryGetValue("include", out var inc) == true && inc is bool b && b ? "Include" : "Exclude";
            return $"{include} file '{path}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is IncludeExcludeResponse resp)
                return resp.Success ? resp.Message : $"Operation failed: {resp.ErrorMessage}";
            return "Operation completed.";
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Includes or excludes a file from a specific .csproj project without deleting the file from disk. Use include=true (default) to add a file to a project via AddFromFile; use include=false to remove a file from the project (the file remains on disk — use delete_file to delete it physically). Both the file and the project must exist. Does not copy or move the file — it only adds/removes the project reference. Example: {\"file_path\":\"src/NewService.cs\",\"project_path\":\"src/MyApp.csproj\",\"include\":true} → {\"success\":true,\"file_path\":\"src/NewService.cs\",\"error_message\":null,\"message\":\"File included successfully.\"}.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        ["file_path"] = new ToolDetails
                        {
                            Type = "string",
                            Description = "Absolute or relative path to the file to include/exclude."
                        },
                        ["project_path"] = new ToolDetails
                        {
                            Type = "string",
                            Description = "Absolute or relative path to the .csproj file."
                        },
                        ["include"] = new ToolDetails
                        {
                            Type = "boolean",
                            Description = "True to include (add) the file, false to exclude (remove). Default: true."
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

        [JsonProperty("error_message")]
        public string ErrorMessage { get; set; }

        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }
    }
}