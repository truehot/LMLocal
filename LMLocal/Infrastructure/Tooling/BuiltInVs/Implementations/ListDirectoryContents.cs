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
        Task<object> ExecuteAsync(
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
                Description = $"Lists all files and subdirectories within a specified path. Path should be relative to solution root or absolute. Returns entries with name, full path, and type (file or directory). Only works within solution directory. Limited to first {MaxEntries} entries.",
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

        public async Task<object> ExecuteAsync(
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken = default)
        {
            var directoryPath = ExtractAndValidateParameters(parameters);

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            await _vsDependencies.InitializeAsync();

            string solutionDir = _vsDependencies.GetSolutionDirectory();

            if (!_pathResolver.TryResolveFilePath(directoryPath, solutionDir, out string absolutePath) || string.IsNullOrEmpty(absolutePath))
                throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");

            if (!Directory.Exists(absolutePath))
                throw new DirectoryNotFoundException($"Directory not found: {absolutePath}");

            if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                throw new ArgumentException($"Directory '{absolutePath}' is outside the solution directory '{solutionDir}'.");

            if (!_pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath))
                relativePath = absolutePath;

            if (string.IsNullOrEmpty(relativePath))
                relativePath = ".";

            var result = await Task.Run(() => EnumerateDirectoryContents(absolutePath, solutionDir, cancellationToken), cancellationToken);

            result.DirectoryPath = relativePath;
            return result;
        }

        private DirectoryContentsResponse EnumerateDirectoryContents(string absolutePath, string solutionDir, CancellationToken cancellationToken)
        {
            var result = new DirectoryContentsResponse
            {
                DirectoryPath = "",
                Entries = new List<DirectoryEntry>()
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
                        result.HasMoreEntries = true;
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
                        result.HasMoreEntries = true;
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
                throw new ArgumentException($"Access denied to directory '{absolutePath}': {ex.Message}", nameof(absolutePath), ex);
            }
            catch (IOException ex)
            {
                throw new ArgumentException($"Error reading directory '{absolutePath}': {ex.Message}", nameof(absolutePath), ex);
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

            [JsonProperty("has_more_entries")]
            public bool HasMoreEntries { get; set; }
        }
    }
}
