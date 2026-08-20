using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    internal interface IListDirectory : IBuiltInTool
    {
    }

    internal class ListDirectory : IListDirectory
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IFileSystem _fileSystem;
        private const int MaxEntries = 200;


        public string ToolName => "list_directory";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

        public ListDirectory(IVsDependencies vsDependencies, IPathResolver pathResolver, IFileSystem fileSystem)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Lists files and subdirectories within a given path. Generated and dependency directories (bin, obj, node_modules, .git, etc.) are automatically excluded from results. Returns up to 200 entries; if has_more_results is true, the directory has more entries not shown. Only works inside the solution directory — paths outside the solution are rejected. Use '.' for the solution root.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "directory_path", new ToolDetails { Type = "string", Description = "Path to list (relative to solution root or absolute). Use '.' for solution root." } }
                    },
                    Required = new List<string> { "directory_path" }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                var (directoryPath, error) = ExtractAndValidateParameters(parameters);
                if (error != null)
                    return Error(error, directoryPath);

                if (!_vsDependencies.IsSolutionOpen)
                    return Error("No solution is currently open.", directoryPath);

                string solutionDir = _vsDependencies.GetSolutionDirectory();

                if (!_pathResolver.TryResolveFilePath(directoryPath, solutionDir, out string absolutePath) || string.IsNullOrEmpty(absolutePath))
                    return Error($"Directory not found: {directoryPath}", directoryPath);

                if (!_fileSystem.DirectoryExists(absolutePath))
                    return Error($"Directory not found: {absolutePath}", directoryPath);

                if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                    return Error($"Directory '{absolutePath}' is outside the solution directory '{solutionDir}'.", directoryPath);

                if (!_pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath))
                    relativePath = absolutePath;

                if (string.IsNullOrEmpty(relativePath))
                    relativePath = ".";

                var result = await EnumerateDirectoryContentsAsync(absolutePath, solutionDir, cancellationToken);

                result.DirectoryPath = relativePath;
                return result;
            }
            catch (Exception ex)
            {
                return new DirectoryContentsResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    DirectoryPath = parameters?.TryGetValue("directory_path", out var dp) == true ? dp?.ToString() : "",
                    Entries = new List<DirectoryEntry>(),
                    HasMoreResults = false
                };
            }
        }

        private async Task<DirectoryContentsResponse> EnumerateDirectoryContentsAsync(string absolutePath, string solutionDir, CancellationToken cancellationToken)
        {
            var result = new DirectoryContentsResponse
            {
                DirectoryPath = "",
                Entries = new List<DirectoryEntry>(),
                Success = true,
                ErrorMessage = null
            };

            try
            {
                var entries = await _fileSystem.EnumerateDirectoryAsync(absolutePath, VsFileFilter.ExcludedDirectories, cancellationToken);

                int entryCount = 0;
                foreach (var entry in entries)
                {
                    if (entryCount >= MaxEntries)
                    {
                        result.HasMoreResults = true;
                        break;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    if (!_pathResolver.TryGetRelativePath(entry.FullPath, solutionDir, out string entryRelativePath))
                        entryRelativePath = entry.FullPath;

                    result.Entries.Add(new DirectoryEntry
                    {
                        Name = entry.Name,
                        Path = entryRelativePath,
                        Type = entry.IsDirectory ? "directory" : "file"
                    });
                    entryCount++;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Access denied to directory '{absolutePath}': {ex.Message}";
            }
            catch (IOException ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Error reading directory '{absolutePath}': {ex.Message}";
            }

            return result;
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            if (parameters == null) return "Listing directory... ";

            var path = parameters.TryGetValue("directory_path", out var p) ? p?.ToString() : "";
            return $"Listing directory '{path}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is DirectoryContentsResponse dirResult)
                return dirResult.Success
                    ? $"Listed {dirResult.Entries.Count} {Pluralizer.Pluralize(dirResult.Entries.Count, "entry", "entries")}."
                    : $"Listing directory failed: {dirResult.ErrorMessage}";
            return "Directory listing finished.";
        }

        private (string directoryPath, string error) ExtractAndValidateParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, "Parameters are required.");

            if (!parameters.TryGetValue("directory_path", out object pathObj) || !(pathObj is string))
                return (null, "directory_path parameter is required and must be a string.");

            var directoryPath = (string)pathObj;

            if (string.IsNullOrWhiteSpace(directoryPath))
                directoryPath = ".";

            return (directoryPath, null);
        }

        private static DirectoryContentsResponse Error(string message, string directoryPath = "")
        {
            return new DirectoryContentsResponse
            {
                Success = false,
                ErrorMessage = message,
                DirectoryPath = directoryPath,
                Entries = new List<DirectoryEntry>(),
                HasMoreResults = false
            };
        }

        public class DirectoryEntry
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("path")]
            public string Path { get; set; }

            [JsonProperty("type")]
            public string Type { get; set; }
        }

        public class DirectoryContentsResponse
        {
            [JsonProperty("directory")]
            public string DirectoryPath { get; set; }

            [JsonProperty("entries")]
            public List<DirectoryEntry> Entries { get; set; }

            [JsonProperty("has_more_results")]
            public bool HasMoreResults { get; set; }

            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("error_message")]
            public string ErrorMessage { get; set; }
        }
    }
}
