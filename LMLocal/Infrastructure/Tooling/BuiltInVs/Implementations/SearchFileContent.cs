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
        private const string CacheVersion = "sig3";

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
                Description = "Searches file contents within the current Visual Studio solution for a case-insensitive literal substring match. Use for plain-text or identifier searches when you need matching lines and file locations. It does not search file names; use find_files for that. Supports optional extension and project filters. Use group_by to control result grouping: 'file' (default) groups matches by file, with each matching line and its line number; 'text' groups identical matching text across the searched solution files, showing each distinct line once with its total occurrence count and all file:line locations. Both modes include line numbers; 'text' only collapses repeated lines, reducing duplicate results and pagination. Prefer 'text' for broad or exploratory searches, or when many identical/repeated matches are expected. Results are paginated; the response includes 'next_page_token' when more results exist and omits/nulls it when exhausted. If 'next_page_token' is present, call this tool again with 'page_token' set to retrieve the remaining results before finalizing an answer that claims completeness. Default max_results is 25 (max 500); for exhaustive searches or searches where relevant results may be buried among many matches, use a higher max_results value (e.g. 100-200) rather than relying on the default. The search is limited to the first 1500 files in the solution. Lines are 1-indexed.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "text", new ToolDetails { Type = "string", Description = "The plain text substring to search for (case-insensitive) inside file contents." } },
                        { "group_by", new ToolDetails { Type = "string", Description = "'file' (default) groups matches by file. Use 'text' to collapse identical matching lines, reducing duplicate results and pagination; each distinct line still includes all file:line locations." } },
                        { "extension_filter", new ToolDetails { Type = "string", Description = "Use this to narrow the search by file extension (e.g., '.cs', '.js'). If omitted, searches all file types." } },
                        { "project_filter", new ToolDetails { Type = "string", Description = "Use this to narrow result set. If specified, only files from projects matching this name (case-insensitive substring match) will be searched." } },
                        { "page_token", new ToolDetails { Type = "string", Description = "Pagination token for fetching the next page of results. Use 'next_page_token' from a previous response to continue searching." } },
                        { "max_results", new ToolDetails { Type = "integer", Description = "Number of results to return per page. Default 25, max 500. Use a larger value for broad or complete searches to reduce pagination." } }
                    },
                    Required = new List<string> { "text" }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            SearchGrouping grouping = SearchGrouping.File;
            try
            {
                var (searchText, fileExtensions, projectFilter, pageToken, pageSize, parsedGrouping, error) = ExtractAndValidateParameters(parameters);
                grouping = parsedGrouping;
                if (error != null)
                    return Error(grouping, error);

                int pageNumber = string.IsNullOrEmpty(pageToken) || !int.TryParse(pageToken, out var pn) ? 0 : Math.Max(0, pn);

                if (!_vsDependencies.IsSolutionOpen)
                    return Error(grouping, "No solution is currently open.");

                string solutionDir = _vsDependencies.GetSolutionDirectory();

                string cacheKey = BuildCacheKey(searchText, fileExtensions, projectFilter, grouping);
                if (grouping == SearchGrouping.Text)
                {
                    if (_searchCache.TryGet(cacheKey, solutionDir, out CachedToolResults<TextSearchPage> cachedText))
                        return BuildTextPageResponse(cachedText.AllResults, pageNumber);
                }
                else if (_searchCache.TryGet(cacheKey, solutionDir, out CachedToolResults<FileSearchPage> cachedFile))
                {
                    return BuildFilePageResponse(cachedFile.AllResults, pageNumber);
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
                bool needRanking = grouping == SearchGrouping.File;

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
                            var m = ContentSearchMatcher.Match(line, searchText, extension, needRanking && isIdentifierQuery, needRanking);
                            if (!m.IsMatch)
                                return;

                            var match = new SearchMatch
                            {
                                LineNumber = lineNumber,
                                LineText = line.Trim()
                            };

                            if (needRanking)
                            {
                                match.IsExactWord = m.IsExactWord;
                                match.DeclarationKind = m.Kind == SearchMatchKind.Other ? null : m.Kind.ToString();

                                if (m.IsExactWord)
                                    exactWordCount++;
                                if (m.Kind != SearchMatchKind.Other)
                                    declarationWeightSum += DeclarationWeights.WeightOf(m.Kind);
                            }

                            matches.Add(match);
                        }, cancellationToken).ConfigureAwait(false);

                        if (matches.Count > 0)
                        {
                            if (!_pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath))
                                relativePath = absolutePath;

                            allResults.Add(new SearchResult
                            {
                                FilePath = relativePath,
                                Matches = matches,
                                MatchCount = matches.Count,
                                Score = needRanking ? ComputeScore(matches.Count, exactWordCount, declarationWeightSum) : 0
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

                if (needRanking)
                {
                    allResults.Sort((a, b) =>
                    {
                        int c = b.Score.CompareTo(a.Score);
                        if (c != 0) return c;
                        c = b.MatchCount.CompareTo(a.MatchCount);
                        if (c != 0) return c;
                        return string.CompareOrdinal(a.FilePath, b.FilePath);
                    });
                }

                if (grouping == SearchGrouping.Text)
                {
                    var groups = SearchResultGrouper.GroupByText(allResults);
                    var textPages = SearchResultPaginator.PaginateByGroups(groups, pageSize);

                    _searchCache.Set(cacheKey, solutionDir, new CachedToolResults<TextSearchPage>
                    {
                        AllResults = textPages,
                        ItemsScanned = allFiles.Count
                    });

                    return BuildTextPageResponse(textPages, 0);
                }
                else
                {
                    var pages = SearchResultPaginator.PaginateByMatches(allResults, pageSize);

                    _searchCache.Set(cacheKey, solutionDir, new CachedToolResults<FileSearchPage>
                    {
                        AllResults = pages,
                        ItemsScanned = allFiles.Count
                    });

                    return BuildFilePageResponse(pages, 0);
                }
            }
            catch (OperationCanceledException)
            {
                return Error(grouping, "Operation was cancelled.");
            }
            catch (Exception ex)
            {
                return Error(grouping, ex.Message);
            }
        }

        private static object Error(SearchGrouping grouping, string message)
        {
            if (grouping == SearchGrouping.Text)
            {
                return new SearchTextGroupResponse
                {
                    Success = false,
                    ErrorMessage = message,
                    Results = new List<SearchTextGroupResult>(),
                    NextPageToken = null,
                    TotalMatches = 0,
                    TotalFiles = 0
                };
            }

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

        private static SearchResultsResponse BuildFilePageResponse(IReadOnlyList<FileSearchPage> pages, int pageNumber)
        {
            if (pageNumber >= 0 && pageNumber < pages.Count)
            {
                var page = pages[pageNumber];
                string nextToken = pageNumber + 1 < pages.Count ? (pageNumber + 1).ToString() : null;
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
                TotalMatches = pages.Count > 0 ? pages[0].TotalMatches : 0,
                TotalFiles = pages.Count > 0 ? pages[0].TotalFiles : 0,
                Success = true
            };
        }

        private static SearchTextGroupResponse BuildTextPageResponse(IReadOnlyList<TextSearchPage> pages, int pageNumber)
        {
            if (pageNumber >= 0 && pageNumber < pages.Count)
            {
                var page = pages[pageNumber];
                string nextToken = pageNumber + 1 < pages.Count ? (pageNumber + 1).ToString() : null;
                return new SearchTextGroupResponse
                {
                    Results = page.Results,
                    NextPageToken = nextToken,
                    TotalMatches = page.TotalMatches,
                    TotalFiles = page.TotalFiles,
                    Success = true
                };
            }

            return new SearchTextGroupResponse
            {
                Results = new List<SearchTextGroupResult>(),
                NextPageToken = null,
                TotalMatches = pages.Count > 0 ? pages[0].TotalMatches : 0,
                TotalFiles = pages.Count > 0 ? pages[0].TotalFiles : 0,
                Success = true
            };
        }

        private string BuildCacheKey(string text, string extensionFilter, string projectFilter, SearchGrouping grouping)
        {
            var ext = extensionFilter ?? string.Empty;
            var proj = projectFilter ?? string.Empty;
            var txt = text ?? string.Empty;

            return $"{txt}||{ext}||{proj}||{grouping}||{CacheVersion}";
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            if (parameters == null) return "Searching... ";

            var text = parameters.TryGetValue("text", out var q) ? q?.ToString() : "";
            var ext = parameters.TryGetValue("extension_filter", out var e) ? e?.ToString() : null;
            var project = parameters.TryGetValue("project_filter", out var p) ? p?.ToString() : null;
            var pageToken = parameters.TryGetValue("page_token", out var t) ? t?.ToString() : null;
            var groupBy = parameters.TryGetValue("group_by", out var g) ? g?.ToString() : null;

            var message = $"Searching for '{text}'";
            if (!string.IsNullOrEmpty(project))
                message += $" in '{project}'";

            if (!string.IsNullOrEmpty(ext))
                message += $", with extension '{ext}'";
            else
                message += " in all files";

            if (string.Equals(groupBy, "text", StringComparison.OrdinalIgnoreCase))
                message += ", grouped by text";

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
                return FormatCompletionMessage(pageMatches, searchResults.TotalMatches);
            }

            if (result is SearchTextGroupResponse textResults)
            {
                if (!textResults.Success)
                    return $"Searching failed: {textResults.ErrorMessage}";

                int pageMatches = textResults.Results.Sum(r => r.OccurrenceCount);
                return FormatCompletionMessage(pageMatches, textResults.TotalMatches);
            }

            return "Search finished.";
        }

        private static string FormatCompletionMessage(int pageMatches, int totalMatches)
        {
            var message = pageMatches == 0
                ? "Found no matches"
                : $"Found {pageMatches} {Pluralizer.Pluralize(pageMatches, "match", "matches")}";
            if (totalMatches > 0 && pageMatches < totalMatches)
                message += $" (total: {totalMatches} {Pluralizer.Pluralize(totalMatches, "match", "matches")})";
            message += ".";
            return message;
        }

        private static int ComputeScore(int matchCount, int exactWordCount, int declarationWeightSum)
        {
            return matchCount + exactWordCount * DeclarationWeights.ExactWordBonus + declarationWeightSum;
        }

        private (string searchText, string fileExtensions, string projectFilter, string pageToken, int pageSize, SearchGrouping grouping, string error) ExtractAndValidateParameters(
            Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, null, null, null, DefaultPageSize, SearchGrouping.File, "Parameters cannot be null.");
            if (!parameters.TryGetValue("text", out object textObj) || !(textObj is string))
                return (null, null, null, null, DefaultPageSize, SearchGrouping.File, "Parameter 'text' is required and must be a string.");

            var searchText = (string)textObj;
            var fileExtensions = parameters.TryGetValue("extension_filter", out object extObj) ? extObj as string : null;
            var projectFilter = parameters.TryGetValue("project_filter", out object projObj) ? projObj as string : null;
            var pageToken = parameters.TryGetValue("page_token", out object tokenObj) ? tokenObj as string : null;

            var grouping = SearchGrouping.File;
            if (parameters.TryGetValue("group_by", out object groupObj) && groupObj is string groupStr &&
                string.Equals(groupStr, "text", StringComparison.OrdinalIgnoreCase))
            {
                grouping = SearchGrouping.Text;
            }

            int pageSize = DefaultPageSize;
            if (parameters.TryGetValue("max_results", out object maxObj) && maxObj != null && int.TryParse(maxObj.ToString(), out int maxVal))
                pageSize = Math.Min(Math.Max(maxVal, 1), MaxPageSize);

            return (searchText, fileExtensions, projectFilter, pageToken, pageSize, grouping, null);
        }

    }
}
