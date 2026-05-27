using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Common;
using LMLocal.Infrastructure.Tooling.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
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


    /// <summary>
    /// Searches for text across Visual Studio solution files.
    /// Performs a case-insensitive substring match on file contents.
    /// </summary>

    internal interface ISolutionSearchTool : ITool
    {
        Task<object> ExecuteAsync(
            IServiceProvider sp,
            string searchText,
            string fileExtensions = ".cs",
            CancellationToken cancellationToken = default,
            string projectFilter = null);
    }

    internal class SolutionSearchTool : ISolutionSearchTool
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IVsSolutionFilesScanner _solutionFilesScanner;
        private const int MaxSearchResults = 50;
        private const int MaxFilesToScan = 500;
        public string ToolName => "Search_Local_Solution_Files";

        public SolutionSearchTool(IVsDependencies vsDependencies, IPathResolver pathResolver, IVsSolutionFilesScanner solutionFilesScanner)
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
                Description = $"Performs a text search across Visual Studio solution files. Returns matching lines with file path, line number, column, and text. Search is limited to the first {MaxSearchResults} files. Results include has_more_results flag indicating if more matches exist beyond the limit.",
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
            IServiceProvider sp,
            string searchText,
            string fileExtensions = null,
            CancellationToken cancellationToken = default,
            string projectFilter = null)
        {
            if (string.IsNullOrEmpty(searchText))
                return new SearchResultsResponse { Results = new List<SearchResult>(), HasMoreResults = false, SearchFilesLimit = MaxSearchResults };

            var fileMatches = new Dictionary<string, List<SearchMatch>>();

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            await _vsDependencies.InitializeAsync();

            string solutionDir = _vsDependencies.GetSolutionDirectory();

            var filter = new EnumerateSolutionFilesFilter
            {
                ExtensionFilter = fileExtensions,
                ReturnRelative = false,
                ProjectFilter = projectFilter,
                Limit = MaxFilesToScan
            };
            var allFiles = _solutionFilesScanner.EnumerateSolutionFiles(filter).ToList();

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
    }
}
