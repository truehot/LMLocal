using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    public class ActiveDocumentResponse
    {
        [JsonProperty("file")]
        public string FilePath { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }
    }

    internal interface IActiveDocumentTool : ITool
    {
        Task<ActiveDocumentResponse> ExecuteAsync(IServiceProvider sp, CancellationToken cancellationToken = default);

        Task<string> GetContentAsync();
    }

    internal class ActiveDocumentTool : IActiveDocumentTool
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;

        public string ToolName => "Get_Active_Document_Content";

        public ActiveDocumentTool(IVsDependencies vsDependencies, IPathResolver pathResolver)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Returns the file path and the full content of the currently active text document in Visual Studio. If no document is currently active or the file cannot be read, returns an object with null FilePath and empty Content string. If file read fails, the FilePath is still returned but Content will be empty.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>(),
                    Required = new List<string>()
                }
            };
        }

        public async Task<ActiveDocumentResponse> ExecuteAsync(
            IServiceProvider sp,
            CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            await _vsDependencies.InitializeAsync();
            
            string solutionDir = _vsDependencies.GetSolutionDirectory();

            var (filePath, content) = await GetActiveTextDocumentAsync();
            if (string.IsNullOrEmpty(filePath))
                return new ActiveDocumentResponse { FilePath = null, Content = content };

            
            if (string.IsNullOrEmpty(solutionDir) || !_pathResolver.TryGetRelativePath(filePath, solutionDir, out string relativePath))
                relativePath = filePath;
            return new ActiveDocumentResponse
            {
                FilePath = relativePath,
                Content = content
            };
        }

        public async Task<string> GetContentAsync()
        {
            var (_, text) = await GetActiveTextDocumentAsync();
            return text;
        }

        private async Task<(string filePath, string content)> GetActiveTextDocumentAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (!(await ServiceProvider.GetGlobalServiceAsync(typeof(SVsShellMonitorSelection)) is IVsMonitorSelection monitor))
                return (null, string.Empty);

            monitor.GetCurrentElementValue(
                (uint)VSConstants.VSSELELEMID.SEID_DocumentFrame,
                out object frameObj);

            if (!(frameObj is IVsWindowFrame frame))
                return (null, string.Empty);

            frame.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument, out object pathObj);
            string filePath = pathObj as string;

            if (string.IsNullOrEmpty(filePath))
                return (null, string.Empty);

            try
            {
                string content = File.ReadAllText(filePath);
                return (filePath, content);
            }
            catch
            {
                return (filePath, string.Empty);
            }
        }
    }
}
