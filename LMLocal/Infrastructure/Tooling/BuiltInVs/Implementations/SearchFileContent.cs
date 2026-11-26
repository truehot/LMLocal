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
        internal const int MaxTexts = 10;
        internal const string TextParameter = "text";
        internal const string TextsParameter = "texts";

        /// <summary>
        /// Version of the search/matching/ranking logic.
        /// </summary>
        private const string CacheVersion = "sig6";

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
                Description = "Use this tool whenever the user asks to search for text, literal text, a substring, an occurrence, a match, an identifier, or content inside files. This is the required tool for content searches, even when the search string looks like a file name or path. Without 'path', searches file contents only in files that belong to the currently loaded Visual Studio solution; files in the solution directory that are not included in the solution are not searched unless 'path' points to them. To search only inside one file, a folder or a project, pass it in 'path'. Uses a case-insensitive literal substring match; OR, wildcards, regular expressions and phrase quotes are not search syntax. To search several literal texts with the same path, extension_filter and group_by, pass them all in 'texts' in one call instead of one call per text: each text is searched on its own, and the response has 'terms' with one block per text (its 'text', results, totals, note and next_page_token); to get more results of one block, call again with 'text' set to that block's text and 'page_token'. If the literal text has no matches but looks like 'A OR B' or is wrapped in quotes, the tool also searches the split/unquoted terms and explains this in 'note'. Use for plain-text or identifier searches when you need matching lines and file locations. It does not search file names; use find_files for that. Supports an optional extension filter. Use group_by to control result grouping: 'file' (default) groups matches by file, with each matching line and its line number, and the file's total_lines (its line count, so a following read_file_lines range can stay within the file);'text' groups identical matching text across the searched solution files, showing each distinct line once with its total occurrence count and all file:line locations. Both modes include line numbers; 'text' only collapses repeated lines, reducing duplicate results and pagination. Prefer 'text' for broad or exploratory searches, or when many identical/repeated matches are expected. Results are paginated; the response includes 'next_page_token' when more results exist and omits/nulls it when exhausted. If 'next_page_token' is present, call this tool again with 'page_token' set to retrieve the remaining results before finalizing an answer that claims completeness. Default max_results is 25 (max 500); for exhaustive searches or searches where relevant results may be buried among many matches, use a higher max_results value (e.g. 100-200) rather than relying on the default. The search scans at most 1500 files; when this limit is reached, the response 'note' says the search was limited, and you should narrow it with extension_filter and/or path. Lines are 1-indexed. Independent: several calls can go in one response.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "text", new ToolDetails { Type = "string", Description = "The exact plain-text substring to search for inside file contents. When the user requests a literal search, pass the requested text exactly as written, without replacing placeholders, generating values, normalizing characters, or otherwise modifying it. Matching is case-insensitive. Required unless 'texts' is passed." } },
                        { "group_by", new ToolDetails { Type = "string", Description = "'file' (default) groups matches by file. Use 'text' to collapse identical matching lines, reducing duplicate results and pagination; each distinct line still includes all file:line locations." } },
                        { "extension_filter", new ToolDetails { Type = "string", Description = "Exact file extension to narrow the search, as a plain dot-prefixed string with NO wildcard characters — e.g. '.cs', '.csproj', '.js'. Do NOT use glob patterns: pass '.cs', never '*.cs'. If omitted, searches all file types." } },
                        { "path", new ToolDetails { Type = "string", Description = "Optional. Where to search: an existing file (only that file is read; extension_filter does not apply), an existing folder (all text files under it on disk, including files not in any project; bin/obj/.git/node_modules and binaries are skipped), or an exact project name such as 'LMLocal.Tests.Unit' (its folder). Solution-relative or absolute, inside the solution directory. Not a substring filter. A bare file name is not a path: resolve it with find_files first. No wildcards or exclusions ('*', '!'): to search everywhere, omit path. A solution-relative path starts with the project folder: 'MyApp/Services/OrderService.cs', not 'Services/OrderService.cs'." } },
                        { "page_token", new ToolDetails { Type = "string", Description = "The value of next_page_token from the previous response, copied exactly; never a value that no response returned. The page is taken from that result, so text, path, extension_filter and group_by may be left out. Pass 'last' (together with the same text and filters) to get the last page, 'first' for the first." } },
                        { "max_results", new ToolDetails { Type = "integer", Description = "Number of results to return per page. Default 25, max 500. Use a larger value for broad or complete searches to reduce pagination." } },
                        { TextsParameter, new ToolDetails { Type = "array", Items = new ToolDetails { Type = "string" }, Description = $"Several literal texts to search in one call (up to {MaxTexts}), each exactly as written, like 'text'. The response has one block per text in 'terms'; max_results applies to each block, and all blocks together return at most {MaxPageSize} matches. Use instead of 'text' when the texts are known up front." } }
                    },
                    Required = new List<string>()
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            SearchGrouping grouping = SearchGrouping.File;
            try
            {
                var selection = PageSelection.From(parameters);
                if (selection.Error != null)
                    return Error(grouping, selection.Error);

                if (selection.IsCursor)
                {
                    var probe = ExtractAndValidateParameters(parameters);
                    bool severalTexts = probe.Terms != null && probe.Terms.Count > 1;
                    if (!severalTexts)
                    {
                        CachedToolResults<SearchTextGroupResult> textEntry = null;
                        bool foundFile = _searchCache.TryGetById(selection.Id, out CachedToolResults<SearchResult> fileEntry);
                        bool foundText = !foundFile && _searchCache.TryGetById(selection.Id, out textEntry);
                        bool found = foundFile || foundText;
                        string subject = foundFile ? fileEntry.Subject : foundText ? textEntry.Subject : null;
                        string callText = probe.Error == null && probe.Terms != null && probe.Terms.Count == 1 ? probe.Terms[0] : null;
                        selection.Resolve(found, found && (callText == null || subject == null || string.Equals(callText, subject, StringComparison.OrdinalIgnoreCase)));

                        if (selection.UseEntry)
                        {
                            int entryPageSize = ReadPageSize(parameters);
                            if (foundFile)
                                return BuildFilePageResponse(fileEntry.AllResults, selection.Offset(entryPageSize, CountMatches(fileEntry.AllResults)), entryPageSize, fileEntry.Note, selection.Id, null);

                            return BuildTextPageResponse(textEntry.AllResults, selection.Offset(entryPageSize, textEntry.AllResults?.Count ?? 0), entryPageSize, textEntry.Note, selection.Id, null);
                        }
                    }
                }

                var request = ExtractAndValidateParameters(parameters);
                grouping = request.Grouping;
                if (request.Error != null)
                    return Error(grouping, request.Terms == null ? selection.WithPagingHint(request.Error) : request.Error);

                var terms = request.Terms;

                int pageSize = request.IsMulti ? Math.Max(1, Math.Min(request.PageSize, MaxPageSize / terms.Count)) : request.PageSize;

                if (!_vsDependencies.IsSolutionOpen)
                    return Error(grouping, "No solution is currently open.");

                string solutionDir = _vsDependencies.GetSolutionDirectory();

                SearchScope scope = null;
                if (!string.IsNullOrWhiteSpace(request.SearchPath))
                {
                    var (resolved, scopeError) = await _scopeResolver.ResolveAsync(
                        request.SearchPath, solutionDir, name => FindProjectDirectoriesAsync(name, cancellationToken), cancellationToken);
                    if (scopeError != null)
                        return Error(grouping, scopeError);
                    scope = resolved;
                }

                string warning = AppendNote(selection.Warning, scope?.Note);

                var pages = new object[terms.Count];
                var uncached = new List<int>();
                for (int i = 0; i < terms.Count; i++)
                {
                    string cacheKey = BuildCacheKey(terms[i], request.FileExtensions, scope?.Key, grouping);
                    if (grouping == SearchGrouping.Text)
                    {
                        if (_searchCache.TryGet(cacheKey, solutionDir, out CachedToolResults<SearchTextGroupResult> cachedText))
                            pages[i] = BuildTextPageResponse(cachedText.AllResults, selection.Offset(pageSize, cachedText.AllResults?.Count ?? 0), pageSize, cachedText.Note, _searchCache.GetId(cacheKey), warning);
                    }
                    else if (_searchCache.TryGet(cacheKey, solutionDir, out CachedToolResults<SearchResult> cachedFile))
                    {
                        pages[i] = BuildFilePageResponse(cachedFile.AllResults, selection.Offset(pageSize, CountMatches(cachedFile.AllResults)), pageSize, cachedFile.Note, _searchCache.GetId(cacheKey), warning);
                    }

                    if (pages[i] == null)
                        uncached.Add(i);
                }

                if (uncached.Count > 0)
                {
                    List<string> allFiles;
                    bool scanLimitReached = false;
                    string scopeNote = null;

                    if (scope == null)
                    {
                        var filter = new EnumerateSolutionFilesFilter
                        {
                            ExtensionFilter = request.FileExtensions,
                            ReturnRelative = false,
                            Limit = MaxFilesToScan + 1,
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
                        if (!string.IsNullOrWhiteSpace(request.FileExtensions))
                            scopeNote = "extension_filter was ignored because path is a file.";
                    }
                    else
                    {
                        await TaskScheduler.Default;
                        allFiles = await _scopeResolver.EnumerateFolderFilesAsync(scope.Directories, request.FileExtensions, MaxFilesToScan + 1, cancellationToken).ConfigureAwait(false);
                        scanLimitReached = TrimToLimit(allFiles, MaxFilesToScan);
                    }

                    var scanTerms = uncached.Select(i => terms[i]).ToList();
                    if (scanLimitReached)
                        InternalLogger.Warn($"SearchInSolution: scan limit of {MaxFilesToScan} files reached for {string.Join(", ", scanTerms.Select(t => "'" + t + "'"))}.");

                    var scanned = await ContentTermScanner.ScanAsync(
                        _fileSystem,
                        allFiles,
                        scanTerms,
                        needRanking: grouping == SearchGrouping.File,
                        toRelativePath: absolutePath => _pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath) ? relativePath : absolutePath,
                        cancellationToken).ConfigureAwait(false);

                    string limitNote = null;
                    if (scanLimitReached)
                    {
                        string scannedFiles = scope == null ? "solution files" : "files under path";
                        limitNote = $"The search was limited: the scan limit of {MaxFilesToScan} files was reached, so only the first {MaxFilesToScan} {scannedFiles} were searched and matches in the remaining files are missing. A missing match is not proof of absence. Narrow the search with extension_filter and/or path.";
                    }

                    for (int k = 0; k < uncached.Count; k++)
                    {
                        int i = uncached[k];
                        var term = scanned[k];
                        string note = AppendNote(AppendNote(term.FallbackNote, limitNote), scopeNote);
                        string cacheKey = BuildCacheKey(terms[i], request.FileExtensions, scope?.Key, grouping);

                        if (grouping == SearchGrouping.Text)
                        {
                            var groups = SearchResultGrouper.GroupByText(term.Results);
                            _searchCache.Set(cacheKey, solutionDir, new CachedToolResults<SearchTextGroupResult>
                            {
                                AllResults = groups,
                                ItemsScanned = allFiles.Count,
                                Note = note,
                                Subject = terms[i]
                            });
                            pages[i] = BuildTextPageResponse(groups, selection.Offset(pageSize, groups?.Count ?? 0), pageSize, note, _searchCache.GetId(cacheKey), warning);
                        }
                        else
                        {
                            _searchCache.Set(cacheKey, solutionDir, new CachedToolResults<SearchResult>
                            {
                                AllResults = term.Results,
                                ItemsScanned = allFiles.Count,
                                Note = note,
                                Subject = terms[i]
                            });
                            pages[i] = BuildFilePageResponse(term.Results, selection.Offset(pageSize, CountMatches(term.Results)), pageSize, note, _searchCache.GetId(cacheKey), warning);
                        }
                    }
                }

                if (!request.IsMulti)
                    return pages[0];

                for (int i = 0; i < terms.Count; i++)
                {
                    if (pages[i] is SearchResultsResponse byFile)
                        byFile.Text = terms[i];
                    else if (pages[i] is SearchTextGroupResponse byText)
                        byText.Text = terms[i];
                }

                return new SearchMultiTermResponse { Terms = pages.ToList(), Success = true };
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
        internal static SearchResultsResponse BuildFilePageResponse(IReadOnlyList<SearchResult> allResults, int offset, int pageSize, string note, string cacheId = null, string warning = null)
        {
            var page = SearchResultPaginator.SliceByMatches(allResults, offset, pageSize);

            int totalMatches = CountMatches(allResults);

            int pageMatches = 0;
            foreach (var result in page)
                pageMatches += result?.MatchCount ?? 0;

            string nextToken = ToolPagination.NextToken(offset + pageMatches, totalMatches, cacheId);

            return new SearchResultsResponse
            {
                Results = page,
                Note = note,
                Showing = ToolPagination.Describe(offset, pageMatches, totalMatches, nextToken != null, pageSize),
                NextPageToken = nextToken,
                TotalMatches = totalMatches,
                TotalFiles = allResults?.Count ?? 0,
                Success = true,
                Warning = warning
            };
        }

        internal static SearchTextGroupResponse BuildTextPageResponse(IReadOnlyList<SearchTextGroupResult> groups, int offset, int pageSize, string note, string cacheId = null, string warning = null)
        {
            var page = SearchResultPaginator.SliceByGroups(groups, offset, pageSize);
            int total = groups?.Count ?? 0;
            string nextToken = ToolPagination.NextToken(offset + page.Count, total, cacheId);

            return new SearchTextGroupResponse
            {
                Results = page,
                Note = note,
                Showing = ToolPagination.Describe(offset, page.Count, total, nextToken != null, pageSize),
                NextPageToken = nextToken,
                TotalMatches = SearchResultGrouper.CountOccurrences(groups),
                TotalFiles = SearchResultGrouper.CountDistinctFiles(groups),
                Success = true,
                Warning = warning
            };
        }

        /// <summary>Number of matches of the whole ranked list: the unit of the offset in grouping = file.</summary>
        internal static int CountMatches(IReadOnlyList<SearchResult> allResults)
        {
            int totalMatches = 0;
            if (allResults != null)
            {
                foreach (var result in allResults)
                    totalMatches += result?.MatchCount ?? 0;
            }

            return totalMatches;
        }

        /// <summary>max_results as the page size (default and bounds as in <see cref="ExtractAndValidateParameters"/>).</summary>
        private static int ReadPageSize(Dictionary<string, object> parameters)
        {
            if (parameters != null && parameters.TryGetValue("max_results", out object maxObj) && maxObj != null && int.TryParse(maxObj.ToString(), out int maxVal))
                return Math.Min(Math.Max(maxVal, 1), MaxPageSize);
            return DefaultPageSize;
        }

        /// <summary>
        /// The cache holds the whole ranked result for both grouping modes, so the page size is not part of the key.
        /// </summary>
        internal static string BuildCacheKey(string text, string extensionFilter, string scopeKey, SearchGrouping grouping)
        {
            var ext = extensionFilter ?? string.Empty;
            var scope = scopeKey ?? string.Empty;
            var txt = text ?? string.Empty;

            return $"{txt}||{ext}||{scope}||{grouping}||{CacheVersion}";
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            if (parameters == null) return "Searching... ";

            var request = ExtractAndValidateParameters(parameters);
            var text = request.Terms != null && request.IsMulti
                ? string.Join("', '", request.Terms)
                : parameters.TryGetValue(TextParameter, out var q) ? q?.ToString() : "";
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

            if (result is SearchMultiTermResponse multi)
            {
                if (!multi.Success)
                    return $"Searching failed: {multi.ErrorMessage}";

                var counts = new List<string>();
                int total = 0;
                foreach (var block in multi.Terms ?? new List<object>())
                {
                    if (block is SearchResultsResponse byFile)
                    {
                        counts.Add($"'{byFile.Text}': {byFile.TotalMatches}");
                        total += byFile.TotalMatches;
                    }
                    else if (block is SearchTextGroupResponse byText)
                    {
                        counts.Add($"'{byText.Text}': {byText.TotalMatches}");
                        total += byText.TotalMatches;
                    }
                }

                return $"Found {total} {Pluralizer.Pluralize(total, "match", "matches")} for {counts.Count} {Pluralizer.Pluralize(counts.Count, "text", "texts")} ({string.Join(", ", counts)}).";
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

        internal sealed class SearchRequest
        {
            public List<string> Terms;

            /// <summary>'texts' was passed: the response is <see cref="SearchMultiTermResponse"/>, one block per text.</summary>
            public bool IsMulti;

            public string FileExtensions, SearchPath, PageToken, Error;
            public int PageSize = DefaultPageSize;
            public SearchGrouping Grouping = SearchGrouping.File;
        }

        internal static SearchRequest ExtractAndValidateParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return new SearchRequest { Error = "Parameters cannot be null." };

            parameters.TryGetValue(TextParameter, out object textObj);
            parameters.TryGetValue(TextsParameter, out object textsObj);
            bool isMulti = !LiteralListParameter.IsMissing(textsObj);

            List<string> terms;
            if (!isMulti)
            {
                // Single text: unchanged contract, the text is used exactly as passed.
                if (!(textObj is string single))
                    return new SearchRequest { Error = $"Parameter '{TextParameter}' (or '{TextsParameter}') is required and must be a string." };
                terms = new List<string> { single };
            }
            else
            {
                if (textObj != null && !(textObj is string))
                    return new SearchRequest { Error = $"Parameter '{TextParameter}' must be a string." };

                terms = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                LiteralListParameter.Add(textObj as string, terms, seen);
                if (!LiteralListParameter.TryAdd(textsObj, terms, seen))
                    return new SearchRequest { Error = $"Parameter '{TextsParameter}' must be an array of strings." };

                if (terms.Count == 0)
                    return new SearchRequest { Error = $"Parameter '{TextsParameter}' has no non-empty text." };
                if (terms.Count > MaxTexts)
                    return new SearchRequest { Error = $"At most {MaxTexts} texts per call (got {terms.Count}). Split them into several calls." };
            }

            var request = new SearchRequest
            {
                Terms = terms,
                IsMulti = isMulti,
                FileExtensions = parameters.TryGetValue("extension_filter", out object extObj) ? extObj as string : null,
                SearchPath = parameters.TryGetValue("path", out object pathObj) ? pathObj as string : null,
                PageToken = ToolPagination.ReadToken(parameters)
            };

            if (parameters.TryGetValue("group_by", out object groupObj) && groupObj is string groupStr &&
                string.Equals(groupStr, "text", StringComparison.OrdinalIgnoreCase))
            {
                request.Grouping = SearchGrouping.Text;
            }

            if (parameters.TryGetValue("max_results", out object maxObj) && maxObj != null && int.TryParse(maxObj.ToString(), out int maxVal))
                request.PageSize = Math.Min(Math.Max(maxVal, 1), MaxPageSize);

            if (isMulti && terms.Count > 1 && request.PageToken != null)
                request.Error = $"'{ToolPagination.TokenParameter}' continues one text: call again with '{TextParameter}' set to that block's text and its next_page_token.";

            return request;
        }
    }
}
