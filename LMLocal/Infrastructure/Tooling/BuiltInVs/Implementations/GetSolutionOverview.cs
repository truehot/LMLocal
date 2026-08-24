using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    internal interface IGetSolutionOverview : IBuiltInTool
    {
    }

    internal class GetSolutionOverview : IGetSolutionOverview
    {
        private readonly IVsDependencies _vsDependencies;

        public string ToolName => "get_solution_overview";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

        public GetSolutionOverview(IVsDependencies vsDependencies)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Returns a high-level summary of the current Visual Studio solution: name, path, project list (with language, file count, test project flag), solution folders, and total file count. Use as a first step to understand the codebase layout before diving into specific files. The projects array is limited to 200 entries; has_more_results=true means some projects were not included. Results are cached for performance — call once and refer to it.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>(),
                    Required = new List<string>()
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                var solution = _vsDependencies.GetSolution();
                if (solution == null)
                {
                    return Error("No solution is currently open");
                }

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
                InternalLogger.Error($"Failed to generate solution overview: {ex}");
                return Error(ex.Message);
            }
        }

        private SolutionOverviewResponse Error(string errorMessage)
        {
            return new SolutionOverviewResponse
            {
                Success = false,
                ErrorMessage = errorMessage,
                SolutionName = null,
                SolutionPath = null,
                TotalProjects = 0,
                TotalFiles = 0,
                HasMoreResults = false,
                Projects = new List<ProjectOverviewItem>(),
                SolutionFolders = new List<string>()
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            return "Loading solution structure... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is SolutionOverviewResponse solutionResult)
                return solutionResult.Success
                    ? $"Loaded {solutionResult.TotalProjects} {Pluralizer.Pluralize(solutionResult.TotalProjects, "project", "projects")}, {solutionResult.TotalFiles} {Pluralizer.Pluralize(solutionResult.TotalFiles, "file", "files")}."
                    : $"Failed to load: {solutionResult.ErrorMessage}";
            return "Solution overview loaded.";
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

            [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
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
