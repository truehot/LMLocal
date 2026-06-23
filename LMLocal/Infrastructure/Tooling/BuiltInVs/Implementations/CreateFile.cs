using System;
using System.Collections.Generic;
using System.Text;
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
    /// Tool to create new file with content.
    /// </summary>
    internal interface ICreateFile : IBuiltInTool
    {
    }

    internal class CreateFile : ICreateFile
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IFileSystem _fileSystem;
        private readonly ISnapshotManager _snapshotManager;
        public string ToolName => "create_file";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.FullAccess;

        public CreateFile(IVsDependencies vsDependencies, IPathResolver pathResolver, IFileSystem fileSystem, ISnapshotManager snapshotManager)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Creates a new file with the given content. Fails if the file already exists — use replace_file_content to update an existing file instead. Parent directories are created automatically if they don't exist. Path can be absolute or relative to solution root. Example: {\"file_path\":\"Models/Customer.cs\",\"content\":\"public class Customer {}\"} → {\"success\":true,\"file_path\":\"Models/Customer.cs\",\"created_successfully\":true,\"error_message\":null}.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Absolute or relative path to file to create." } },
                        { "content", new ToolDetails { Type = "string", Description = "Initial content for the file." } }
                    },
                    Required = new List<string> { "file_path", "content" }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                var (filePath, content, error) = ExtractAndValidateParameters(parameters);
                if (!string.IsNullOrEmpty(error))
                    return Error(error);

                if (!_vsDependencies.IsSolutionOpen)
                    return Error("No solution is currently open.");

                string solutionDir = _vsDependencies.GetSolutionDirectory();

                if (!_pathResolver.TryResolveFilePath(filePath, solutionDir, out string absolutePath))
                    return Error($"Failed to resolve file path: {filePath}");

                try
                {
                    _fileSystem.ValidateFilePath(absolutePath);
                }
                catch (ArgumentException ex)
                {
                    return Error($"Invalid file path: {ex.Message}");
                }

                if (_fileSystem.FileExists(absolutePath))
                    return Error($"File already exists: {filePath}");
                
                await _snapshotManager.SnapshotFileAsync(absolutePath, SnapshotChangeStatus.BeforeCreate, cancellationToken).ConfigureAwait(false);

                _fileSystem.EnsureDirectoryExistsForFile(absolutePath);

                byte[] data = Encoding.UTF8.GetBytes(content);
                await _fileSystem.WriteAllBytesAsync(absolutePath, data, cancellationToken).ConfigureAwait(false);

                _pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath);

                return new CreateFileResponse
                {
                    Success = true,
                    FilePath = relativePath ?? absolutePath,
                    CreatedSuccessfully = true
                };
            }
            catch (OperationCanceledException)
            {
                InternalLogger.Info($"Operation in {ToolName} was cancelled.");
                return Error($"Operation in {ToolName} was cancelled.");
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
            return $"Creating file '{filePath}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is CreateFileResponse response)
                return response.Success ? "File created." : $"File creation failed: {response.ErrorMessage}";
            return "File creation finished.";
        }

        private (string filePath, string content, string error) ExtractAndValidateParameters(
            Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, null, "Parameters cannot be null.");

            if (!parameters.TryGetValue("file_path", out object filePathObj) || !(filePathObj is string))
                return (null, null, "file_path parameter is required and must be a string.");

            if (!parameters.TryGetValue("content", out object contentObj) || !(contentObj is string))
                return (null, null, "content parameter is required and must be a string.");

            return ((string)filePathObj, (string)contentObj, null);
        }

        private static CreateFileResponse Error(string message)
        {
            return new CreateFileResponse
            {
                Success = false,
                ErrorMessage = message,
                CreatedSuccessfully = false
            };
        }
    }

    internal class CreateFileResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        [JsonProperty("created_successfully")]
        public bool CreatedSuccessfully { get; set; }

        [JsonProperty("error_message")]
        public string ErrorMessage { get; set; }
    }
}