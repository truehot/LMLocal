using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Projects;
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
        private readonly SearchScopeResolver _scopeResolver;
        private const int DefaultPageSize = 25;
        private const int MaxPageSize = 500;
        private const int MaxFilesToScan = 1500;

        /// <summary>
        /// Version of the search/matching/ranking logic.
        /// </summary>
        private const string CacheVersion = "sig5";

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
            _scopeResolver = new SearchScopeResolver(_pathResolver, _fileSystem);
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Use this tool whenever the user asks to search for text, literal text, a substring, an occurrence, a match, an identifier, or content inside files. This is the required tool for content searches, even when the search string looks like a file name or path. Without 'path', searches file contents only in files that belong to the currently loaded Visual Studio solution; files in the solution directory that are not included in the solution are not searched unless 'path' points to them. To search only inside one file, a folder or a project, pass it in 'path'. Uses a case-insensitive literal substring match: pass one literal term per call; OR, wildcards, regular expressions and phrase quotes are not search syntax. If the literal text has no matches but looks like 'A OR B' or is wrapped in quotes, the tool also searches the split/unquoted terms and explains this in 'note'. Use for plain-text or identifier searches when you need matching lines and file locations. It does not search file names; use find_files for that. Supports an optional extension filter. Use group_by to control result grouping: 'file' (default) groups matches by file, with each matching line and its line number; 'text' groups identical matching text across the searched solution files, showing each distinct line once with its total occurrence count and all file:line locations. Both modes include line numbers; 'text' only collapses repeated lines, reducing duplicate results and pagination. Prefer 'text' for broad or exploratory searches, or when many identical/repeated matches are expected. Results are paginated; the response includes 'next_page_token' when more results exist and omits/nulls it when exhausted. If 'next_page_token' is present, call this tool again with 'page_token' set to retrieve the remaining results before finalizing an answer that claims completeness. Default max_results is 25 (max 500); for exhaustive searches or searches where relevant results may be buried among many matches, use a higher max_results value (e.g. 100-200) rather than relying on the default. The search scans at most 1500 files; when this limit is reached, the response 'note' says the search was limited, and you should narrow it with extension_filter and/or path. Lines are 1-indexed. Independent: several calls can go in one response.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "text", new ToolDetails { Type = "string", Description = "The exact plain-text substring to search for inside file contents. When the user requests a literal search, pass the requested text exactly as written, without replacing placeholders, generating values, normalizing characters, or otherwise modifying it. Matching is case-insensitive." } },
                        { "group_by", new ToolDetails { Type = "string", Description = "'file' (default) groups matches by file. Use 'text' to collapse identical matching lines, reducing duplicate results and pagination; each distinct line still includes all file:line locations." } },
                        { "extension_filter", new ToolDetails { Type = "string", Description = "Exact file extension to narrow the search, as a plain dot-prefixed string with NO wildcard characters — e.g. '.cs', '.csproj', '.js'. Do NOT use glob patterns: pass '.cs', never '*.cs'. If omitted, searches all file types." } },
                        { "path", new ToolDetails { Type = "string", Description = "Optional. Where to search: an existing file (only that file is read; extension_filter does not apply), an existing folder (all text files under it on disk, including files not in any project; bin/obj/.git/node_modules and binaries are skipped), or an exact project name such as 'LMLocal.Tests.Unit' (its folder). Solution-relative or absolute, inside the solution directory. Not a substring filter. A bare file name is not a path: resolve it with find_files first." } },
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
                var (searchText, fileExtensions, searchPath, pageToken, pageSize, parsedGrouping, error) = ExtractAndValidateParameters(parameters);
                grouping = parsedGrouping;
                if (error != null)
                    return Error(grouping, error);

                int offset = ToolPagination.ParseOffset(pageToken, pageSize);

                if (!_vsDependencies.IsSolutionOpen)
                    return Error(grouping, "No solution is currently open.");

                string solutionDir = _vsDependencies.GetSolutionDirectory();

                SearchScope scope = null;
                if (!string.IsNullOrWhiteSpace(searchPath))
                {
                    var (resolved, scopeError) = await _scopeResolver.ResolveAsync(
                        searchPath, solutionDir, name => FindProjectDirectoriesAsync(name, cancellationToken), cancellationToken);
                    if (scopeError != null)
                        return Error(grouping, scopeError);
                    scope = resolved;
                }

                string cacheKey = BuildCacheKey(searchText, fileExtensions, scope?.Key, grouping);
                if (grouping == SearchGrouping.Text)
                {
                    if (_searchCache.TryGet(cacheKey, solutionDir, out CachedToolResults<SearchTextGroupResult> cachedText))
                        return BuildTextPageResponse(cachedText.AllResults, offset, pageSize, cachedText.Note);
                }
                else if (_searchCache.TryGet(cacheKey, solutionDir, out CachedToolResults<SearchResult> cachedFile))
                {
                    return BuildFilePageResponse(cachedFile.AllResults, offset, pageSize, cachedFile.Note);
                }

                List<string> allFiles;
                bool scanLimitReached = false;
                string scopeNote = null;

                if (scope == null)
                {
                    var filter = new EnumerateSolutionFilesFilter
                    {
                        ExtensionFilter = fileExtensions,
                        ReturnRelative = false,
                        Limit = MaxFilesToScan + 1, // one extra item detects that the cap was hit
                        IncludeProjects = true
                    };

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                    allFiles = (await _solutionFilesScanner.EnumerateSolutionFilesAsync(filter, cancellationToken)).ToList();
                    scanLimitReached = TrimToLimit(allFiles, MaxFilesToScan);
                    await TaskScheduler.Default;
                }
                else if (scope.Kind == SearchScopeKind.File)
                {
                    await TaskScheduler.Default;
                    allFiles = new List<string> { scope.FilePath };
                    if (!string.IsNullOrWhiteSpace(fileExtensions))
                        scopeNote = "extension_filter was ignored because path is a file.";
                }
                else
                {
                    await TaskScheduler.Default;
                    allFiles = await _scopeResolver.EnumerateFolderFilesAsync(scope.Directories, fileExtensions, MaxFilesToScan + 1, cancellationToken).ConfigureAwait(false);
                    scanLimitReached = TrimToLimit(allFiles, MaxFilesToScan);
                }

                if (scanLimitReached)
                    InternalLogger.Warn($"SearchInSolution: scan limit of {MaxFilesToScan} files reached for '{searchText}'.");

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
                    string scanned = scope == null ? "solution files" : "files under path";
                    string limitNote = $"The search was limited: the scan limit of {MaxFilesToScan} files was reached, so only the first {MaxFilesToScan} {scanned} were searched and matches in the remaining files are missing. A missing match is not proof of absence. Narrow the search with extension_filter and/or path.";
                    note = AppendNote(note, limitNote);
                }

                note = AppendNote(note, scopeNote);

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

                    _searchCache.Set(cacheKey, solutionDir, new CachedToolResults<SearchTextGroupResult>
                    {
                        AllResults = groups,
                        ItemsScanned = allFiles.Count,
                        Note = note
                    });

                    return BuildTextPageResponse(groups, offset, pageSize, note);
                }
                else
                {
                    _searchCache.Set(cacheKey, solutionDir, new CachedToolResults<SearchResult>
                    {
                        AllResults = allResults,
                        ItemsScanned = allFiles.Count,
                        Note = note
                    });

                    return BuildFilePageResponse(allResults, offset, pageSize, note);
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

        /// <summary>
        /// Folders of the solution projects whose name, unique name or project file name equals <paramref name="name"/> exactly.
        /// </summary>
        private async Task<IReadOnlyList<string>> FindProjectDirectoriesAsync(string name, CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var directories = new List<string>();
            var solution = _vsDependencies.GetDTE()?.Solution;
            if (solution == null)
                return directories;

            foreach (var project in ProjectFinder.FindByName(solution, name))
            {
                string fullName = ProjectFinder.SafeName(project, ProjectField.FullName);
                if (string.IsNullOrEmpty(fullName))
                    continue;

                string directory = Path.GetDirectoryName(fullName);
                if (!string.IsNullOrEmpty(directory))
                    directories.Add(directory);
            }

            return directories;
        }

        private static string AppendNote(string note, string addition)
        {
            if (string.IsNullOrEmpty(addition))
                return note;
            return string.IsNullOrEmpty(note) ? addition : note + " " + addition;
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

        /// <summary>
        /// Builds one page of the full ranked result list.
        /// </summary>
        internal static SearchResultsResponse BuildFilePageResponse(IReadOnlyList<SearchResult> allResults, int offset, int pageSize, string note)
        {
            var page = SearchResultPaginator.SliceByMatches(allResults, offset, pageSize);

            int totalMatches = 0;
            if (allResults != null)
            {
                foreach (var result in allResults)
                    totalMatches += result?.MatchCount ?? 0;
            }

            int pageMatches = 0;
            foreach (var result in page)
                pageMatches += result?.MatchCount ?? 0;

            return new SearchResultsResponse
            {
                Results = page,
                Note = note,
                NextPageToken = ToolPagination.NextToken(offset + pageMatches, totalMatches),
                TotalMatches = totalMatches,
                TotalFiles = allResults?.Count ?? 0,
                Success = true
            };
        }

        internal static SearchTextGroupResponse BuildTextPageResponse(IReadOnlyList<SearchTextGroupResult> groups, int offset, int pageSize, string note)
        {
            var page = SearchResultPaginator.SliceByGroups(groups, offset, pageSize);

            return new SearchTextGroupResponse
            {
                Results = page,
                Note = note,
                NextPageToken = ToolPagination.NextToken(offset + page.Count, groups?.Count ?? 0),
                TotalMatches = SearchResultGrouper.CountOccurrences(groups),
                TotalFiles = SearchResultGrouper.CountDistinctFiles(groups),
                Success = true
            };
        }

        /// <summary>
        /// The cache holds the whole ranked result for both grouping modes, so the page size is not part of the key.
        /// </summary>
        private string BuildCacheKey(string text, string extensionFilter, string scopeKey, SearchGrouping grouping)
        {
            var ext = extensionFilter ?? string.Empty;
            var scope = scopeKey ?? string.Empty;
            var txt = text ?? string.Empty;

            return $"{txt}||{ext}||{scope}||{grouping}||{CacheVersion}";
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            if (parameters == null) return "Searching... ";

            var text = parameters.TryGetValue("text", out var q) ? q?.ToString() : "";
            var ext = parameters.TryGetValue("extension_filter", out var e) ? e?.ToString() : null;
            var path = parameters.TryGetValue("path", out var p) ? p?.ToString() : null;
            var pageToken = ToolPagination.ReadToken(parameters);
            var groupBy = parameters.TryGetValue("group_by", out var g) ? g?.ToString() : null;

            var message = $"Searching for '{text}'";
            if (!string.IsNullOrWhiteSpace(path))
                message += $" in '{path.Trim()}'";

            if (!string.IsNullOrEmpty(ext))
                message += $", with extension '{ext}'";
            else if (string.IsNullOrWhiteSpace(path))
                message += " in all files";

            if (string.Equals(groupBy, "text", StringComparison.OrdinalIgnoreCase))
                message += ", grouped by text";

            int offset = ToolPagination.ParseOffset(pageToken);
            if (offset > 0)
                message += $" (from result {(long)offset + 1})";

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

        private (string searchText, string fileExtensions, string searchPath, string pageToken, int pageSize, SearchGrouping grouping, string error) ExtractAndValidateParameters(
            Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, null, null, null, DefaultPageSize, SearchGrouping.File, "Parameters cannot be null.");
            if (!parameters.TryGetValue("text", out object textObj) || !(textObj is string))
                return (null, null, null, null, DefaultPageSize, SearchGrouping.File, "Parameter 'text' is required and must be a string.");

            var searchText = (string)textObj;
            var fileExtensions = parameters.TryGetValue("extension_filter", out object extObj) ? extObj as string : null;
            var searchPath = parameters.TryGetValue("path", out object pathObj) ? pathObj as string : null;
            var pageToken = ToolPagination.ReadToken(parameters);

            var grouping = SearchGrouping.File;
            if (parameters.TryGetValue("group_by", out object groupObj) && groupObj is string groupStr &&
                string.Equals(groupStr, "text", StringComparison.OrdinalIgnoreCase))
            {
                grouping = SearchGrouping.Text;
            }

            int pageSize = DefaultPageSize;
            if (parameters.TryGetValue("max_results", out object maxObj) && maxObj != null && int.TryParse(maxObj.ToString(), out int maxVal))
                pageSize = Math.Min(Math.Max(maxVal, 1), MaxPageSize);

            return (searchText, fileExtensions, searchPath, pageToken, pageSize, grouping, null);
        }

    }
}
