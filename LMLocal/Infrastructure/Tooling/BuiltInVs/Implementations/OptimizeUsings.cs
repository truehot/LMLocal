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
    internal interface IOptimizeUsings : IBuiltInTool { }

    internal class OptimizeUsings : IOptimizeUsings
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly ISnapshotManager _snapshotManager;

        public string ToolName => "optimize_usings";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.FullAccess;

        public OptimizeUsings(IVsDependencies vsDependencies, IPathResolver pathResolver, ISnapshotManager snapshotManager)
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

            string solutionDir = _vsDependencies.GetSolutionDirectory();
            if (string.IsNullOrEmpty(solutionDir))
                return ErrorResponse("Solution directory not available.");

            if (!_pathResolver.TryResolveFilePath(filePathParam, solutionDir, out string absolutePath))
                return ErrorResponse($"Cannot resolve file path: {filePathParam}");

            if (!File.Exists(absolutePath))
                return ErrorResponse($"File not found: {absolutePath}");

            var dte = _vsDependencies.GetDTE();
            if (dte == null)
                return ErrorResponse("DTE service not available.");

            Window window = null;
            bool wasOpen = false;
            try
            {
                foreach (Document doc in dte.Documents)
                {
                    if (string.Equals(doc.FullName, absolutePath, StringComparison.OrdinalIgnoreCase))
                    {
                        wasOpen = true;
                        window = doc.ActiveWindow;
                        doc.Activate();
                        break;
                    }
                }

                if (!wasOpen)
                {
                    window = dte.ItemOperations.OpenFile(absolutePath, EnvDTE.Constants.vsViewKindAny);
                }
                await _snapshotManager.SnapshotFileAsync(absolutePath, SnapshotChangeStatus.BeforeModify, cancellationToken).ConfigureAwait(false);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                dte.ExecuteCommand("Edit.RemoveAndSort");
                dte.ActiveDocument.Save();

                return new RemoveAndSortUsingsResponse
                {
                    Success = true,
                    FilePath = absolutePath
                };
            }
            catch (Exception ex)
            {
                return ErrorResponse($"Remove and sort usings failed: {ex.Message}");
            }
            finally
            {
                if (!wasOpen && window != null)
                {
                    window.Close(vsSaveChanges.vsSaveChangesNo);
                }
            }
        }

        private static RemoveAndSortUsingsResponse ErrorResponse(string message)
        {
            return new RemoveAndSortUsingsResponse
            {
                Success = false,
                ErrorMessage = message
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var path = parameters?.TryGetValue("file_path", out var p) == true ? p?.ToString() : "file";
            return $"Removing and sorting usings for '{path}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is RemoveAndSortUsingsResponse resp)
                return resp.Success ? "Successfully removed and sorted usings." : $"Remove and sort usings failed: {resp.ErrorMessage}";
            return "Remove and sort usings completed.";
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Removes unused 'using' directives and sorts the remaining ones alphabetically in a C# code file. Use after adding or removing code that changes which namespaces are needed. The file is opened, processed, saved, and closed automatically. Only works on C# files. Fails if the file does not exist. Example: {\"file_path\":\"src/Program.cs\"} → {\"success\":true,\"error_message\":null,\"file_path\":\"src/Program.cs\"}.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Path to the C# file to process (absolute or relative to solution root)." } }
                    },
                    Required = new List<string> { "file_path" }
                }
            };
        }
    }

    public class RemoveAndSortUsingsResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("error_message")]
        public string ErrorMessage { get; set; }

        [JsonProperty("file_path")]
        public string FilePath { get; set; }
    }
}