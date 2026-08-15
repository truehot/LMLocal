using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Tool to delete files with backup capability for undo.
    /// </summary>
    internal interface IDeleteFile : IBuiltInTool
    {
    }

    internal class DeleteFile : IDeleteFile
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly ISnapshotManager _snapshotManager;
        private readonly IFileSystem _fileSystem;

        public string ToolName => "delete_file";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.FullAccess;

        public DeleteFile(IVsDependencies vsDependencies, IPathResolver pathResolver,
            ISnapshotManager snapshotManager, IFileSystem fileSystem)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Deletes a file from disk. Fails if the file does not exist. The file is permanently removed. Path relative to solution root. For files referenced in a project, use set_file_project_status with include=false instead - it removes the project reference and deletes the file in one step.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Relative path to file to delete." } }
                    },
                    Required = new List<string> { "file_path" }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                var (filePath, error) = ExtractAndValidateParameters(parameters);
                if (!string.IsNullOrEmpty(error))
                    return Error(error);

                if (!_vsDependencies.IsSolutionOpen)
                    return Error("No solution is currently open.");

                string solutionDir = _vsDependencies.GetSolutionDirectory();

                if (!_pathResolver.TryResolveFilePath(filePath, solutionDir, out string absolutePath))
                    return Error($"Failed to resolve file path: {filePath}");

                if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                    return Error($"File '{filePath}' is outside the solution directory '{solutionDir}'.");

                try
                {
                    _fileSystem.ValidateFilePath(absolutePath);
                }
                catch (ArgumentException ex)
                {
                    return Error($"Invalid file path: {ex.Message}");
                }

                if (!_fileSystem.FileExists(absolutePath))
                    return Error($"File not found: {filePath}");

                await _snapshotManager.SnapshotFileAsync(absolutePath, SnapshotChangeStatus.BeforeDelete, cancellationToken).ConfigureAwait(false);

                _fileSystem.Delete(absolutePath);

                if (_fileSystem.FileExists(absolutePath))
                    return Error($"Failed to delete file: {filePath}");

                _pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath);

                return new DeleteFileResponse
                {
                    Success = true,
                    FilePath = relativePath ?? absolutePath,
                    DeletedSuccessfully = true
                };
            }
            catch (OperationCanceledException)
            {
                return Error("Operation was cancelled.");
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error in {ToolName}: {ex}");
                return Error($"Error: {ex.Message}");
            }
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var filePath = (parameters?.TryGetValue("file_path", out var f) == true ? f?.ToString() : "") ?? "";
            return $"Deleting file '{filePath}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is DeleteFileResponse response)
                return response.Success ? "File deleted." : $"File deletion failed: {response.ErrorMessage}";
            return "File deletion finished.";
        }

        private (string filePath, string error) ExtractAndValidateParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, "Parameters cannot be null.");

            if (!parameters.TryGetValue("file_path", out object filePathObj) || !(filePathObj is string))
                return (null, "file_path parameter is required and must be a string.");

            return ((string)filePathObj, null);
        }

        private static DeleteFileResponse Error(string message)
        {
            return new DeleteFileResponse
            {
                Success = false,
                ErrorMessage = message,
                DeletedSuccessfully = false
            };
        }
    }

    internal class DeleteFileResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        [JsonProperty("deleted_successfully")]
        public bool DeletedSuccessfully { get; set; }

        [JsonProperty("error_message")]
        public string ErrorMessage { get; set; }
    }
}
