using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Tool to list all files and subdirectories within a specified path.
    /// </summary>
    internal interface IListDirectoryContents : IBuiltInTool
    {
    }

    internal class ListDirectoryContents : IListDirectoryContents
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private const int MaxEntries = 200;

        private static readonly HashSet<string> _excludedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin",
            "obj",
            ".vs",
            ".git",
            "CopilotBaseline"
        };

        public string ToolName => "List_Directory_Contents";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

        public ListDirectoryContents(IVsDependencies vsDependencies, IPathResolver pathResolver)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = $"Lists files and subdirectories within a path. System directories (bin, obj, .vs, .git, CopilotBaseline) are excluded from results. Response fields: success (bool), error_message (string), directory (string), entries (array of {{name (string), path (string), type (string)}}), has_more_results (bool). has_more_results indicates more entries exist beyond the {MaxEntries} entry limit. Only works inside solution directory.",
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

                if (!Directory.Exists(absolutePath))
                    return Error($"Directory not found: {absolutePath}", directoryPath);

                if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                    return Error($"Directory '{absolutePath}' is outside the solution directory '{solutionDir}'.", directoryPath);

                if (!_pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath))
                    relativePath = absolutePath;

                if (string.IsNullOrEmpty(relativePath))
                    relativePath = ".";

                var result = await Task.Run(() => EnumerateDirectoryContents(absolutePath, solutionDir, cancellationToken), cancellationToken);

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

        private DirectoryContentsResponse EnumerateDirectoryContents(string absolutePath, string solutionDir, CancellationToken cancellationToken)
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
                var dirInfo = new DirectoryInfo(absolutePath);
                int entryCount = 0;

                foreach (var dir in dirInfo.EnumerateDirectories()
                    .Where(d => !_excludedDirectories.Contains(d.Name))
                    .OrderBy(d => d.Name))
                {
                    if (entryCount >= MaxEntries)
                    {
                        result.HasMoreResults = true;
                        break;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    var fullPath = dir.FullName;
                    if (!_pathResolver.TryGetRelativePath(fullPath, solutionDir, out string entryRelativePath))
                        entryRelativePath = fullPath;

                    result.Entries.Add(new DirectoryEntry
                    {
                        Name = dir.Name,
                        Path = entryRelativePath,
                        Type = "directory"
                    });
                    entryCount++;
                }

                foreach (var file in dirInfo.EnumerateFiles().OrderBy(f => f.Name))
                {
                    if (entryCount >= MaxEntries)
                    {
                        result.HasMoreResults = true;
                        break;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    var fullPath = file.FullName;
                    if (!_pathResolver.TryGetRelativePath(fullPath, solutionDir, out string entryRelativePath))
                        entryRelativePath = fullPath;

                    result.Entries.Add(new DirectoryEntry
                    {
                        Name = file.Name,
                        Path = entryRelativePath,
                        Type = "file"
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
            if (parameters == null) return "Listing directory contents... ";

            var path = parameters.TryGetValue("directory_path", out var p) ? p?.ToString() : "";
            return $"Listing directory '{path}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            var dirResult = (DirectoryContentsResponse)result;
            if (!dirResult.Success)
            {
                return $"Error: {dirResult.ErrorMessage}";
            }
            return $"Listed {dirResult.Entries.Count} entries.";
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
