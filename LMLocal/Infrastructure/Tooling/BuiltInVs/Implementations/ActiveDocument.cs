using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.FindResults;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json;
using static LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations.ActiveDocument;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Tool interface to retrieve the currently active text document in Visual Studio.
    /// </summary>
    internal interface IActiveDocument : IBuiltInTool
    {
        Task<ActiveDocumentResponse> ExecuteAsync(CancellationToken cancellationToken = default);
        Task<string> GetContentAsync();
    }

    internal class ActiveDocument : IActiveDocument
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;

        public string ToolName => "Get_Active_Document_Content";

        public ActiveDocument(IVsDependencies vsDependencies, IPathResolver pathResolver)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Returns the currently active text document in Visual Studio. Response fields: success (bool), error_message (string), file (string), content (string). If no document is currently active, returns null file with empty content and success=true.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>(),
                    Required = new List<string>()
                }
            };
        }

        public async Task<ActiveDocumentResponse> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                await _vsDependencies.InitializeAsync();

                string solutionDir = _vsDependencies.GetSolutionDirectory();

                var (filePath, content) = await GetActiveTextDocumentAsync(cancellationToken);
                if (string.IsNullOrEmpty(filePath))
                {
                    return new ActiveDocumentResponse
                    {
                        FilePath = null,
                        Content = content,
                        Success = false,
                        ErrorMessage = "No active document found."
                    };
                }

                if (content == null)
                {
                    return new ActiveDocumentResponse
                    {
                        FilePath = filePath,
                        Content = "",
                        Success = false,
                        ErrorMessage = "Failed to retrieve document content."
                    };
                }

                if (string.IsNullOrEmpty(solutionDir) || !_pathResolver.TryGetRelativePath(filePath, solutionDir, out string relativePath))
                {
                    relativePath = filePath;
                }

                return new ActiveDocumentResponse
                {
                    FilePath = relativePath,
                    Content = content,
                    Success = true,
                    ErrorMessage = null
                };
            }
            catch (Exception ex)
            {
                return new ActiveDocumentResponse
                {
                    FilePath = null,
                    Content = string.Empty,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<string> GetContentAsync()
        {
            var (_, text) = await GetActiveTextDocumentAsync();
            return text;
        }

        private async Task<(string filePath, string content)> GetActiveTextDocumentAsync(CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

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
                string content = await ReadFileContentAsync(filePath, cancellationToken).ConfigureAwait(false);
                return (filePath, content);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return (filePath, null);
            }
        }

        private static async Task<string> ReadFileContentAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(filePath))
                return string.Empty;

            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true))
                using (var sr = new StreamReader(fs))
                {
                    var sb = new StringBuilder();
                    char[] buffer = new char[8192];
                    int charsRead;
                    while ((charsRead = await sr.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        sb.Append(buffer, 0, charsRead);
                    }
                    return sb.ToString();
                }
            }
            catch (OperationCanceledException)
            {
                InternalLogger.Warn($"File read operation for '{filePath}' was canceled.");
                throw;
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error reading file '{filePath}': {ex.Message}");
                return string.Empty;
            }
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            return "Reading active document... ";
        }

        public string GetCompletionMessage(object result)
        {
            var docResult = (ActiveDocumentResponse)result;
            if (!docResult.Success)
            {
                return $"Error: {docResult.ErrorMessage}";
            }

            return $"Read '{docResult.FilePath}'.";
        }

        public class ActiveDocumentResponse
        {
            [JsonProperty("file")]
            public string FilePath { get; set; }

            [JsonProperty("content")]
            public string Content { get; set; }

            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("error_message")]
            public string ErrorMessage { get; set; }
        }
    }
}
