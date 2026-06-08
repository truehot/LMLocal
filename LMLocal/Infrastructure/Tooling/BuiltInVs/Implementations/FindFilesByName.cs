using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations.FindFilesByName;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Finds files in the Visual Studio solution by name using case-insensitive substring matching.
    /// Automatically excludes temporary directories (bin, obj, .vs, .git, CopilotBaseline, system temp folders),
    /// minified files (*.min.js, *.min.css, *.udm.js), and other non-source files.
    /// Results are limited to first 500 files to ensure reasonable performance.
    /// Returns FileSearchResultsResponse with results list and has_more_results flag indicating if search was limited.
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
        private const int MaxFilesToScan = 500;

        public string ToolName => "Find_Files_By_Name";

        public FindFilesByName(IVsDependencies vsDependencies, IVsSolutionFilesScanner solutionFilesScanner)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _solutionFilesScanner = solutionFilesScanner ?? throw new ArgumentNullException(nameof(solutionFilesScanner));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = $"Finds files by name within the current Visual Studio solution using case-insensitive substring matching. Response fields: success (bool), error_message (string), results (array of {{file (string)}}), has_more_results (bool), total_files_limit (int). has_more_results indicates more files exist beyond the limit. Limited to {MaxFilesToScan} files. Use optional filters: file_extension (e.g., '.cs'), project_filter. For all files, pass file_name='.'.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_name", new ToolDetails { Type = "string", Description = "The file name or partial file name to search for (case-insensitive substring match). Do NOT use wildcards like '*'.For all files, use '.'" } },
                        { "file_extension", new ToolDetails { Type = "string", Description = "Optional file extension filter (e.g., '.cs', '.json'). If not specified, all file extensions are searched." } },
                        { "project_filter", new ToolDetails { Type = "string", Description = "Optional project name filter. If specified, only files from projects matching this name (case-insensitive substring match) will be searched." } }
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
                var (fileName, fileExtension, projectFilter) = ExtractAndValidateParametersFromDict(parameters);
                return await ExecuteCoreAsync(fileName, fileExtension, projectFilter, cancellationToken);
            }
            catch (Exception ex)
            {
                return new FileSearchResultsResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Results = new List<FileSearchResult>(),
                    HasMoreResults = false,
                    TotalFilesLimit = MaxFilesToScan
                };
            }
        }

        private async Task<FileSearchResultsResponse> ExecuteCoreAsync(
            string fileName,
            string fileExtension,
            string projectFilter,
            CancellationToken cancellationToken)
        {
            var results = new List<FileSearchResult>();
            bool hasMoreResults = false;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            await _vsDependencies.InitializeAsync();

            var filter = new EnumerateSolutionFilesFilter
            {
                ExtensionFilter = fileExtension,
                ReturnRelative = true,
                FileName = fileName,
                ProjectFilter = projectFilter,
                Limit = MaxFilesToScan,
                IncludeProjects = true
            };
            var matchingFiles = (await _solutionFilesScanner.EnumerateSolutionFilesAsync(filter)).ToList();

            if (matchingFiles.Count >= filter.Limit)
                hasMoreResults = true;

            foreach (var file in matchingFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(new FileSearchResult { FilePath = file });
            }

            return new FileSearchResultsResponse
            {
                Results = results,
                HasMoreResults = hasMoreResults,
                TotalFilesLimit = filter.Limit,
                Success = true,
                ErrorMessage = null
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            if (parameters == null) return "Finding... ";

            var fileName = parameters.TryGetValue("file_name", out var fn) ? fn?.ToString() : "";
            var ext = parameters.TryGetValue("file_extension", out var fe) ? fe?.ToString() : null;
            var project = parameters.TryGetValue("project_filter", out var pf) ? pf?.ToString() : null;

            var message = $"Finding '{fileName}'";
            if (!string.IsNullOrEmpty(project))
                message += $" in project '{project}'";
            if (!string.IsNullOrEmpty(ext))
                message += $", with extension '{ext}'";

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
            return $"Found {fileResults.Results.Count} files.";
        }

        private (string fileName, string fileExtension, string projectFilter) ExtractAndValidateParametersFromDict(
            Dictionary<string, object> parameters)
        {
            if (!parameters.TryGetValue("file_name", out object fileNameObj) || !(fileNameObj is string))
                throw new ArgumentException("Parameter 'file_name' is required and must be a string.", nameof(parameters));

            var fileName = (string)fileNameObj;
            var fileExtension = parameters.TryGetValue("file_extension", out object extObj) ? extObj as string : null;
            var projectFilter = parameters.TryGetValue("project_filter", out object projObj) ? projObj as string : null;

            return (fileName, fileExtension, projectFilter);
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

            [JsonProperty("has_more_results")]
            public bool HasMoreResults { get; set; }

            [JsonProperty("total_files_limit")]
            public int TotalFilesLimit { get; set; }

            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("error_message")]
            public string ErrorMessage { get; set; }
        }
    }
}
