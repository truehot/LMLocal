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
                Description = "Use this tool only when the user asks to find or locate a file by file name, path, or file-name pattern. Do not use it for requests to search for text or content inside files. Finds files by name in files that belong to the currently loaded Visual Studio solution. It does not search arbitrary files located in the solution directory or its subdirectories unless those files are included in the solution. Matching is against the full file name including extension, not just the name stem. Matching is case-insensitive substring matching unless '*' wildcards are used to constrain the match by position: 'Program*' matches names starting with 'Program', '*Service' matches names ending with 'Service', and 'Chat*Service' matches names starting with 'Chat' and ending with 'Service'. Therefore, a pattern without '*' such as 'ChatLogSerializer' can still match 'ChatLogSerializer.cs'. Use this tool to locate files when you know part of the name. It can also be used as a fallback/cross-check when searching for where a type is declared: in this codebase's C# convention, a type is usually declared in a file with a matching name (e.g. class ChatLogSerializer → ChatLogSerializer.cs), so searching '<TypeName>*' here can be a useful fallback or cross-check when a content search was inconclusive. Does not search file contents — use search_file_content for that. Results are paginated: default max_results is 50 (max 500); the response includes 'next_page_token' when more results exist. The search returns at most 1500 matching files in total; when this limit is reached, the response 'note' says the list is incomplete, and you should narrow it with file_name, extension_filter and/or project_filter. Independent: several calls can go in one response.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_name", new ToolDetails { Type = "string", Description = "File name pattern. Supports '*' anywhere: 'Program*' (prefix), '*Service' (suffix), 'Chat*Service' (middle). For all files, use '.'." } },
                        { "extension_filter", new ToolDetails { Type = "string", Description = "Exact file extension to narrow the search, as a plain dot-prefixed string with NO wildcard characters — e.g. '.cs', '.csproj', '.js'. Do NOT use glob patterns: pass '.cs', never '*.cs'. If omitted, searches all file types." } },
                        { "project_filter", new ToolDetails { Type = "string", Description = "Substring filter applied case-insensitively to project/path components. It is not an exact full project-name match; for example, 'Tests' can match a project such as 'LMLocal.Tests.E2E' when that text occurs in a relevant path component." } },
                        { "page_token", new ToolDetails { Type = "string", Description = "Page token for next page of results." } },
                        { "max_results", new ToolDetails { Type = "integer", Description = "Number of files to return per page. Default 50, max 500." } }
                    },
                    Required = new List<string> { "file_name" }
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

                var (fileName, fileExtension, projectFilter, pageToken, pageSize, error) = ExtractAndValidateParameters(parameters);
                if (!string.IsNullOrEmpty(error))
                    return ErrorResponse(error);

                int offset = ToolPagination.ParseOffset(pageToken, pageSize);

                string cacheKey = BuildCacheKey(fileName, fileExtension, projectFilter);
                if (_searchCache.TryGet(cacheKey, solutionDir, out CachedToolResults<FileSearchResult> cached))
                    return BuildPageResponse(cached.AllResults, offset, pageSize, cached.Note);

                var filter = new EnumerateSolutionFilesFilter
                {
                    ExtensionFilter = fileExtension,
                    ReturnRelative = true,
                    FileName = fileName,
                    ProjectFilter = projectFilter,
                    Limit = MaxFilesToScan + 1, // one extra item detects that the cap was hit
                    IncludeProjects = true
                };

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                var matchingFiles = (await _solutionFilesScanner.EnumerateSolutionFilesAsync(filter, cancellationToken)).ToList();
                bool limitReached = TrimToLimit(matchingFiles, MaxFilesToScan);
                string note = limitReached
                    ? $"The search was limited: the limit of {MaxFilesToScan} matching files was reached, so more files match than are listed and the list is incomplete. Narrow the search with file_name, extension_filter and/or project_filter."
                    : null;
                if (limitReached)
                    InternalLogger.Warn($"{ToolName}: limit of {MaxFilesToScan} files reached for '{fileName}'.");

                var response = await Task.Run(() =>
                {
                    var results = matchingFiles
                        .Select(file => new FileSearchResult { FilePath = file })
                        .ToList();

                    var cacheEntry = new CachedToolResults<FileSearchResult>
                    {
                        AllResults = results,
                        ItemsScanned = matchingFiles.Count,
                        Note = note
                    };
                    _searchCache.Set(cacheKey, solutionDir, cacheEntry);

                    return BuildPageResponse(cacheEntry.AllResults, offset, pageSize, cacheEntry.Note);

                }, cancellationToken).ConfigureAwait(false);

                return response;
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

        private string BuildCacheKey(string fileName, string fileExtension, string projectFilter)
        {
            var ext = fileExtension ?? string.Empty;
            var proj = projectFilter ?? string.Empty;
            var name = fileName ?? string.Empty;
            return $"{name}||{ext}||{proj}";
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            if (parameters == null) return "Finding... ";

            var fileName = parameters.TryGetValue("file_name", out var fn) ? fn?.ToString() : "";
            var ext = parameters.TryGetValue("extension_filter", out var fe) ? fe?.ToString() : null;
            var project = parameters.TryGetValue("project_filter", out var pf) ? pf?.ToString() : null;
            var pageToken = ToolPagination.ReadToken(parameters);

            var message = $"Finding '{fileName}'";
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
            return "File search finished.";
        }

        private (string fileName, string fileExtension, string projectFilter, string pageToken, int pageSize, string error) ExtractAndValidateParameters(
            Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, null, null, null, DefaultPageSize, "Parameters cannot be null.");
            if (!parameters.TryGetValue("file_name", out object fileNameObj) || !(fileNameObj is string))
                return (null, null, null, null, DefaultPageSize, "Parameter 'file_name' is required and must be a string.");

            var fileName = (string)fileNameObj;
            var fileExtension = parameters.TryGetValue("extension_filter", out object extObj) ? extObj as string : null;
            var projectFilter = parameters.TryGetValue("project_filter", out object projObj) ? projObj as string : null;
            var pageToken = ToolPagination.ReadToken(parameters);

            int pageSize = DefaultPageSize;
            if (parameters.TryGetValue("max_results", out object maxObj) && maxObj != null && int.TryParse(maxObj.ToString(), out int maxVal))
                pageSize = Math.Min(Math.Max(maxVal, 1), MaxPageSize);

            return (fileName, fileExtension, projectFilter, pageToken, pageSize, null);
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
    }
}
