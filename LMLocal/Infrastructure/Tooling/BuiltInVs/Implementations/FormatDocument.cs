using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    internal interface IFormatDocument : IBuiltInTool { }

    internal class FormatDocument : IFormatDocument
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly ISnapshotManager _snapshotManager;
        private readonly IFileSystem _fileSystem;

        public string ToolName => "format_document";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.FullAccess;

        public FormatDocument(IVsDependencies vsDependencies, IPathResolver pathResolver, ISnapshotManager snapshotManager, IFileSystem fileSystem)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            if (parameters?.TryGetValue("file_path", out var fp) != true || !(fp is string filePathParam) || string.IsNullOrEmpty(filePathParam))
                return ErrorResponse("Parameter 'file_path' is required.");

            bool organizeUsings = true;
            if (parameters != null && parameters.TryGetValue("organize_usings", out var organizeValue))
            {
                if (!TryParseBoolean(organizeValue, out organizeUsings))
                    return ErrorResponse("Parameter 'organize_usings' must be a boolean.");
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            string solutionDir = _vsDependencies.GetSolutionDirectory();
            if (string.IsNullOrEmpty(solutionDir))
                return ErrorResponse("Solution directory not available.");

            if (!_pathResolver.TryResolveFilePath(filePathParam, solutionDir, out string absolutePath))
                return ErrorResponse($"Cannot resolve file path: {filePathParam}");

            if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                return ErrorResponse($"File '{absolutePath}' is outside the solution directory.");
            if (!_fileSystem.FileExists(absolutePath))
                return ErrorResponse($"File not found: {absolutePath}");

            var dte = _vsDependencies.GetDTE();
            if (dte == null)
                return ErrorResponse("DTE service not available.");

            Document targetDocument = null;
            Window openedWindow = null;
            bool wasOpen = false;

            try
            {
                foreach (Document doc in dte.Documents)
                {
                    if (string.Equals(doc.FullName, absolutePath, StringComparison.OrdinalIgnoreCase))
                    {
                        targetDocument = doc;
                        wasOpen = true;
                        break;
                    }
                }

                if (!wasOpen)
                {
                    openedWindow = dte.ItemOperations.OpenFile(absolutePath, EnvDTE.Constants.vsViewKindAny);
                    targetDocument = openedWindow.Document;
                }

                if (targetDocument == null)
                    return ErrorResponse("Failed to obtain document reference.");

                targetDocument.Activate();

                string syncError = await DocumentSyncGuard.GetSyncErrorAsync(targetDocument, absolutePath, _fileSystem, cancellationToken);
                if (syncError != null)
                    return ErrorResponse(syncError);

                await _snapshotManager.SnapshotFileAsync(absolutePath, SnapshotChangeStatus.BeforeModify, cancellationToken);

                bool isCSharpFile = string.Equals(Path.GetExtension(absolutePath), ".cs", StringComparison.OrdinalIgnoreCase);
                if (organizeUsings && isCSharpFile)
                    dte.ExecuteCommand("Edit.RemoveAndSort");

                dte.ExecuteCommand("Edit.FormatDocument");

                targetDocument.Save();
                return new FormatCodeResponse
                {
                    Success = true,
                    FilePath = absolutePath
                };
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error during formatting for {absolutePath}: {ex}");
                return ErrorResponse($"Formatting failed: {ex.Message}");
            }
            finally
            {

                if (!wasOpen && openedWindow != null)
                {
                    try
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        openedWindow.Close(vsSaveChanges.vsSaveChangesNo);
                    }
                    catch
                    {
                        InternalLogger.Info($"Failed to close window for file: {absolutePath}");
                    }
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

        private static bool TryParseBoolean(object value, out bool result)
        {
            if (value is bool b)
            {
                result = b;
                return true;
            }

            if (value is string s && bool.TryParse(s, out result))
                return true;

            result = false;
            return false;
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var path = parameters?.TryGetValue("file_path", out var p) == true ? p?.ToString() : "file";
            bool organizeUsings = true;
            if (parameters != null && parameters.TryGetValue("organize_usings", out var organizeValue))
                TryParseBoolean(organizeValue, out organizeUsings);

            bool isCSharpFile = !string.IsNullOrEmpty(path)
                && string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase);

            return organizeUsings && isCSharpFile
                ? $"Organizing usings and formatting '{path}'... "
                : $"Formatting '{path}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is FormatCodeResponse resp)
                return resp.Success ? "Successfully formatted." : $"Formatting failed: {resp.ErrorMessage}";
            return "Formatting completed.";
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Formats a code file using Visual Studio's built-in formatting engine. Normalizes indentation, spacing, and line breaks according to the solution's .editorconfig or VS settings. The file is opened, processed, saved, and closed automatically. Works on any file type supported by the VS editor (C#, XML, JSON, etc.); the organize_usings step optionally removes unused 'using' directives and sorts them first and applies only to C# files. Fails if the file does not exist or cannot be resolved.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Relative path to file." } },
                        { "organize_usings", new ToolDetails { Type = "boolean", Description = "If true, removes unused 'using' directives and sorts the remaining ones before formatting. Applies only to C# files. Defaults to true." } }
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
