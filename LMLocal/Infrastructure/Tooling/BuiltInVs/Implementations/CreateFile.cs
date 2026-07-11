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
                Description = "Creates a new file with the given content. Fails if the file already exists — use replace_file_content to update an existing file instead. Parent directories are created automatically if they don't exist. Path relative to solution root. After creating .cs files, use set_file_project_status to register them in the .csproj project.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Relative path to file to create." } },
                        { "content", new ToolDetails { Type = "string", Description = "Initial content for the file." } }
                    },
                    Required = new List<string> { "file_path", "content" }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                var (filePath, content, error) = ExtractAndValidateParameters(parameters);
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

                if (_fileSystem.FileExists(absolutePath))
                    return ErrorReponse($"File already exists: {filePath}");


                await _snapshotManager.SnapshotFileAsync(absolutePath, SnapshotChangeStatus.BeforeCreate, cancellationToken).ConfigureAwait(false);

                _fileSystem.EnsureDirectoryExistsForFile(absolutePath);

                await _fileSystem.WriteAllBytesWithEncodingAsync(absolutePath, content, Encoding.UTF8, hasBom: true, cancellationToken).ConfigureAwait(false);

                string[] syntaxErrors = null;
                var checker = _syntaxFactory.GetChecker(absolutePath);
                if (checker != null && !checker.IsSyntaxValid(content, out var errors))
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
            var filePath = (parameters?.TryGetValue("file_path", out var f) == true ? f?.ToString() : "") ?? "";
            return $"Creating file '{filePath}'... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is CreateFileResponse response)
            {
                if (!response.Success)
                    return $"File creation failed: {response.ErrorMessage}";

                string msg = "File created";
                if (response.SyntaxErrors != null && response.SyntaxErrors.Length > 0)
                    msg += $" with {response.SyntaxErrors.Length} syntax error(s)";
                msg += ".";

                return msg;
            }
            return "File creation finished.";
        }

        private (string filePath, string content, string error) ExtractAndValidateParameters(
            Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, null, "Parameters cannot be null.");

            if (!parameters.TryGetValue("file_path", out object filePathObj) || !(filePathObj is string))
                return (null, null, "file_path parameter is required and must be a string.");

            if (!parameters.TryGetValue("content", out object contentObj) || !(contentObj is string))
                return (null, null, "content parameter is required and must be a string.");

            return ((string)filePathObj, (string)contentObj, null);
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

    internal class CreateFileResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        [JsonProperty("created_successfully")]
        public bool CreatedSuccessfully { get; set; }

        [JsonProperty("error_message")]
        public string ErrorMessage { get; set; }

        [JsonProperty("syntax_errors", NullValueHandling = NullValueHandling.Ignore)]
        public string[] SyntaxErrors { get; set; }
    }
}
