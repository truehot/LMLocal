using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Common.VsSolutionFilesScanner;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    internal interface ISearchFileContent : IBuiltInTool
    {
    }

    internal class SearchFileContent : ISearchFileContent
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IVsSolutionFilesScanner _solutionFilesScanner;
        private readonly ISearchResultCache _searchCache;
        private readonly IFileSystem _fileSystem;
        private const int DefaultPageSize = 25;
        private const int MaxPageSize = 500;
        private const int MaxFilesToScan = 1500;

        /// <summary>
        /// Version of the search/matching/ranking logic.
        /// </summary>
        private const string CacheVersion = "sig2";

        public string ToolName => "search_file_content";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

        public SearchFileContent(
            IVsDependencies vsDependencies,
            IPathResolver pathResolver,
            IVsSolutionFilesScanner solutionFilesScanner,
            ISearchResultCache searchCache,
            IFileSystem fileSystem)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _solutionFilesScanner = solutionFilesScanner ?? throw new ArgumentNullException(nameof(solutionFilesScanner));
            _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Searches inside file contents for a case-insensitive substring match. Does NOT search file names — use find_files for that. Files are ranked by relevance: declaration lines (class/struct/interface/enum/function/method/property/field) get a boost, and for single-token identifier queries exact whole-word matches score higher. Each match exposes is_exact_word and declaration_kind. Results are paginated by total number of matches. Limited to scanning the first 1500 files in the solution. The search text is plain substring matching.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "text", new ToolDetails { Type = "string", Description = "The plain text to search for (substring match, case-insensitive) inside file contents." } },
                        { "extension_filter", new ToolDetails { Type = "string", Description = "Use it to narrow result set. File extension filter (e.g., '.cs', '.js'). If not specified, searches all file types." } },
                        { "project_filter", new ToolDetails { Type = "string", Description = "Use it to narrow result set. If specified, only files from projects matching this name (case-insensitive substring match) will be searched. " } },
                        { "page_token", new ToolDetails { Type = "string", Description = "Page token for fetching a specific page of results. Leave empty or null for the first page. Use 'next_page_token' from the response as 'page_token' to get next page of results." } },
                        { "max_results", new ToolDetails { Type = "integer", Description = "Number of matches to return per page. Default 25, max 500." } }
                    },
                    Required = new List<string> { "text" }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                var (searchText, fileExtensions, projectFilter, pageToken, pageSize, error) = ExtractAndValidateParameters(parameters);
                if (error != null)
                    return Error(error);

                int pageNumber = string.IsNullOrEmpty(pageToken) || !int.TryParse(pageToken, out var pn) ? 0 : Math.Max(0, pn);

                if (!_vsDependencies.IsSolutionOpen)
                    return Error("No solution is currently open.");

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
                            Success = true
                        };
                    }

                    return new SearchResultsResponse
                    {
                        Results = new List<SearchResult>(),
                        NextPageToken = null,
                        TotalMatches = cached.AllResults.FirstOrDefault()?.TotalMatches ?? 0,
                        TotalFiles = cached.AllResults.FirstOrDefault()?.TotalFiles ?? 0,
                        Success = true
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

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                var allFiles = (await _solutionFilesScanner.EnumerateSolutionFilesAsync(filter, cancellationToken)).ToList();

                await TaskScheduler.Default;

                var allResults = new List<SearchResult>();
                bool isIdentifierQuery = QueryClassifier.IsIdentifierQuery(searchText);

                foreach (var absolutePath in allFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!_fileSystem.FileExists(absolutePath))
                    {
                        InternalLogger.Warn($"SearchInSolution: file not found: '{absolutePath}'");
                        continue;
                    }

                    try
                    {
                        var matches = new List<SearchMatch>();
                        int exactWordCount = 0;
                        int declarationWeightSum = 0;
                        string extension = Path.GetExtension(absolutePath);

                        await _fileSystem.ReadLinesAsync(absolutePath, (lineNumber, line) =>
                        {
                            var m = ContentSearchMatcher.Match(line, searchText, extension, isIdentifierQuery);
                            if (!m.IsMatch)
                                return;

                            matches.Add(new SearchMatch
                            {
                                LineNumber = lineNumber,
                                LineText = line.Trim(),
                                IsExactWord = m.IsExactWord,
                                DeclarationKind = m.Kind == SearchMatchKind.Other ? null : m.Kind.ToString()
                            });

                            if (m.IsExactWord)
                                exactWordCount++;
                            if (m.Kind != SearchMatchKind.Other)
                                declarationWeightSum += DeclarationWeights.WeightOf(m.Kind);
                        }, cancellationToken).ConfigureAwait(false);

                        if (matches.Count > 0)
                        {
                            if (!_pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath))
                                relativePath = absolutePath;

                            int declarationCount = 0;
                            for (int i = 0; i < matches.Count; i++)
                            {
                                if (matches[i].DeclarationKind != null)
                                    declarationCount++;
                            }

                            allResults.Add(new SearchResult
                            {
                                FilePath = relativePath,
                                Matches = matches,
                                MatchCount = matches.Count,
                                Score = ComputeScore(matches.Count, exactWordCount, declarationWeightSum),
                                ExactWordCount = exactWordCount,
                                DeclarationCount = declarationCount > 0 ? (int?)declarationCount : null
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Warn($"SearchInSolution: error reading '{absolutePath}': {ex.Message}");
                    }
                }

                allResults.Sort((a, b) =>
                {
                    int c = b.Score.CompareTo(a.Score);
                    if (c != 0) return c;
                    c = b.MatchCount.CompareTo(a.MatchCount);
                    if (c != 0) return c;
                    return string.CompareOrdinal(a.FilePath, b.FilePath);
                });

                var pages = PaginateByMatches(allResults, pageSize);
                int totalMatches = allResults.Sum(r => r.MatchCount);
                int totalFiles = allResults.Count;

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
                        TotalFiles = totalFiles,
                        Success = true
                    };
                }

                return new SearchResultsResponse
                {
                    Results = new List<SearchResult>(),
                    NextPageToken = null,
                    TotalMatches = 0,
                    TotalFiles = 0,
                    Success = true
                };
            }
            catch (OperationCanceledException)
            {
                return Error("Operation was cancelled.");
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        }

        private static SearchResultsResponse Error(string message)
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
                            MatchCount = chunkSize,
                            Score = result.Score,
                            ExactWordCount = result.ExactWordCount,
                            DeclarationCount = result.DeclarationCount
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

            return $"{txt}||{ext}||{proj}||{CacheVersion}";
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
                message += $", with extension '{ext}'";
            else
                message += " in all files";

            if (!string.IsNullOrEmpty(pageToken) && int.TryParse(pageToken, out var pageTokenValue) && pageTokenValue > 0)
                message += $" (page {++pageTokenValue})";

            message += "... ";
            return message;
        }

        public string GetCompletionMessage(object result)
        {
            if (result is SearchResultsResponse searchResults)
            {
                if (!searchResults.Success)
                    return $"Searching failed: {searchResults.ErrorMessage}";

                int pageMatches = searchResults.Results.Sum(r => r.MatchCount);
                var message = pageMatches == 0
                    ? "Found no matches"
                    : $"Found {pageMatches} {Pluralizer.Pluralize(pageMatches, "match", "matches")}";
                if (searchResults.TotalMatches > 0 && pageMatches < searchResults.TotalMatches)
                    message += $" (total: {searchResults.TotalMatches} {Pluralizer.Pluralize(searchResults.TotalMatches, "match", "matches")})";
                message += ".";
                return message;
            }
            return "Search finished.";
        }

        private static int ComputeScore(int matchCount, int exactWordCount, int declarationWeightSum)
        {
            return matchCount + exactWordCount * DeclarationWeights.ExactWordBonus + declarationWeightSum;
        }

        private (string searchText, string fileExtensions, string projectFilter, string pageToken, int pageSize, string error) ExtractAndValidateParameters(
            Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, null, null, null, DefaultPageSize, "Parameters cannot be null.");
            if (!parameters.TryGetValue("text", out object textObj) || !(textObj is string))
                return (null, null, null, null, DefaultPageSize, "Parameter 'text' is required and must be a string.");

            var searchText = (string)textObj;
            var fileExtensions = parameters.TryGetValue("extension_filter", out object extObj) ? extObj as string : null;
            var projectFilter = parameters.TryGetValue("project_filter", out object projObj) ? projObj as string : null;
            var pageToken = parameters.TryGetValue("page_token", out object tokenObj) ? tokenObj as string : null;

            int pageSize = DefaultPageSize;
            if (parameters.TryGetValue("max_results", out object maxObj) && maxObj != null && int.TryParse(maxObj.ToString(), out int maxVal))
                pageSize = Math.Min(Math.Max(maxVal, 1), MaxPageSize);

            return (searchText, fileExtensions, projectFilter, pageToken, pageSize, null);
        }

        public class SearchMatch
        {
            [JsonProperty("line")]
            public int LineNumber { get; set; }

            [JsonProperty("text")]
            public string LineText { get; set; }

            [JsonProperty("is_exact_word")]
            public bool IsExactWord { get; set; }

            [JsonProperty("declaration_kind", NullValueHandling = NullValueHandling.Ignore)]
            public string DeclarationKind { get; set; }
        }

        public class SearchResult
        {
            [JsonProperty("file_path")]
            public string FilePath { get; set; }

            [JsonProperty("matches")]
            public List<SearchMatch> Matches { get; set; }

            [JsonProperty("match_count")]
            public int MatchCount { get; set; }

            [JsonProperty("score")]
            public int Score { get; set; }

            [JsonProperty("exact_word_count")]
            public int ExactWordCount { get; set; }

            [JsonProperty("declaration_count", NullValueHandling = NullValueHandling.Ignore)]
            public int? DeclarationCount { get; set; }
        }

        public class SearchResultsResponse
        {
            [JsonProperty("results")]
            public List<SearchResult> Results { get; set; }

            [JsonProperty("next_page_token", NullValueHandling = NullValueHandling.Ignore)]
            public string NextPageToken { get; set; }

            [JsonProperty("total_matches")]
            public int TotalMatches { get; set; }

            [JsonProperty("total_files")]
            public int TotalFiles { get; set; }

            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
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
