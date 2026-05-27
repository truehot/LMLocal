using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Response object for solution overview tool.
    /// </summary>
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

        [JsonProperty("truncated")]
        public bool Truncated { get; set; }

        [JsonProperty("projects")]
        public List<ProjectOverviewItem> Projects { get; set; }

        [JsonProperty("solution_folders")]
        public List<string> SolutionFolders { get; set; }
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

    internal interface IGetSolutionOverviewTool : ITool
    {
        Task<object> ExecuteAsync(IServiceProvider sp, CancellationToken cancellationToken = default);
    }

    internal class GetSolutionOverviewTool : IGetSolutionOverviewTool
    {
        private readonly IVsDependencies _vsDependencies;

        public string ToolName => "Get_Solution_Overview";

        public GetSolutionOverviewTool(IVsDependencies vsDependencies)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Returns a high-level summary of the solution structure: solution name, total project count, estimated file count, list of projects with names, programming languages, relative paths (use in project_filter), file counts, and test project flags. Also lists solution folders. Results limited to first 200 projects; truncated flag indicates more projects exist. Cached for performance. Use as a navigation starting point.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>(),
                    Required = new List<string>()
                }
            };
        }

        public async Task<object> ExecuteAsync(IServiceProvider sp, CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            await _vsDependencies.InitializeAsync();

            var solution = _vsDependencies.GetSolution();
            if (solution == null)
                throw new InvalidOperationException("No solution is currently open");

            var overview = SolutionInspector.GetSolutionOverview(solution, maxProjects: 200);

            var response = new SolutionOverviewResponse
            {
                SolutionName = overview.SolutionName,
                SolutionPath = overview.SolutionPath,
                TotalProjects = overview.TotalProjects,
                TotalFiles = overview.TotalFiles,
                Truncated = overview.Truncated,
                SolutionFolders = overview.SolutionFolders,
                Projects = new List<ProjectOverviewItem>()
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
    }
}
