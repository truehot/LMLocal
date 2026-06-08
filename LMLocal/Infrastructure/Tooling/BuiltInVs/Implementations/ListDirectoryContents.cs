using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations.ListDirectoryContents;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Lists all files and subdirectories within a specified path (relative to solution root or absolute).
    /// Helps navigate the project structure without scanning the entire solution.
    /// Works only within the solution directory.
    /// Returns list of entries with path, name, and type (file or folder).
    /// </summary>
    internal interface IListDirectoryContents : IBuiltInTool
    {
        Task<DirectoryContentsResponse> ExecuteAsync(
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken = default);
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
                Description = $"Lists files and subdirectories within a path. Response fields: success (bool), error_message (string), directory (string), entries (array of {{name (string), path (string), type (string)}}), has_more_entries (bool). has_more_entries indicates more entries exist beyond the {MaxEntries} entry limit. Only works inside solution directory.",
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

        public async Task<DirectoryContentsResponse> ExecuteAsync(
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var directoryPath = ExtractAndValidateParameters(parameters);

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                await _vsDependencies.InitializeAsync();

                string solutionDir = _vsDependencies.GetSolutionDirectory();

                if (!_pathResolver.TryResolveFilePath(directoryPath, solutionDir, out string absolutePath) || string.IsNullOrEmpty(absolutePath))
                    return new DirectoryContentsResponse
                    {
                        Success = false,
                        ErrorMessage = $"Directory not found: {directoryPath}",
                        DirectoryPath = directoryPath,
                        Entries = new List<DirectoryEntry>(),
                        HasMoreResults = false
                    };

                if (!Directory.Exists(absolutePath))
                    return new DirectoryContentsResponse
                    {
                        Success = false,
                        ErrorMessage = $"Directory not found: {absolutePath}",
                        DirectoryPath = directoryPath,
                        Entries = new List<DirectoryEntry>(),
                        HasMoreResults = false
                    };

                if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                    return new DirectoryContentsResponse
                    {
                        Success = false,
                        ErrorMessage = $"Directory '{absolutePath}' is outside the solution directory '{solutionDir}'.",
                        DirectoryPath = directoryPath,
                        Entries = new List<DirectoryEntry>(),
                        HasMoreResults = false
                    };

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

        private string ExtractAndValidateParameters(Dictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("directory_path", out object pathObj) || !(pathObj is string))
                throw new ArgumentException("Parameter 'directory_path' is required and must be a string.", nameof(parameters));

            var directoryPath = (string)pathObj;

            if (string.IsNullOrWhiteSpace(directoryPath))
                directoryPath = ".";

            return directoryPath;
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
