using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    internal interface IFormatDocument : IBuiltInTool { }

    internal class FormatDocument : IFormatDocument
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private DTE2 _dte;

        public string ToolName => "Format_Document";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.FullAccess;

        public FormatDocument(IVsDependencies vsDependencies, IPathResolver pathResolver)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        }

        private DTE2 GetDTE()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_dte == null)
                _dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
            return _dte;
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (parameters?.TryGetValue("file_path", out var fp) != true || !(fp is string filePathParam) || string.IsNullOrEmpty(filePathParam))
                return ErrorResponse("Parameter 'file_path' is required.");

            string solutionDir = _vsDependencies.GetSolutionDirectory();
            if (string.IsNullOrEmpty(solutionDir))
                return ErrorResponse("Solution directory not available.");

            if (!_pathResolver.TryResolveFilePath(filePathParam, solutionDir, out string absolutePath))
                return ErrorResponse($"Cannot resolve file path: {filePathParam}");

            if (!File.Exists(absolutePath))
                return ErrorResponse($"File not found: {absolutePath}");

            var dte = GetDTE();
            if (dte == null)
                return ErrorResponse("DTE service not available.");

            Window window = null;
            bool wasOpen = false;
            try
            {
                foreach (Document doc in dte.Documents)
                {
                    if (string.Equals(doc.FullName, absolutePath, StringComparison.OrdinalIgnoreCase))
                    {
                        wasOpen = true;
                        window = doc.ActiveWindow;
                        doc.Activate();
                        break;
                    }
                }

                if (!wasOpen)
                {
                    window = dte.ItemOperations.OpenFile(absolutePath, EnvDTE.Constants.vsViewKindAny);
                }

                dte.ExecuteCommand("Edit.FormatDocument");
                dte.ActiveDocument.Save();

                return new FormatCodeResponse
                {
                    Success = true,
                    FilePath = absolutePath
                };
            }
            catch (Exception ex)
            {
                return ErrorResponse($"Formatting failed: {ex.Message}");
            }
            finally
            {
                if (!wasOpen && window != null)
                {
                    window.Close(vsSaveChanges.vsSaveChangesNo);
                }
            }
        }

        private static FormatCodeResponse ErrorResponse(string message)
        {
            return new FormatCodeResponse
            {
                Success = false,
                ErrorMessage = message
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var path = parameters?.TryGetValue("file_path", out var p) == true ? p?.ToString() : "file";
            return $"Formatting '{path}'...";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is FormatCodeResponse resp)
                return resp.Success ? "Successfully formatted." : "Formatting failed.";
            return "Formatting completed.";
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Formats a specific code file using Visual Studio's formatting engine. The file is saved after formatting. Requires 'file_path' (absolute or relative to solution). Response fields: success (bool), error_message (string), file_path (string).",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Path to the file to format (absolute or relative to solution root)." } }
                    },
                    Required = new List<string> { "file_path" }
                }
            };
        }
    }

    public class FormatCodeResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("error_message")]
        public string ErrorMessage { get; set; }

        [JsonProperty("file_path")]
        public string FilePath { get; set; }
    }
}