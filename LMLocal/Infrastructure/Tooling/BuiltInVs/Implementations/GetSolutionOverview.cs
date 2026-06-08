using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations.GetSolutionOverview;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Tool interface to obtain a high-level summary of the currently opened Visual Studio solution.
    /// </summary>
    internal interface IGetSolutionOverview : IBuiltInTool
    {
        Task<SolutionOverviewResponse> ExecuteAsync(CancellationToken cancellationToken = default);
    }

    internal class GetSolutionOverview : IGetSolutionOverview
    {
        private readonly IVsDependencies _vsDependencies;

        public string ToolName => "Get_Solution_Overview";

        public GetSolutionOverview(IVsDependencies vsDependencies)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Returns a high-level summary of the current Visual Studio solution structure. Response fields: success (bool), error_message (string), solution_name (string), solution_path (string), total_projects (int), total_files (int), has_more_results (bool), projects (array of {name (string), language (string), path (string), file_count (int), is_test_project (bool)}), solution_folders (array of string). has_more_results indicates more projects exist beyond the 200 project limit. Cached for performance.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>(),
                    Required = new List<string>()
                }
            };
        }

        public async Task<SolutionOverviewResponse> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                await _vsDependencies.InitializeAsync();

                var solution = _vsDependencies.GetSolution();
                if (solution == null)
                    return new SolutionOverviewResponse
                    {
                        Success = false,
                        ErrorMessage = "No solution is currently open",
                        SolutionName = null,
                        SolutionPath = null,
                        TotalProjects = 0,
                        TotalFiles = 0,
                        HasMoreResults = false,
                        Projects = new List<ProjectOverviewItem>(),
                        SolutionFolders = new List<string>()
                    };

                var overview = SolutionInspector.GetSolutionOverview(solution, maxProjects: 200);

                var response = new SolutionOverviewResponse
                {
                    SolutionName = overview.SolutionName,
                    SolutionPath = overview.SolutionPath,
                    TotalProjects = overview.TotalProjects,
                    TotalFiles = overview.TotalFiles,
                    HasMoreResults = overview.Truncated,
                    SolutionFolders = overview.SolutionFolders,
                    Projects = new List<ProjectOverviewItem>(),
                    Success = true,
                    ErrorMessage = null
                };

                foreach (var project in overview.Projects)
                {
                    response.Projects.Add(new ProjectOverviewItem
                    {
                        Name = project.Name,
                        Language = project.Language,
                        Path = project.Path,
                        FileCount = project.FileCount,
                        IsTestProject = project.IsTestProject
                    });
                }

                return response;
            }
            catch (Exception ex)
            {
                return new SolutionOverviewResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    SolutionName = null,
                    SolutionPath = null,
                    TotalProjects = 0,
                    TotalFiles = 0,
                    HasMoreResults = false,
                    Projects = new List<ProjectOverviewItem>(),
                    SolutionFolders = new List<string>()
                };
            }
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            return "Loading solution structure... ";
        }

        public string GetCompletionMessage(object result)
        {
            var solutionResult = (SolutionOverviewResponse)result;
            if (!solutionResult.Success)
            {
                return $"Error: {solutionResult.ErrorMessage}";
            }
            return $"Loaded {solutionResult.TotalProjects} projects, {solutionResult.TotalFiles} files.";
        }

        public class SolutionOverviewResponse
        {
            [JsonProperty("solution_name")]
            public string SolutionName { get; set; }

            [JsonProperty("solution_path")]
            public string SolutionPath { get; set; }

            [JsonProperty("total_projects")]
            public int TotalProjects { get; set; }

            [JsonProperty("total_files")]
            public int TotalFiles { get; set; }

            [JsonProperty("has_more_results")]
            public bool HasMoreResults { get; set; }

            [JsonProperty("projects")]
            public List<ProjectOverviewItem> Projects { get; set; }

            [JsonProperty("solution_folders")]
            public List<string> SolutionFolders { get; set; }

            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("error_message")]
            public string ErrorMessage { get; set; }
        }

        public class ProjectOverviewItem
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("language")]
            public string Language { get; set; }

            [JsonProperty("path")]
            public string Path { get; set; }

            [JsonProperty("file_count")]
            public int FileCount { get; set; }

            [JsonProperty("is_test_project")]
            public bool IsTestProject { get; set; }
        }
    }
}
