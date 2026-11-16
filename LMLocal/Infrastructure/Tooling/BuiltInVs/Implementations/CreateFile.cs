using LMLocal.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.Syntax;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    internal interface ICreateFile : IBuiltInTool
    {
    }

    internal class CreateFile : ICreateFile
    {
        private const string FilePathParameter = "file_path";
        private const string ContentParameter = "content";
        private const string CopyFromParameter = "copy_from";
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IFileSystem _fileSystem;
        private readonly ISnapshotManager _snapshotManager;
        private readonly ISyntaxCheckerFactory _syntaxFactory;
        public string ToolName => "create_file";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.FullAccess;

        public CreateFile(IVsDependencies vsDependencies, IPathResolver pathResolver, IFileSystem fileSystem, ISnapshotManager snapshotManager, ISyntaxCheckerFactory syntaxFactory)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
            _syntaxFactory = syntaxFactory ?? throw new ArgumentNullException(nameof(syntaxFactory));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Creates a new file — either with the given content, or as a byte-for-byte copy of an existing file via copy_from. Exactly one of content/copy_from is required. To copy an existing file, pass copy_from instead of content — byte-for-byte, no need to read the file first; the copy keeps the original encoding, BOM and line endings. Fails if the file already exists — use replace_file_content to update an existing file instead. Parent directories are created automatically if they don't exist. Path relative to solution root. A copy is a new file in the project, so in SDK-style projects it joins the compilation immediately — rename the namespace/type names next. After creating .cs files, use set_file_project_status to register them in the .csproj project.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { FilePathParameter, new ToolDetails { Type = "string", Description = "Relative path to file to create." } },
                        { ContentParameter, new ToolDetails { Type = "string", Description = "Initial content for the file. Required unless copy_from is passed." } },
                        { CopyFromParameter, new ToolDetails { Type = "string", Description = "Relative path to an existing file to copy byte-for-byte (same format as file_path). Requires content to be omitted." } }
                    },
                    Required = new List<string> { FilePathParameter }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                string pruneRejection = PrunedContentGuard.Validate(parameters);
                if (pruneRejection != null)
                    return ErrorReponse(pruneRejection);

                var (filePath, content, copyFrom, error) = ExtractAndValidateParameters(parameters);
                if (!string.IsNullOrEmpty(error))
                    return ErrorReponse(error);

                cancellationToken.ThrowIfCancellationRequested();

                if (!_vsDependencies.IsSolutionOpen)
                    return ErrorReponse("No solution is currently open.");

                string solutionDir = _vsDependencies.GetSolutionDirectory();

                if (!_pathResolver.TryResolveFilePath(filePath, solutionDir, out string absolutePath))
                    return ErrorReponse($"Failed to resolve file path: {filePath}");

                if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                    return ErrorReponse($"File '{filePath}' is outside the solution directory '{solutionDir}'.");

                try
                {
                    _fileSystem.ValidateFilePath(absolutePath);
                }
                catch (ArgumentException ex)
                {
                    return ErrorReponse($"Invalid file path: {ex.Message}");
                }

                string absoluteSourcePath = null;
                if (copyFrom != null)
                {
                    if (!_pathResolver.TryResolveFilePath(copyFrom, solutionDir, out absoluteSourcePath))
                        return ErrorReponse($"Failed to resolve source path: {copyFrom}");

                    if (!_pathResolver.IsPathInsideDirectory(absoluteSourcePath, solutionDir))
                        return ErrorReponse($"Source file '{copyFrom}' is outside the solution directory '{solutionDir}'.");

                    try
                    {
                        _fileSystem.ValidateFilePath(absoluteSourcePath);
                    }
                    catch (ArgumentException ex)
                    {
                        return ErrorReponse($"Invalid source file path: {ex.Message}");
                    }

                    if (string.Equals(absoluteSourcePath, absolutePath, StringComparison.OrdinalIgnoreCase))
                        return ErrorReponse($"Cannot copy file onto itself: {filePath}");

                    if (_fileSystem.DirectoryExists(absoluteSourcePath))
                        return ErrorReponse($"Source '{copyFrom}' is a directory. Only files can be copied.");

                    if (!_fileSystem.FileExists(absoluteSourcePath))
                        return ErrorReponse($"Source file not found: {copyFrom}");
                }

                if (_fileSystem.FileExists(absolutePath))
                    return ErrorReponse($"File already exists: {filePath}");


                await _snapshotManager.SnapshotFileAsync(absolutePath, SnapshotChangeStatus.BeforeCreate, cancellationToken).ConfigureAwait(false);

                _fileSystem.EnsureDirectoryExistsForFile(absolutePath);

                if (copyFrom != null)
                    return await CopyFromExistingFileAsync(filePath, absolutePath, copyFrom, absoluteSourcePath, solutionDir, cancellationToken).ConfigureAwait(false);

                string normalizedContent = content.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

                await _fileSystem.WriteAllBytesWithEncodingAsync(absolutePath, normalizedContent, Encoding.UTF8, hasBom: true, cancellationToken).ConfigureAwait(false);

                string[] syntaxErrors = null;
                var checker = _syntaxFactory.GetChecker(absolutePath);
                if (checker != null && !checker.IsSyntaxValid(normalizedContent, out var errors))
                {
                    syntaxErrors = errors.Select(e => $"{e.Id}: {e.GetMessage()}").ToArray();
                    InternalLogger.Info($"Syntax errors detected after creation in {absolutePath}:\n{string.Join("\n", syntaxErrors)}");
                }

                _pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath);

                return new CreateFileResponse
                {
                    Success = true,
                    FilePath = relativePath ?? absolutePath,
                    CreatedSuccessfully = true,
                    SyntaxErrors = syntaxErrors
                };
            }
            catch (OperationCanceledException)
            {
                InternalLogger.Info($"Operation in {ToolName} was cancelled.");
                return ErrorReponse($"Operation in {ToolName} was cancelled.");
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error in {ToolName}: {ex}");
                return ErrorReponse($"Error: {ex.Message}");
            }
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var filePath = (parameters?.TryGetValue(FilePathParameter, out var f) == true ? f?.ToString() : "") ?? "";
            var copyFrom = (parameters?.TryGetValue(CopyFromParameter, out var c) == true ? c?.ToString() : "") ?? "";

            if (!string.IsNullOrWhiteSpace(copyFrom))
                return $"Copying '{copyFrom}' -> '{filePath}'... ";

            return $"Creating file '{filePath}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is CreateFileResponse response)
            {
                if (response.CopiedFrom != null)
                    return "File copied.";

                if (!response.Success)
                    return $"File creation failed: {response.ErrorMessage}";

                string msg = "File created";
                if (response.SyntaxErrors != null && response.SyntaxErrors.Length > 0)
                    msg += $" with {response.SyntaxErrors.Length} syntax  {Pluralizer.Pluralize(response.SyntaxErrors.Length, "error", "errors")}";
                msg += ".";

                return msg;
            }
            return "File creation finished.";
        }

        private (string filePath, string content, string copyFrom, string error) ExtractAndValidateParameters(
            Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, null, null, "Parameters cannot be null.");

            if (!parameters.TryGetValue(FilePathParameter, out object filePathObj) || !(filePathObj is string filePath) || string.IsNullOrEmpty(filePath))
                return (null, null, null, "file_path parameter is required and must be a string.");

            parameters.TryGetValue(ContentParameter, out object contentObj);
            parameters.TryGetValue(CopyFromParameter, out object copyFromObj);

            bool hasCopyFrom = copyFromObj != null && !(copyFromObj is string copyFromText && string.IsNullOrWhiteSpace(copyFromText));
            bool hasContent = contentObj != null && !(hasCopyFrom && contentObj is string contentText && contentText.Length == 0);

            if (hasContent && hasCopyFrom)
                return (null, null, null, "Provide either 'content' or 'copy_from', not both.");

            if (!hasContent && !hasCopyFrom)
                return (null, null, null, "Provide either 'content' (string) or 'copy_from' (string).");

            if (hasContent)
            {
                if (!(contentObj is string content))
                    return (null, null, null, "content parameter must be a string.");

                return (filePath, content, null, null);
            }

            if (!(copyFromObj is string copyFrom) || string.IsNullOrWhiteSpace(copyFrom))
                return (null, null, null, "copy_from parameter must be a non-empty string (relative path to the file to copy).");

            return (filePath, null, copyFrom, null);
        }

        /// <summary>
        /// Copy branch: the file is copied byte-for-byte, so encoding, BOM and line endings are preserved.
        /// </summary>
        private async Task<CreateFileResponse> CopyFromExistingFileAsync(
            string filePath, string absolutePath, string copyFrom, string absoluteSourcePath, string solutionDir,
            CancellationToken cancellationToken)
        {
            _pathResolver.TryGetRelativePath(absoluteSourcePath, solutionDir, out string relativeSourcePath);

            try
            {
                await _fileSystem.CopyFileAsync(absoluteSourcePath, absolutePath, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryDeletePartialCopy(absolutePath);
                throw;
            }
            catch (Exception ex)
            {
                TryDeletePartialCopy(absolutePath);
                InternalLogger.Error($"{ToolName}: failed to copy '{absoluteSourcePath}' to '{absolutePath}': {ex}");
                return ErrorReponse($"Failed to copy '{copyFrom}' to '{filePath}': {ex.Message}");
            }

            _pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath);

            InternalLogger.Info($"{ToolName}: copied '{absoluteSourcePath}' to '{absolutePath}'.");

            return new CreateFileResponse
            {
                Success = true,
                FilePath = relativePath ?? absolutePath,
                CreatedSuccessfully = true,
                CopiedFrom = relativeSourcePath ?? absoluteSourcePath
            };
        }

        /// <summary>
        /// A failed copy can leave a truncated target behind; remove it so the tree stays clean.
        /// </summary>
        private void TryDeletePartialCopy(string absolutePath)
        {
            try
            {
                if (_fileSystem.FileExists(absolutePath))
                    _fileSystem.Delete(absolutePath);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"{ToolName}: failed to delete the partial copy '{absolutePath}': {ex.Message}");
            }
        }

        private static CreateFileResponse ErrorReponse(string message)
        {
            return new CreateFileResponse
            {
                Success = false,
                ErrorMessage = message,
                CreatedSuccessfully = false
            };
        }
    }

    internal class CreateFileResponse : IToolFileOpenResult
    {
        string IToolFileOpenResult.FileToOpen => FilePath;

        bool IToolFileOpenResult.WroteFile => Success && CreatedSuccessfully;

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        [JsonProperty("created_successfully")]
        public bool CreatedSuccessfully { get; set; }

        [JsonProperty("copied_from", NullValueHandling = NullValueHandling.Ignore)]
        public string CopiedFrom { get; set; }

        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }

        [JsonProperty("syntax_errors", NullValueHandling = NullValueHandling.Ignore)]
        public string[] SyntaxErrors { get; set; }
    }
}
