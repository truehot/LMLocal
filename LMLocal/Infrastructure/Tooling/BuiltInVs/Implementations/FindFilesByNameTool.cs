using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
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
    }

    /// <summary>
    /// Finds files in the Visual Studio solution by name using case-insensitive substring matching.
    /// Automatically excludes temporary directories (bin, obj, .vs, .git, CopilotBaseline, system temp folders),
    /// minified files (*.min.js, *.min.css, *.udm.js), and other non-source files.
    /// Results are limited to first 100 files to ensure reasonable performance.
    /// Returns FileSearchResultsResponse with results list and has_more_results flag indicating if search was limited.
    /// </summary>

    internal interface IFindFilesByNameTool : ITool
    {
        Task<object> ExecuteAsync(
            IServiceProvider sp,
            string fileName,
            string fileExtension = null,
            CancellationToken cancellationToken = default,
            string projectFilter = null);
    }

    internal class FindFilesByNameTool : IFindFilesByNameTool
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IVsSolutionFilesScanner _solutionFilesScanner;
        private const int MaxFilesToScan = 100;

        public string ToolName => "Find_Files_By_Name";

        public FindFilesByNameTool(IVsDependencies vsDependencies, IVsSolutionFilesScanner solutionFilesScanner)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _solutionFilesScanner = solutionFilesScanner ?? throw new ArgumentNullException(nameof(solutionFilesScanner));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = $"Finds files in the Visual Studio solution by file name. Returns a list of file paths. Search is limited to the first {MaxFilesToScan} files in the solution. File name matching is case-insensitive substring match (e.g., 'config' matches 'AppConfig.cs' and 'config.json'). Results include has_more_results flag indicating if more files exist beyond the limit.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_name", new ToolDetails { Type = "string", Description = "The file name or partial file name to search for (case-insensitive substring match)." } },
                        { "file_extension", new ToolDetails { Type = "string", Description = "Optional file extension filter (e.g., '.cs', '.json'). If not specified, all file extensions are searched." } },
                        { "project_filter", new ToolDetails { Type = "string", Description = "Optional project name filter. If specified, only files from projects matching this name (case-insensitive substring match) will be searched." } }
                    },
                    Required = new List<string> { "file_name" }
                }
            };
        }

        public async Task<object> ExecuteAsync(
            IServiceProvider sp,
            string fileName,
            string fileExtension = null,
            CancellationToken cancellationToken = default,
            string projectFilter = null)
        {
            if (string.IsNullOrEmpty(fileName))
                return new FileSearchResultsResponse { Results = new List<FileSearchResult>(), HasMoreResults = false, TotalFilesLimit = MaxFilesToScan };

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
                Limit = MaxFilesToScan
            };
            var matchingFiles = _solutionFilesScanner.EnumerateSolutionFiles(filter).ToList();

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
                TotalFilesLimit = filter.Limit
            };
        }
    }
}
