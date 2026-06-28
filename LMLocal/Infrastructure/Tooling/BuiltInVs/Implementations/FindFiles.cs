using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
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
        private const int DefaultTake = 100;
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
                Description = "Finds files by name within the current Visual Studio solution using case-insensitive matching. Supports wildcard '*' in any position: 'Program*' (starts with), '*Service' (ends with), 'Chat*Service' (starts with 'Chat' and ends with 'Service'). Use to locate files when you know part of the name. Results are paginated (100 files per page); if next_page_token is not null, call again with page_token set to that value to get the next page. Limited to scanning first 1500 files. Example: {\"file_name\":\"Chat*Service\",\"file_extension\":\".cs\"}.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_name", new ToolDetails { Type = "string", Description = "File name pattern. Supports '*' anywhere: 'Program*' (prefix), '*Service' (suffix), 'Chat*Service' (middle). For all files, use '.'." } },
                        { "file_extension", new ToolDetails { Type = "string", Description = "Extension filter (e.g., '.cs'). If not specified or file_name='.', all extensions are searched." } },
                        { "project_filter", new ToolDetails { Type = "string", Description = "Project name filter (substring match)." } },
                        { "page_token", new ToolDetails { Type = "string", Description = "Page token for next page of results." } }
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
                    return Error("No solution is currently open.");

                string solutionDir = _vsDependencies.GetSolutionDirectory();

                var (fileName, fileExtension, projectFilter, pageToken, error) = ExtractAndValidateParameters(parameters);
                if (!string.IsNullOrEmpty(error))
                    return Error(error);

                int pageNumber = string.IsNullOrEmpty(pageToken) || !int.TryParse(pageToken, out var pn) ? 0 : pn;
                int skip = pageNumber * DefaultTake;

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

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
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
                InternalLogger.Error($"Error in {ToolName}: {ex}");
                return Error(ex.Message);
            }
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
            if (!string.IsNullOrEmpty(pageToken) && int.TryParse(pageToken, out var pageTokenValue) && pageTokenValue > 0)
                message += $" (page {++pageTokenValue})";

            message += "... ";
            return message;
        }

        public string GetCompletionMessage(object result)
        {
            if (result is FileSearchResultsResponse fileResults)
            {
                if (!fileResults.Success)
                    return $"Error: {fileResults.ErrorMessage}";

                var message = $"Found {fileResults.Results.Count} files";
                if (fileResults.TotalFiles > 0 && fileResults.Results.Count < fileResults.TotalFiles)
                    message += $" (total: {fileResults.TotalFiles} files)";
                message += ".";
                return message;
            }
            return "File search finished.";
        }

        private (string fileName, string fileExtension, string projectFilter, string pageToken, string error) ExtractAndValidateParameters(
            Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, null, null, null, "Parameters cannot be null.");
            if (!parameters.TryGetValue("file_name", out object fileNameObj) || !(fileNameObj is string))
                return (null, null, null, null, "Parameter 'file_name' is required and must be a string.");

            var fileName = (string)fileNameObj;
            var fileExtension = parameters.TryGetValue("file_extension", out object extObj) ? extObj as string : null;
            var projectFilter = parameters.TryGetValue("project_filter", out object projObj) ? projObj as string : null;
            var pageToken = parameters.TryGetValue("page_token", out object tokenObj) ? tokenObj as string : null;

            return (fileName, fileExtension, projectFilter, pageToken, null);
        }

        private static FileSearchResultsResponse Error(string message)
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
