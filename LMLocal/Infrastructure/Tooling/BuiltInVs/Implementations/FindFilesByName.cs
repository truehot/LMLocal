using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations.FindFilesByName;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Finds files in the Visual Studio solution by name using case-insensitive substring matching.
    /// Automatically excludes temporary directories (bin, obj, .vs, .git, CopilotBaseline, system temp folders),
    /// minified files (*.min.js, *.min.css, *.udm.js), and other non-source files.
    /// </summary>

    internal interface IFindFilesByName : IBuiltInTool
    {
        Task<FileSearchResultsResponse> ExecuteAsync(
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken = default);
    }

    internal class FindFilesByName : IFindFilesByName
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IVsSolutionFilesScanner _solutionFilesScanner;
        private readonly ISearchResultCache _searchCache;
        private const int DefaultTake = 100;
        private const int MaxFilesToScan = 1500;

        public string ToolName => "Find_Files_By_Name";

        public FindFilesByName(
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
                Description = $"Finds files by name within the current Visual Studio solution using case-insensitive substring matching. Response fields: success (bool), error_message (string), results (array of {{file (string)}}), total_files (int), next_page_token (string or null). If 'next_page_token' is not null, more results exist; pass it as 'page_token' to get next page. Results are paginated by {DefaultTake} files per page. Limited to scanning first {MaxFilesToScan} files. Use optional filters: file_extension (e.g., '.cs'), project_filter. For all files, pass file_name='.'.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_name", new ToolDetails { Type = "string", Description = "The file name or partial file name to search for (case-insensitive substring match). Do NOT use wildcards like '*'.For all files, use '.'" } },
                        { "file_extension", new ToolDetails { Type = "string", Description = "File extension filter (e.g., '.cs', '.json'). If not specified, all file extensions are searched." } },
                        { "project_filter", new ToolDetails { Type = "string", Description = "Project name filter. If specified, only files from projects matching this name (case-insensitive substring match) will be searched. Use it to narrow result set." } },
                        { "page_token", new ToolDetails { Type = "string", Description = "Page token for fetching a specific page of results. Leave empty or null for the first page. Use the next_page_token value from the previous response to get the next page." } }
                    },
                    Required = new List<string> { "file_name" }
                }
            };
        }

        public async Task<FileSearchResultsResponse> ExecuteAsync(
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (fileName, fileExtension, projectFilter, pageToken) = ExtractAndValidateParametersFromDict(parameters);
                int pageNumber = string.IsNullOrEmpty(pageToken) || !int.TryParse(pageToken, out var pn) ? 0 : pn;
                int skip = pageNumber * DefaultTake;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                await _vsDependencies.InitializeAsync();
                string solutionDir = _vsDependencies.GetSolutionDirectory();

                string cacheKey = BuildCacheKey(fileName, fileExtension, projectFilter);
                if (_searchCache.TryGet(cacheKey, solutionDir, out CachedToolResults<FileSearchResult> cached))
                {
                    var page = cached.AllResults.Skip(skip).Take(DefaultTake).ToList();
                    string nextToken = (skip + DefaultTake) < cached.AllResults.Count ? (pageNumber + 1).ToString() : null;
                    return new FileSearchResultsResponse
                    {
                        Results = page,
                        NextPageToken = nextToken,
                        TotalFiles = cached.AllResults.Count,
                        Success = true,
                        ErrorMessage = null
                    };
                }

                var filter = new EnumerateSolutionFilesFilter
                {
                    ExtensionFilter = fileExtension,
                    ReturnRelative = true,
                    FileName = fileName,
                    ProjectFilter = projectFilter,
                    Limit = MaxFilesToScan,
                    IncludeProjects = true
                };
                var matchingFiles = (await _solutionFilesScanner.EnumerateSolutionFilesAsync(filter, cancellationToken)).ToList();

                var response = await Task.Run(() =>
                {
                    var results = matchingFiles
                        .Select(file => new FileSearchResult { FilePath = file })
                        .ToList();

                    var cacheEntry = new CachedToolResults<FileSearchResult>
                    {
                        AllResults = results,
                        ItemsScanned = matchingFiles.Count
                    };
                    _searchCache.Set(cacheKey, solutionDir, cacheEntry);

                    var page = cacheEntry.AllResults.Skip(skip).Take(DefaultTake).ToList();
                    string nextToken = (skip + DefaultTake) < cacheEntry.AllResults.Count ? (pageNumber + 1).ToString() : null;
                    return new FileSearchResultsResponse
                    {
                        Results = page,
                        NextPageToken = nextToken,
                        TotalFiles = cacheEntry.AllResults.Count,
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

        private static FileSearchResultsResponse ErrorResponse(string message)
        {
            return new FileSearchResultsResponse
            {
                Success = false,
                ErrorMessage = message,
                Results = new List<FileSearchResult>(),
                NextPageToken = null,
                TotalFiles = 0
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
            var ext = parameters.TryGetValue("file_extension", out var fe) ? fe?.ToString() : null;
            var project = parameters.TryGetValue("project_filter", out var pf) ? pf?.ToString() : null;
            var pageToken = parameters.TryGetValue("page_token", out var t) ? t?.ToString() : null;

            var message = $"Finding '{fileName}'";
            if (!string.IsNullOrEmpty(project))
                message += $" in project '{project}'";
            if (!string.IsNullOrEmpty(ext))
                message += $", with extension '{ext}'";
            if (!string.IsNullOrEmpty(pageToken))
                message += $" (page {pageToken})";

            message += "... ";
            return message;
        }

        public string GetCompletionMessage(object result)
        {
            var fileResults = (FileSearchResultsResponse)result;
            if (!fileResults.Success)
            {
                return $"Error: {fileResults.ErrorMessage}";
            }
            return $"Found {fileResults.Results.Count} files on this page (total: {fileResults.TotalFiles} files).";
        }

        private (string fileName, string fileExtension, string projectFilter, string pageToken) ExtractAndValidateParametersFromDict(
            Dictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("file_name", out object fileNameObj) || !(fileNameObj is string))
                throw new ArgumentException("Parameter 'file_name' is required and must be a string.", nameof(parameters));

            var fileName = (string)fileNameObj;
            var fileExtension = parameters.TryGetValue("file_extension", out object extObj) ? extObj as string : null;
            var projectFilter = parameters.TryGetValue("project_filter", out object projObj) ? projObj as string : null;
            var pageToken = parameters.TryGetValue("page_token", out object tokenObj) ? tokenObj as string : null;

            return (fileName, fileExtension, projectFilter, pageToken);
        }

        public class FileSearchResult
        {
            [JsonProperty("file")]
            public string FilePath { get; set; }
        }

        public class FileSearchResultsResponse
        {
            [JsonProperty("results")]
            public List<FileSearchResult> Results { get; set; }

            [JsonProperty("next_page_token")]
            public string NextPageToken { get; set; }

            [JsonProperty("total_files")]
            public int TotalFiles { get; set; }

            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("error_message")]
            public string ErrorMessage { get; set; }
        }
    }
}
