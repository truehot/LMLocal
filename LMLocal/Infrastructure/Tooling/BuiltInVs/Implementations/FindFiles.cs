using LMLocal.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Common.VsSolutionFilesScanner;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    internal interface IFindFiles : IBuiltInTool
    {
    }

    internal class FindFiles : IFindFiles
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IVsSolutionFilesScanner _solutionFilesScanner;
        private readonly ISearchResultCache _searchCache;
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 500;
        private const int MaxFilesToScan = 1500;
        internal const int MaxFileNames = 30;
        internal const string FileNameParameter = "file_name";
        internal const string FileNamesParameter = "file_names";

        public string ToolName => "find_files";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

        public FindFiles(
            IVsDependencies vsDependencies,
            IVsSolutionFilesScanner solutionFilesScanner,
            ISearchResultCache searchCache)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _solutionFilesScanner = solutionFilesScanner ?? throw new ArgumentNullException(nameof(solutionFilesScanner));
            _searchCache = searchCache ?? throw new ArgumentNullException(nameof(searchCache));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Use this tool only when the user asks to find or locate a file by file name, path, or file-name pattern. Several names: pass them all in 'file_names' in ONE call, not one call per name. Do not use it for requests to search for text or content inside files. Finds files by name in files that belong to the currently loaded Visual Studio solution. It does not search arbitrary files located in the solution directory or its subdirectories unless those files are included in the solution. Matching is against the full file name including extension, not just the name stem. Matching is case-insensitive substring matching unless '*' wildcards are used to constrain the match by position: 'Program*' matches names starting with 'Program', '*Service' matches names ending with 'Service', and 'Chat*Service' matches names starting with 'Chat' and ending with 'Service'. Therefore, a pattern without '*' such as 'ChatLogSerializer' can still match 'ChatLogSerializer.cs'. Use this tool to locate files when you know part of the name. It can also be used as a fallback/cross-check when searching for where a type is declared: in this codebase's C# convention, a type is usually declared in a file with a matching name (e.g. class ChatLogSerializer → ChatLogSerializer.cs), so searching '<TypeName>*' here can be a useful fallback or cross-check when a content search was inconclusive. Does not search file contents — use search_file_content for that. With 'file_names' each name is matched on its own and the response has 'terms', one block per name (file_name, results, total_files, note, next_page_token); to continue one block, call again with that 'file_name' and its 'page_token'. Results are paginated: default max_results is 50 (max 500); the response includes 'next_page_token' when more results exist. The search returns at most 1500 matching files in total; when this limit is reached, the response 'note' says the list is incomplete, and you should narrow it with file_name, extension_filter and/or project_filter. Independent: several calls can go in one response.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { FileNameParameter, new ToolDetails { Type = "string", Description = "File name pattern. Supports '*' anywhere: 'Program*' (prefix), '*Service' (suffix), 'Chat*Service' (middle). For all files, use '.'. Required unless 'file_names' is passed." } },
                        { "extension_filter", new ToolDetails { Type = "string", Description = "Exact file extension to narrow the search, as a plain dot-prefixed string with NO wildcard characters — e.g. '.cs', '.csproj', '.js'. Do NOT use glob patterns: pass '.cs', never '*.cs'. If omitted, searches all file types." } },
                        { "project_filter", new ToolDetails { Type = "string", Description = "Substring filter applied case-insensitively to project/path components. It is not an exact full project-name match; for example, 'Tests' can match a project such as 'LMLocal.Tests.E2E' when that text occurs in a relevant path component." } },
                        { "page_token", new ToolDetails { Type = "string", Description = "Page token for next page of results." } },
                        { "max_results", new ToolDetails { Type = "integer", Description = "Number of files to return per page. Default 50, max 500." } },
                        { FileNamesParameter, new ToolDetails { Type = "array", Items = new ToolDetails { Type = "string" }, Description = $"Several file name patterns to find in one call (up to {MaxFileNames}), each like 'file_name'. The response has one block per pattern in 'terms'; max_results applies to each block, and all blocks together return at most {MaxPageSize} files. Use instead of 'file_name' when the names are known up front." } }
                    },
                    Required = new List<string>()
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!_vsDependencies.IsSolutionOpen)
                    return ErrorResponse("No solution is currently open.");

                string solutionDir = _vsDependencies.GetSolutionDirectory();

                var request = ExtractAndValidateParameters(parameters);
                if (request.Error != null)
                    return ErrorResponse(request.Error);

                var names = request.Names;
                // Several names share the page budget: each block gets max_results, but the whole response stays within MaxPageSize.
                int pageSize = request.IsMulti ? Math.Max(1, Math.Min(request.PageSize, MaxPageSize / names.Count)) : request.PageSize;
                int offset = ToolPagination.ParseOffset(request.PageToken, pageSize);

                // Every name is cached under the same key as a search for that name alone, so a page_token call with 'file_name' continues a block.
                var pages = new FileSearchResultsResponse[names.Count];
                var uncached = new List<int>();
                for (int i = 0; i < names.Count; i++)
                {
                    string cacheKey = BuildCacheKey(names[i], request.FileExtension, request.ProjectFilter);
                    if (_searchCache.TryGet(cacheKey, solutionDir, out CachedToolResults<FileSearchResult> cached))
                        pages[i] = BuildPageResponse(cached.AllResults, offset, pageSize, cached.Note);
                    else
                        uncached.Add(i);
                }

                if (uncached.Count > 0)
                {
                    foreach (int i in uncached)
                    {
                        // The scanner reads the solution hierarchy on the UI thread (no-op when already there).
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                        var filter = new EnumerateSolutionFilesFilter
                        {
                            ExtensionFilter = request.FileExtension,
                            ReturnRelative = true,
                            FileName = names[i],
                            ProjectFilter = request.ProjectFilter,
                            Limit = MaxFilesToScan + 1, // one extra item detects that the cap was hit
                            IncludeProjects = true
                        };

                        var matchingFiles = (await _solutionFilesScanner.EnumerateSolutionFilesAsync(filter, cancellationToken)).ToList();
                        bool limitReached = TrimToLimit(matchingFiles, MaxFilesToScan);
                        string note = limitReached
                            ? $"The search was limited: the limit of {MaxFilesToScan} matching files was reached, so more files match than are listed and the list is incomplete. Narrow the search with file_name, extension_filter and/or project_filter."
                            : null;
                        if (limitReached)
                            InternalLogger.Warn($"{ToolName}: limit of {MaxFilesToScan} files reached for '{names[i]}'.");

                        var cacheEntry = new CachedToolResults<FileSearchResult>
                        {
                            AllResults = matchingFiles.Select(file => new FileSearchResult { FilePath = file }).ToList(),
                            ItemsScanned = matchingFiles.Count,
                            Note = note
                        };
                        _searchCache.Set(BuildCacheKey(names[i], request.FileExtension, request.ProjectFilter), solutionDir, cacheEntry);
                        pages[i] = BuildPageResponse(cacheEntry.AllResults, offset, pageSize, cacheEntry.Note);
                    }
                }

                if (!request.IsMulti)
                    return pages[0];

                for (int i = 0; i < names.Count; i++)
                    pages[i].FileName = names[i];

                return new FindFilesMultiResponse { Terms = pages.ToList(), Success = true };
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error in {ToolName}: {ex}");
                return ErrorResponse(ex.Message);
            }
        }

        /// <summary>
        /// Builds one page of the full result list: the page comes from <paramref name="allResults"/>,
        /// the totals are computed over that whole list, and the token is the offset of the first
        /// item that is not on this page.
        /// </summary>
        internal static FileSearchResultsResponse BuildPageResponse(IReadOnlyList<FileSearchResult> allResults, int offset, int pageSize, string note)
        {
            var all = allResults ?? new List<FileSearchResult>();
            var page = all.Skip(offset).Take(pageSize).ToList();

            return new FileSearchResultsResponse
            {
                Results = page,
                Note = note,
                NextPageToken = ToolPagination.NextToken(offset + page.Count, all.Count),
                TotalFiles = all.Count,
                Success = true,
                ErrorMessage = null
            };
        }

        internal static string BuildCacheKey(string fileName, string fileExtension, string projectFilter)
        {
            var ext = fileExtension ?? string.Empty;
            var proj = projectFilter ?? string.Empty;
            var name = fileName ?? string.Empty;
            return $"{name}||{ext}||{proj}";
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            if (parameters == null) return "Finding... ";

            var request = ExtractAndValidateParameters(parameters);
            var names = request.Names != null && request.Names.Count > 0
                ? string.Join(", ", request.Names.Take(MaxFileNames).Select(n => "'" + n + "'"))
                  + (request.Names.Count > MaxFileNames ? $" and {request.Names.Count - MaxFileNames} more" : "")
                : "''";
            var ext = parameters.TryGetValue("extension_filter", out var fe) ? fe?.ToString() : null;
            var project = parameters.TryGetValue("project_filter", out var pf) ? pf?.ToString() : null;
            var pageToken = ToolPagination.ReadToken(parameters);

            var message = $"Finding {names}";
            if (!string.IsNullOrEmpty(project))
                message += $" in project '{project}'";
            if (!string.IsNullOrEmpty(ext))
                message += $", with extension '{ext}'";
            int offset = ToolPagination.ParseOffset(pageToken);
            if (offset > 0)
                message += $" (from result {(long)offset + 1})";

            message += "... ";
            return message;
        }

        public string GetCompletionMessage(object result)
        {
            if (result is FileSearchResultsResponse fileResults)
            {
                if (!fileResults.Success)
                    return $"Error: {fileResults.ErrorMessage}";

                var message = fileResults.Results.Count == 0
                    ? "Found no files"
                    : $"Found {fileResults.Results.Count} {Pluralizer.Pluralize(fileResults.Results.Count, "file", "files")}";
                if (fileResults.TotalFiles > 0 && fileResults.Results.Count < fileResults.TotalFiles)
                    message += $" (total: {fileResults.TotalFiles} {Pluralizer.Pluralize(fileResults.TotalFiles, "file", "files")})";
                message += ".";
                return message;
            }

            if (result is FindFilesMultiResponse multi)
            {
                if (!multi.Success)
                    return $"Error: {multi.ErrorMessage}";

                var blocks = multi.Terms ?? new List<FileSearchResultsResponse>();
                int total = blocks.Sum(b => b.TotalFiles);
                var counts = string.Join(", ", blocks.Select(b => $"'{b.FileName}': {b.TotalFiles}"));
                return $"Found {total} {Pluralizer.Pluralize(total, "file", "files")} for {blocks.Count} {Pluralizer.Pluralize(blocks.Count, "name", "names")} ({counts}).";
            }

            return "File search finished.";
        }

        internal sealed class FindRequest
        {
            public List<string> Names;

            /// <summary>'file_names' was passed: the response is <see cref="FindFilesMultiResponse"/>, one block per name.</summary>
            public bool IsMulti;

            public string FileExtension, ProjectFilter, PageToken, Error;
            public int PageSize = DefaultPageSize;
        }

        internal static FindRequest ExtractAndValidateParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return new FindRequest { Error = "Parameters cannot be null." };

            parameters.TryGetValue(FileNameParameter, out object nameObj);
            parameters.TryGetValue(FileNamesParameter, out object namesObj);

            if (LiteralListParameter.IsMissing(namesObj) && nameObj != null && !(nameObj is string))
            {
                namesObj = nameObj;
                nameObj = null;
            }

            bool isMulti = !LiteralListParameter.IsMissing(namesObj);

            List<string> names;
            if (!isMulti)
            {
                // Single name: unchanged contract.
                if (!(nameObj is string single))
                    return new FindRequest { Error = $"Parameter '{FileNameParameter}' (or '{FileNamesParameter}') is required and must be a string." };
                names = new List<string> { single };
            }
            else
            {
                if (nameObj != null && !(nameObj is string))
                    return new FindRequest { Error = $"Parameter '{FileNameParameter}' must be a string." };

                // File-name matching is case-insensitive, so case variants are duplicates.
                names = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                LiteralListParameter.Add(nameObj as string, names, seen);
                if (!LiteralListParameter.TryAdd(namesObj, names, seen))
                    return new FindRequest { Error = $"Parameter '{FileNamesParameter}' must be an array of strings." };

                if (names.Count == 0)
                    return new FindRequest { Error = $"Parameter '{FileNamesParameter}' has no non-empty name." };
                if (names.Count > MaxFileNames)
                    return new FindRequest { Names = names, Error = $"At most {MaxFileNames} file names per call (got {names.Count}). Split them into several calls." };
            }

            var request = new FindRequest
            {
                Names = names,
                IsMulti = isMulti,
                FileExtension = parameters.TryGetValue("extension_filter", out object extObj) ? extObj as string : null,
                ProjectFilter = parameters.TryGetValue("project_filter", out object projObj) ? projObj as string : null,
                PageToken = ToolPagination.ReadToken(parameters)
            };

            if (parameters.TryGetValue("max_results", out object maxObj) && maxObj != null && int.TryParse(maxObj.ToString(), out int maxVal))
                request.PageSize = Math.Min(Math.Max(maxVal, 1), MaxPageSize);

            if (isMulti && names.Count > 1 && request.PageToken != null)
                request.Error = $"'{ToolPagination.TokenParameter}' continues one name: call again with '{FileNameParameter}' set to that block's name and its next_page_token.";

            return request;
        }

        private static FileSearchResultsResponse ErrorResponse(string message)
        {
            return new FileSearchResultsResponse
            {
                Results = new List<FileSearchResult>(),
                NextPageToken = null,
                TotalFiles = 0,
                Success = false,
                ErrorMessage = message
            };
        }


        public class FileSearchResult
        {
            [JsonProperty("file_path")]
            public string FilePath { get; set; }
        }

        public class FileSearchResultsResponse
        {
            /// <summary>The searched name pattern; set only on a block of <see cref="FindFilesMultiResponse"/>.</summary>
            [JsonProperty("file_name", NullValueHandling = NullValueHandling.Ignore, Order = -2)]
            public string FileName { get; set; }

            [JsonProperty("results")]
            public List<FileSearchResult> Results { get; set; }

            /// <summary>Set when the file limit was reached and the result list is incomplete.</summary>
            [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
            public string Note { get; set; }

            [JsonProperty("next_page_token", NullValueHandling = NullValueHandling.Ignore)]
            public string NextPageToken { get; set; }

            [JsonProperty("total_files")]
            public int TotalFiles { get; set; }

            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
            public string ErrorMessage { get; set; }
        }

        /// <summary>
        /// Response of a call with 'file_names': one <see cref="FileSearchResultsResponse"/> block per name, in request order,
        /// each with <c>FileName</c> set and its own totals, note and next_page_token.
        /// </summary>
        public class FindFilesMultiResponse
        {
            [JsonProperty("terms")]
            public List<FileSearchResultsResponse> Terms { get; set; }

            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
            public string ErrorMessage { get; set; }
        }
    }
}
