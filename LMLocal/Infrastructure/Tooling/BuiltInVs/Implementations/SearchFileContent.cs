using LMLocal.Core.Models;
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
        private const string CacheVersion = "sig4";

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
                Description = "Use this tool whenever the user asks to search for text, literal text, a substring, an occurrence, a match, an identifier, or content inside files. This is the required tool for content searches, even when the search string looks like a file name or path. Searches file contents only in files that belong to the currently loaded Visual Studio solution. It does not search arbitrary files located in the solution directory or its subdirectories unless those files are included in the solution. Uses a case-insensitive literal substring match: pass one literal term per call; OR, wildcards, regular expressions and phrase quotes are not search syntax. If the literal text has no matches but looks like 'A OR B' or is wrapped in quotes, the tool also searches the split/unquoted terms and explains this in 'note'. Use for plain-text or identifier searches when you need matching lines and file locations. It does not search file names; use find_files for that. Supports optional extension and project filters. Use group_by to control result grouping: 'file' (default) groups matches by file, with each matching line and its line number; 'text' groups identical matching text across the searched solution files, showing each distinct line once with its total occurrence count and all file:line locations. Both modes include line numbers; 'text' only collapses repeated lines, reducing duplicate results and pagination. Prefer 'text' for broad or exploratory searches, or when many identical/repeated matches are expected. Results are paginated; the response includes 'next_page_token' when more results exist and omits/nulls it when exhausted. If 'next_page_token' is present, call this tool again with 'page_token' set to retrieve the remaining results before finalizing an answer that claims completeness. Default max_results is 25 (max 500); for exhaustive searches or searches where relevant results may be buried among many matches, use a higher max_results value (e.g. 100-200) rather than relying on the default. The search scans at most 1500 solution files; when this limit is reached, the response 'note' says the search was limited, and you should narrow it with extension_filter and/or project_filter. Lines are 1-indexed. Safe to call several times in parallel.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "text", new ToolDetails { Type = "string", Description = "The exact plain-text substring to search for inside file contents. When the user requests a literal search, pass the requested text exactly as written, without replacing placeholders, generating values, normalizing characters, or otherwise modifying it. Matching is case-insensitive." } },
                        { "group_by", new ToolDetails { Type = "string", Description = "'file' (default) groups matches by file. Use 'text' to collapse identical matching lines, reducing duplicate results and pagination; each distinct line still includes all file:line locations." } },
                        { "extension_filter", new ToolDetails { Type = "string", Description = "Exact file extension to narrow the search, as a plain dot-prefixed string with NO wildcard characters — e.g. '.cs', '.csproj', '.js'. Do NOT use glob patterns: pass '.cs', never '*.cs'. If omitted, searches all file types." } },
                        { "project_filter", new ToolDetails { Type = "string", Description = "Substring filter applied case-insensitively to project/path components. It is not an exact full project-name match; for example, 'Tests' can match a project such as 'LMLocal.Tests.E2E' when that text occurs in a relevant path component." } },
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
                        return BuildTextPageResponse(cachedText.AllResults, pageNumber, cachedText.Note);
                }
                else if (_searchCache.TryGet(cacheKey, solutionDir, out CachedToolResults<FileSearchPage> cachedFile))
                {
                    return BuildFilePageResponse(cachedFile.AllResults, pageNumber, cachedFile.Note);
                }

                var filter = new EnumerateSolutionFilesFilter
                {
                    ExtensionFilter = fileExtensions,
                    ReturnRelative = false,
                    ProjectFilter = projectFilter,
                    Limit = MaxFilesToScan + 1, // one extra item detects that the cap was hit
                    IncludeProjects = true
                };

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                var allFiles = (await _solutionFilesScanner.EnumerateSolutionFilesAsync(filter, cancellationToken)).ToList();
                bool scanLimitReached = TrimToLimit(allFiles, MaxFilesToScan);
                if (scanLimitReached)
                    InternalLogger.Warn($"SearchInSolution: scan limit of {MaxFilesToScan} files reached for '{searchText}'.");

                await TaskScheduler.Default;

                var literalResults = new List<SearchResult>();
                var fallbackResults = new List<SearchResult>();
                bool isIdentifierQuery = QueryClassifier.IsIdentifierQuery(searchText);
                bool needRanking = grouping == SearchGrouping.File;

                var fallback = SearchQueryFallback.Create(searchText);
                var fallbackIsIdentifier = fallback.Terms.ToDictionary(t => t, QueryClassifier.IsIdentifierQuery, StringComparer.OrdinalIgnoreCase);

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
                        var literal = new FileMatchAccumulator(needRanking);
                        var fallbackAcc = fallback.HasTerms ? new FileMatchAccumulator(needRanking) : null;
                        string extension = Path.GetExtension(absolutePath);

                        await _fileSystem.ReadLinesAsync(absolutePath, (lineNumber, line) =>
                        {
                            var m = ContentSearchMatcher.Match(line, searchText, extension, needRanking && isIdentifierQuery, needRanking);
                            if (m.IsMatch)
                                literal.Add(lineNumber, line, m, null);

                            if (fallbackAcc == null || literalResults.Count > 0 || literal.Count > 0)
                                return;

                            string term = fallback.FindTerm(line);
                            if (term == null)
                                return;

                            var fm = ContentSearchMatcher.Match(line, term, extension, needRanking && fallbackIsIdentifier[term], needRanking);
                            if (fm.IsMatch)
                                fallbackAcc.Add(lineNumber, line, fm, term);
                        }, cancellationToken).ConfigureAwait(false);

                        if (literal.Count > 0 || (fallbackAcc != null && fallbackAcc.Count > 0))
                        {
                            if (!_pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath))
                                relativePath = absolutePath;

                            if (literal.Count > 0)
                                literalResults.Add(literal.ToResult(relativePath));
                            else if (literalResults.Count == 0)
                                fallbackResults.Add(fallbackAcc.ToResult(relativePath));
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

                List<SearchResult> allResults = literalResults;
                string note = null;
                if (literalResults.Count == 0 && fallback.HasTerms)
                {
                    allResults = fallbackResults;
                    note = fallback.BuildNote(fallbackResults.Sum(r => r.MatchCount));
                }

                if (scanLimitReached)
                {
                    string limitNote = $"The search was limited: the scan limit of {MaxFilesToScan} files was reached, so only the first {MaxFilesToScan} solution files were searched and matches in the remaining files are missing. A missing match is not proof of absence. Narrow the search with extension_filter and/or project_filter.";
                    note = string.IsNullOrEmpty(note) ? limitNote : note + " " + limitNote;
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
                        ItemsScanned = allFiles.Count,
                        Note = note
                    });

                    return BuildTextPageResponse(textPages, 0, note);
                }
                else
                {
                    var pages = SearchResultPaginator.PaginateByMatches(allResults, pageSize);

                    _searchCache.Set(cacheKey, solutionDir, new CachedToolResults<FileSearchPage>
                    {
                        AllResults = pages,
                        ItemsScanned = allFiles.Count,
                        Note = note
                    });

                    return BuildFilePageResponse(pages, 0, note);
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

        private static SearchResultsResponse BuildFilePageResponse(IReadOnlyList<FileSearchPage> pages, int pageNumber, string note)
        {
            if (pageNumber >= 0 && pageNumber < pages.Count)
            {
                var page = pages[pageNumber];
                string nextToken = pageNumber + 1 < pages.Count ? (pageNumber + 1).ToString() : null;
                return new SearchResultsResponse
                {
                    Results = page.Results,
                    Note = note,
                    NextPageToken = nextToken,
                    TotalMatches = page.TotalMatches,
                    TotalFiles = page.TotalFiles,
                    Success = true
                };
            }

            return new SearchResultsResponse
            {
                Results = new List<SearchResult>(),
                Note = note,
                NextPageToken = null,
                TotalMatches = pages.Count > 0 ? pages[0].TotalMatches : 0,
                TotalFiles = pages.Count > 0 ? pages[0].TotalFiles : 0,
                Success = true
            };
        }

        private static SearchTextGroupResponse BuildTextPageResponse(IReadOnlyList<TextSearchPage> pages, int pageNumber, string note)
        {
            if (pageNumber >= 0 && pageNumber < pages.Count)
            {
                var page = pages[pageNumber];
                string nextToken = pageNumber + 1 < pages.Count ? (pageNumber + 1).ToString() : null;
                return new SearchTextGroupResponse
                {
                    Results = page.Results,
                    Note = note,
                    NextPageToken = nextToken,
                    TotalMatches = page.TotalMatches,
                    TotalFiles = page.TotalFiles,
                    Success = true
                };
            }

            return new SearchTextGroupResponse
            {
                Results = new List<SearchTextGroupResult>(),
                Note = note,
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

        /// <summary>Collects the matches of one file and the ranking inputs for its score.</summary>
        private sealed class FileMatchAccumulator
        {
            private readonly bool _needRanking;
            private readonly List<SearchMatch> _matches = new List<SearchMatch>();
            private int _exactWordCount;
            private int _declarationWeightSum;

            public FileMatchAccumulator(bool needRanking)
            {
                _needRanking = needRanking;
            }

            public int Count => _matches.Count;

            public void Add(int lineNumber, string line, LineMatch m, string matchedTerm)
            {
                var match = new SearchMatch
                {
                    LineNumber = lineNumber,
                    LineText = line.Trim(),
                    MatchedTerm = matchedTerm
                };

                if (_needRanking)
                {
                    match.IsExactWord = m.IsExactWord;
                    match.DeclarationKind = m.Kind == SearchMatchKind.Other ? null : m.Kind.ToString();

                    if (m.IsExactWord)
                        _exactWordCount++;
                    if (m.Kind != SearchMatchKind.Other)
                        _declarationWeightSum += DeclarationWeights.WeightOf(m.Kind);
                }

                _matches.Add(match);
            }

            public SearchResult ToResult(string relativePath)
            {
                return new SearchResult
                {
                    FilePath = relativePath,
                    Matches = _matches,
                    MatchCount = _matches.Count,
                    Score = _needRanking ? ComputeScore(_matches.Count, _exactWordCount, _declarationWeightSum) : 0
                };
            }
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
