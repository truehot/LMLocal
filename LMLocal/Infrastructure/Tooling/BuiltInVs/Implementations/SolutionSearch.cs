using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Searches for text across Visual Studio solution files.
    /// Performs a case-insensitive substring match on file contents.
    /// </summary>
    internal interface ISolutionSearch : IBuiltInTool
    {
        Task<object> ExecuteAsync(
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken = default);
    }

    internal class SolutionSearch : ISolutionSearch
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IVsSolutionFilesScanner _solutionFilesScanner;
        private const int MaxSearchResults = 50;
        private const int MaxFilesToScan = 1500;
        public string ToolName => "Search_Local_Solution_Files";

        public SolutionSearch(IVsDependencies vsDependencies, IPathResolver pathResolver, IVsSolutionFilesScanner solutionFilesScanner)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _solutionFilesScanner = solutionFilesScanner ?? throw new ArgumentNullException(nameof(solutionFilesScanner));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = $"Performs a text search across Visual Studio solution files. Returns matching lines with file path, line number, column, and text. Search is limited to the first {MaxSearchResults} files that contain matches. Results include has_more_results flag indicating if more matches exist beyond the limit.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "query", new ToolDetails { Type = "string", Description = "The text to search for (substring match, case-insensitive)." } },
                        { "extension_filter", new ToolDetails { Type = "string", Description = "Optional file extension filter (e.g., '.cs', '.js'). If not specified, searches all file types." } },
                        { "project_filter", new ToolDetails { Type = "string", Description = "Optional project name filter. If specified, only files from projects matching this name (case-insensitive substring match) will be searched." } }
                    },
                    Required = new List<string> { "query" }
                }
            };
        }

        public async Task<object> ExecuteAsync(
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken = default)
        {
            var (searchText, fileExtensions, projectFilter) = ExtractAndValidateParametersFromDict(parameters);
            return await ExecuteCoreAsync(searchText, fileExtensions, projectFilter, cancellationToken);
        }

        private async Task<object> ExecuteCoreAsync(
            string searchText,
            string fileExtensions,
            string projectFilter,
            CancellationToken cancellationToken)
        {
            var fileMatches = new Dictionary<string, List<SearchMatch>>();

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            await _vsDependencies.InitializeAsync();

            string solutionDir = _vsDependencies.GetSolutionDirectory();

            var filter = new EnumerateSolutionFilesFilter
            {
                ExtensionFilter = fileExtensions,
                ReturnRelative = false,
                ProjectFilter = projectFilter,
                Limit = MaxFilesToScan,
                IncludeProjects = false
            };
            var allFiles = (await _solutionFilesScanner.EnumerateSolutionFilesAsync(filter)).ToList();

            await Task.Run(() =>
            {
                foreach (var absolutePath in allFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (!_pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath))
                            relativePath = absolutePath;

                        int lineNumber = 0;
                        foreach (var line in File.ReadLines(absolutePath))
                        {
                            lineNumber++;
                            int column = line.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
                            if (column >= 0)
                            {
                                if (!fileMatches.ContainsKey(relativePath))
                                    fileMatches[relativePath] = new List<SearchMatch>();

                                fileMatches[relativePath].Add(new SearchMatch
                                {
                                    LineNumber = lineNumber,
                                    LineText = line.Trim()
                                });
                            }
                        }

                        if (fileMatches.Count >= MaxSearchResults)
                        {
                            break;
                        }
                    }
                    catch (IOException ex)
                    {
                        InternalLogger.Warn($"SearchInSolution: IO error reading file '{absolutePath}': {ex.Message}");
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        InternalLogger.Warn($"SearchInSolution: access denied reading file '{absolutePath}': {ex.Message}");
                    }
                }
            });

            var groupedResults = fileMatches
                .Select(kvp => new SearchResult
                {
                    FilePath = kvp.Key,
                    Matches = kvp.Value,
                    MatchCount = kvp.Value.Count
                })
                .OrderByDescending(r => r.MatchCount)
                .ToList();

            return new SearchResultsResponse
            {
                Results = groupedResults,
                HasMoreResults = groupedResults.Count >= MaxSearchResults,
                SearchFilesLimit = MaxSearchResults
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            if (parameters == null) return "Searching... ";

            var query = parameters.TryGetValue("query", out var q) ? q?.ToString() : "";
            var ext = parameters.TryGetValue("extension_filter", out var e) ? e?.ToString() : null;
            var project = parameters.TryGetValue("project_filter", out var p) ? p?.ToString() : null;

            var message = $"Searching for '{query}'";
            if (!string.IsNullOrEmpty(project))
                message += $" in '{project}'";

            if (!string.IsNullOrEmpty(ext))
            {
                message += $", with extension '{ext}'";
            }
            else
            {
                message += " in all files";
            }

            message += "... ";
            return message;
        }

        public string GetCompletionMessage(object result)
        {
            var searchResults = (SearchResultsResponse)result;
            return $"Found {searchResults.Results.Count} matches.";
        }

        private (string searchText, string fileExtensions, string projectFilter) ExtractAndValidateParametersFromDict(
            Dictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("query", out object queryObj) || !(queryObj is string))
                throw new ArgumentException("Parameter 'query' is required and must be a string.", nameof(parameters));

            var searchText = (string)queryObj;
            var fileExtensions = parameters.TryGetValue("extension_filter", out object extObj) ? extObj as string : null;
            var projectFilter = parameters.TryGetValue("project_filter", out object projObj) ? projObj as string : null;

            return (searchText, fileExtensions, projectFilter);
        }

        public class SearchMatch
        {
            [JsonProperty("line")]
            public int LineNumber { get; set; }

            [JsonProperty("text")]
            public string LineText { get; set; }
        }

        public class SearchResult
        {
            [JsonProperty("file")]
            public string FilePath { get; set; }

            [JsonProperty("matches")]
            public List<SearchMatch> Matches { get; set; }

            [JsonProperty("match_count")]
            public int MatchCount { get; set; }
        }

        public class SearchResultsResponse
        {
            [JsonProperty("results")]
            public List<SearchResult> Results { get; set; }

            [JsonProperty("has_more_results")]
            public bool HasMoreResults { get; set; }

            [JsonProperty("search_files_limit")]
            public int SearchFilesLimit { get; set; }
        }
    }
}
