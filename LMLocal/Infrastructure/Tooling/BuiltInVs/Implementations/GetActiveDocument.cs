using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Tool interface to retrieve the currently active text document in Visual Studio.
    /// </summary>
    internal interface IGetActiveDocument : IBuiltInTool
    {
        Task<string> GetContentAsync();
    }

    internal class GetActiveDocument : IGetActiveDocument
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IFileSystem _fileSystem;

        public string ToolName => "get_active_document";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

        public GetActiveDocument(IVsDependencies vsDependencies, IPathResolver pathResolver, IFileSystem fileSystem)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Returns the currently active text document in Visual Studio — the file the user is viewing/editing. Use when the user asks to 'fix this' or 'refactor this file' and you need to see what they're looking at without guessing the path. If no document is open, returns success=false. If the document content cannot be read (e.g., unsaved changes conflict), returns success=false with an error_message. Example: (no parameters) → {\"success\":true,\"file_path\":\"src/Program.cs\",\"content\":\"using System;\\n...\",\"error_message\":null}.",
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
                if (!_vsDependencies.IsSolutionOpen)
                    return Error("No solution is currently open.");

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
                string content = await _fileSystem.ReadAllTextWithSharedReadAsync(filePath, cancellationToken).ConfigureAwait(false);
                return (filePath, content);
            }
            catch (OperationCanceledException)
            {
                InternalLogger.Warn($"Operation to get active document was canceled.");
                throw;
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error retrieving active document content: {ex.Message}");
                return (filePath, null);
            }
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            return "Reading active document... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is ActiveDocumentResponse docResult)
                return docResult.Success ? $"Read '{docResult.FilePath}'." : $"Error: {docResult.ErrorMessage}";
            return "Read active document finished.";
        }

        private static ActiveDocumentResponse Error(string message)
        {
            return new ActiveDocumentResponse
            {
                FilePath = null,
                Content = string.Empty,
                Success = false,
                ErrorMessage = message
            };
        }

        public class ActiveDocumentResponse
        {
            [JsonProperty("file_path")]
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