using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
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
            if (!FilePathList.TryParse(parameters, out List<string> filePaths, out string pathsError, ExistingFileCheck()))
                return ErrorResponse(pathsError);

            bool organizeUsings = true;
            if (parameters != null && parameters.TryGetValue("organize_usings", out var organizeValue))
            {
                if (!TryParseBoolean(organizeValue, out organizeUsings))
                    return ErrorResponse("Parameter 'organize_usings' must be a boolean.");
            }

            if (filePaths.Count == 1)
                return await FormatOneAsync(filePaths[0], organizeUsings, cancellationToken);

            var batch = new FileBatchResponse<FormatCodeResponse> { TotalFiles = filePaths.Count };
            foreach (string filePath in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                FormatCodeResponse one = await FormatOneAsync(filePath, organizeUsings, cancellationToken);
                if (!one.Success && string.IsNullOrEmpty(one.FilePath))
                    one.FilePath = filePath;

                batch.Files.Add(one);
                if (one.Success)
                    batch.Succeeded++;
            }

            batch.Success = batch.Succeeded > 0;
            if (!batch.Success)
                batch.ErrorMessage = "None of the files could be formatted; see 'files' for each error.";
            return batch;
        }

        /// <summary>
        /// Lets <see cref="FilePathList"/> keep a single path that contains a comma when that path is an existing file.
        /// </summary>
        private Func<string, bool> ExistingFileCheck()
        {
            string solutionDir = _vsDependencies.GetSolutionDirectory();
            if (string.IsNullOrEmpty(solutionDir))
                return null;

            return path => _pathResolver.TryResolveFilePath(path, solutionDir, out string absolutePath)
                           && _fileSystem.FileExists(absolutePath);
        }

        private async Task<FormatCodeResponse> FormatOneAsync(string filePathParam, bool organizeUsings, CancellationToken cancellationToken)
        {
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
                    openedWindow = dte.ItemOperations.OpenFile(absolutePath, EnvDTE.Constants.vsViewKindTextView);
                    targetDocument = openedWindow.Document;
                }

                if (targetDocument == null)
                    return ErrorResponse("Failed to obtain document reference.");

                targetDocument.Activate();

                string syncError = await DocumentSyncGuard.GetSyncErrorAsync(targetDocument, absolutePath, _fileSystem, cancellationToken);
                if (syncError != null)
                    return ErrorResponse(syncError);

                // VS has no formatter for some file types (e.g. Markdown): Edit.FormatDocument is then unavailable and
                // executing it throws COM E_FAIL. Report a clear no-op instead; nothing is snapshotted or saved.
                if (IsCommandAvailable(dte, "Edit.FormatDocument") == false)
                {
                    string extension = Path.GetExtension(absolutePath);
                    InternalLogger.Info($"format_document: formatting is not available for '{absolutePath}'; skipped.");
                    return new FormatCodeResponse
                    {
                        Success = true,
                        FilePath = absolutePath,
                        Skipped = true,
                        Note = $"Visual Studio has no formatter for '{extension}' files; the file was left unchanged."
                    };
                }

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

        /// <summary>
        /// True/false when VS reports whether <paramref name="commandName"/> is available for the active documen.
        /// </summary>
        private static bool? IsCommandAvailable(DTE dte, string commandName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return dte.Commands.Item(commandName, -1)?.IsAvailable;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"format_document: cannot query availability of '{commandName}': {ex.Message}");
                return null;
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
            if (FilePathList.TryParse(parameters, out List<string> paths, out _) && paths.Count > 1)
                return $"Formatting {paths.Count} files... ";

            var path = paths != null && paths.Count == 1 ? paths[0] : "file";
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
            if (result is FileBatchResponse<FormatCodeResponse> batch)
            {
                int skipped = batch.Files.FindAll(f => f.Success && f.Skipped).Count;
                int failed = batch.TotalFiles - batch.Succeeded;
                string msg = $"Formatted {batch.Succeeded - skipped} of {batch.TotalFiles} files";
                if (skipped > 0) msg += $", {skipped} skipped";
                if (failed > 0) msg += $", {failed} failed";
                return msg + ".";
            }

            if (result is FormatCodeResponse resp)
            {
                if (resp.Success && resp.Skipped)
                    return "Formatting skipped: not supported for this file type.";
                return resp.Success ? "Successfully formatted." : $"Formatting failed: {resp.ErrorMessage}";
            }
            return "Formatting completed.";
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Formats a code file using Visual Studio's built-in formatting engine. Normalizes indentation, spacing, and line breaks according to the solution's .editorconfig or VS settings. The file is opened, processed, saved, and closed automatically. Works on any file type supported by the VS editor (C#, XML, JSON, etc.); for a file type VS cannot format (e.g. Markdown) the file is left unchanged and the response has skipped=true; the organize_usings step optionally removes unused 'using' directives and sorts them first and applies only to C# files. Fails if the file does not exist or cannot be resolved. Several files: one call with comma-separated paths in file_path.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Relative path to file, or several comma-separated paths (\"A.cs, B.cs\")." } },
                        { "organize_usings", new ToolDetails { Type = "boolean", Description = "If true, removes unused 'using' directives and sorts the remaining ones before formatting. Applies only to C# files. Defaults to true." } },
                        { "file_paths", new ToolDetails { Type = "array", Description = "Optional: more paths as an array.", Items = new ToolDetails { Type = "string" } } }
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

        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }

        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        /// <summary>True when VS has no formatter for the file type; the file was left unchanged.</summary>
        [JsonProperty("skipped", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Skipped { get; set; }

        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note { get; set; }
    }
}
