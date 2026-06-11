using LMLocal.Core.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations.SolutionSearch;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Searches for text across Visual Studio solution files.
    /// Performs a case-insensitive substring match on file contents.
    /// </summary>
    internal interface ISolutionSearch : IBuiltInTool
    {
        Task<SearchResultsResponse> ExecuteAsync(
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken = default);
    }

    internal class SolutionSearch : ISolutionSearch
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IVsSolutionFilesScanner _solutionFilesScanner;
        private readonly ISearchResultCache _searchCache;
        private const int DefaultTake = 100;
        private const int MaxFilesToScan = 1500;
        public string ToolName => "Search_Local_Solution_Files";

        public SolutionSearch(
            IVsDependencies vsDependencies,
            IPathResolver pathResolver,
            IVsSolutionFilesScanner solutionFilesScanner,
            ISearchResultCache searchCache)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _solutionFilesScanner = solutionFilesScanner ?? throw new ArgumentNullException(nameof(solutionFilesScanner));
            _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = $"Performs a text search across files in the current Visual Studio solution (case-insensitive substring). Response fields: success (bool), error_message (string), results (array of {{file (string), matches (array of {{line (int), text (string)}}), match_count (int)}}), total_matches (int), total_files (int), next_page_token (string or null). Results are paginated by total number of matches ({DefaultTake} matches per page). If 'next_page_token' is not null, pass it as page_token to get next page. Limited to scanning first {MaxFilesToScan} files.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "text", new ToolDetails { Type = "string", Description = "The plain text to search for (substring match, case-insensitive). Do not use Regular Expressions (Regex), wildcards (like '*', '?')." } },
                        { "extension_filter", new ToolDetails { Type = "string", Description = "File extension filter (e.g., '.cs', '.js'). If not specified, searches all file types. Use it to narrow result set." } },
                        { "project_filter", new ToolDetails { Type = "string", Description = "Project name filter. If specified, only files from projects matching this name (case-insensitive substring match) will be searched. Use it to narrow result set." } },
                        { "page_token", new ToolDetails { Type = "string", Description = "Page token for fetching a specific page of results. Leave empty or null for the first page. Use 'next_page_token' from the response as 'page_token' to get next page of results." } }
                    },
                    Required = new List<string> { "text" }
                }
            };
        }

        public async Task<SearchResultsResponse> ExecuteAsync(
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (searchText, fileExtensions, projectFilter, pageToken) = ExtractAndValidateParametersFromDict(parameters);
                int pageNumber = string.IsNullOrEmpty(pageToken) || !int.TryParse(pageToken, out var pn) ? 0 : pn;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                await _vsDependencies.InitializeAsync();
                string solutionDir = _vsDependencies.GetSolutionDirectory();

                string cacheKey = BuildCacheKey(searchText, fileExtensions, projectFilter);
                if (_searchCache.TryGet(cacheKey, solutionDir, out CachedToolResults<PagedSearchResults> cached))
                {
                    if (pageNumber < cached.AllResults.Count)
                    {
                        var page = cached.AllResults[pageNumber];
                        string nextToken = pageNumber + 1 < cached.AllResults.Count ? (pageNumber + 1).ToString() : null;
                        return new SearchResultsResponse
                        {
                            Results = page.Results,
                            NextPageToken = nextToken,
                            TotalMatches = page.TotalMatches,
                            TotalFiles = page.TotalFiles,
                            Success = true,
                            ErrorMessage = null
                        };
                    }

                    return new SearchResultsResponse
                    {
                        Results = new List<SearchResult>(),
                        NextPageToken = null,
                        TotalMatches = cached.AllResults.FirstOrDefault()?.TotalMatches ?? 0,
                        TotalFiles = cached.AllResults.FirstOrDefault()?.TotalFiles ?? 0,
                        Success = true,
                        ErrorMessage = null
                    };
                }

                var filter = new EnumerateSolutionFilesFilter
                {
                    ExtensionFilter = fileExtensions,
                    ReturnRelative = false,
                    ProjectFilter = projectFilter,
                    Limit = MaxFilesToScan,
                    IncludeProjects = false
                };
                var allFiles = (await _solutionFilesScanner.EnumerateSolutionFilesAsync(filter, cancellationToken)).ToList();

                var response = await Task.Run(() =>
                {
                    var allResults = new List<SearchResult>();

                    foreach (var absolutePath in allFiles)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            if (!_pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath))
                                relativePath = absolutePath;

                            var matches = new List<SearchMatch>();
                            int lineNumber = 0;

                            foreach (var line in File.ReadLines(absolutePath))
                            {
                                lineNumber++;
                                int column = line.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
                                if (column >= 0)
                                {
                                    matches.Add(new SearchMatch
                                    {
                                        LineNumber = lineNumber,
                                        LineText = line.Trim()
                                    });
                                }
                            }

                            if (matches.Count > 0)
                            {
                                allResults.Add(new SearchResult
                                {
                                    FilePath = relativePath,
                                    Matches = matches,
                                    MatchCount = matches.Count
                                });
                            }
                        }
                        catch (FileNotFoundException)
                        {
                            InternalLogger.Warn($"SearchInSolution: file not found: '{absolutePath}'");
                        }
                        catch (IOException ex)
                        {
                            InternalLogger.Warn($"SearchInSolution: IO error: {ex.Message}");
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            InternalLogger.Warn($"SearchInSolution: access denied: {ex.Message}");
                        }
                    }

                    allResults.Sort((a, b) => b.MatchCount.CompareTo(a.MatchCount));

                    var pages = PaginateByMatches(allResults, DefaultTake);
                    int totalMatches = allResults.Sum(r => r.MatchCount);

                    var cacheEntry = new CachedToolResults<PagedSearchResults>
                    {
                        AllResults = pages,
                        ItemsScanned = allFiles.Count
                    };
                    _searchCache.Set(cacheKey, solutionDir, cacheEntry);

                    if (pages.Count > 0)
                    {
                        var firstPage = pages[0];
                        string nextToken = pages.Count > 1 ? "1" : null;
                        return new SearchResultsResponse
                        {
                            Results = firstPage.Results,
                            NextPageToken = nextToken,
                            TotalMatches = totalMatches,
                            TotalFiles = firstPage.TotalFiles,
                            Success = true,
                            ErrorMessage = null
                        };
                    }

                    return new SearchResultsResponse
                    {
                        Results = new List<SearchResult>(),
                        NextPageToken = null,
                        TotalMatches = 0,
                        TotalFiles = 0,
                        Success = true,
                        ErrorMessage = null
                    };

                }, cancellationToken).ConfigureAwait(false);

                return response;
            }
            catch (Exception ex)
            {
                return ErrorResponse(ex.Message);
            }
        }

        private static SearchResultsResponse ErrorResponse(string message)
        {
            return new SearchResultsResponse
            {
                Success = false,
                ErrorMessage = message,
                Results = new List<SearchResult>(),
                NextPageToken = null,
                TotalMatches = 0,
                TotalFiles = 0
            };
        }

        private static List<PagedSearchResults> PaginateByMatches(List<SearchResult> allResults, int matchesPerPage)
        {
            var pages = new List<PagedSearchResults>();
            var currentPage = new List<SearchResult>();
            int matchCount = 0;
            int totalMatches = allResults.Sum(r => r.MatchCount);
            int totalFiles = allResults.Count;

            foreach (var result in allResults)
            {
                int remainingSpaceOnPage = matchesPerPage - matchCount;

                if (result.MatchCount <= remainingSpaceOnPage)
                {
                    currentPage.Add(result);
                    matchCount += result.MatchCount;

                    if (matchCount == matchesPerPage)
                    {
                        pages.Add(new PagedSearchResults
                        {
                            Results = currentPage,
                            TotalMatches = totalMatches,
                            TotalFiles = totalFiles
                        });
                        currentPage = new List<SearchResult>();
                        matchCount = 0;
                    }
                }
                else
                {
                    int matchOffset = 0;

                    while (matchOffset < result.Matches.Count)
                    {
                        int spaceAvailableOnPage = matchesPerPage - matchCount;
                        int chunkSize = Math.Min(spaceAvailableOnPage, result.Matches.Count - matchOffset);

                        var chunk = new SearchResult
                        {
                            FilePath = result.FilePath,
                            Matches = result.Matches.Skip(matchOffset).Take(chunkSize).ToList(),
                            MatchCount = chunkSize
                        };

                        currentPage.Add(chunk);
                        matchCount += chunkSize;
                        matchOffset += chunkSize;

                        if (matchCount == matchesPerPage)
                        {
                            pages.Add(new PagedSearchResults
                            {
                                Results = currentPage,
                                TotalMatches = totalMatches,
                                TotalFiles = totalFiles
                            });
                            currentPage = new List<SearchResult>();
                            matchCount = 0;
                        }
                    }
                }
            }

            if (currentPage.Count > 0)
            {
                pages.Add(new PagedSearchResults
                {
                    Results = currentPage,
                    TotalMatches = totalMatches,
                    TotalFiles = totalFiles
                });
            }

            return pages;
        }

        private string BuildCacheKey(string text, string extensionFilter, string projectFilter)
        {
            var ext = extensionFilter ?? string.Empty;
            var proj = projectFilter ?? string.Empty;
            var txt = text ?? string.Empty;
            return $"{txt}||{ext}||{proj}";
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            if (parameters == null) return "Searching... ";

            var text = parameters.TryGetValue("text", out var q) ? q?.ToString() : "";
            var ext = parameters.TryGetValue("extension_filter", out var e) ? e?.ToString() : null;
            var project = parameters.TryGetValue("project_filter", out var p) ? p?.ToString() : null;
            var pageToken = parameters.TryGetValue("page_token", out var t) ? t?.ToString() : null;

            var message = $"Searching for '{text}'";
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

            if (!string.IsNullOrEmpty(pageToken))
                message += $" (page {pageToken})";

            message += "... ";
            return message;
        }

        public string GetCompletionMessage(object result)
        {
            var searchResults = (SearchResultsResponse)result;
            if (!searchResults.Success)
            {
                return $"Error: {searchResults.ErrorMessage}";
            }
            int pageMatches = searchResults.Results.Sum(r => r.MatchCount);
            return $"Found {pageMatches} matches in {searchResults.Results.Count} files (total: {searchResults.TotalMatches} matches in {searchResults.TotalFiles} files).";
        }

        private (string searchText, string fileExtensions, string projectFilter, string pageToken) ExtractAndValidateParametersFromDict(
            Dictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("text", out object textObj) || !(textObj is string))
                throw new ArgumentException("Parameter 'text' is required and must be a string.", nameof(parameters));

            var searchText = (string)textObj;
            var fileExtensions = parameters.TryGetValue("extension_filter", out object extObj) ? extObj as string : null;
            var projectFilter = parameters.TryGetValue("project_filter", out object projObj) ? projObj as string : null;
            var pageToken = parameters.TryGetValue("page_token", out object tokenObj) ? tokenObj as string : null;

            return (searchText, fileExtensions, projectFilter, pageToken);
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

            [JsonProperty("next_page_token")]
            public string NextPageToken { get; set; }

            [JsonProperty("total_matches")]
            public int TotalMatches { get; set; }

            [JsonProperty("total_files")]
            public int TotalFiles { get; set; }

            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("error_message")]
            public string ErrorMessage { get; set; }
        }

        public class PagedSearchResults
        {
            public List<SearchResult> Results { get; set; }
            public int TotalMatches { get; set; }
            public int TotalFiles { get; set; }
        }
    }
}
