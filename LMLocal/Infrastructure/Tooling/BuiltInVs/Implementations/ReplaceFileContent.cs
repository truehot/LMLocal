using LMLocal.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Syntax;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    internal interface IReplaceFileContent : IBuiltInTool
    {
    }

    internal class ReplaceFileContent : IReplaceFileContent
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly ISnapshotManager _snapshotManager;
        private readonly IFileSystem _fileSystem;
        private readonly ISyntaxCheckerFactory _syntaxFactory;

        public string ToolName => "replace_file_content";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.FullAccess;

        public ReplaceFileContent(IVsDependencies vsDependencies, IPathResolver pathResolver, ISnapshotManager snapshotManager, IFileSystem fileSystem, ISyntaxCheckerFactory syntaxFactory)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _syntaxFactory = syntaxFactory ?? throw new ArgumentNullException(nameof(syntaxFactory));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Replaces the entire content of a file with new content. This is a full overwrite — the old content is completely replaced, not merged. After this operation, line numbers shift, so re-read the file if you need accurate line positions. Use for small files or when replacing the whole file is simpler than targeting specific lines. For partial edits, prefer replace_file_lines. Fails if the file does not exist. Path can be absolute or relative to solution root.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Absolute or relative path to file." } },
                        { "content", new ToolDetails { Type = "string", Description = "Raw source text to write as the complete file content." } }
                    },
                    Required = new List<string> { "file_path", "content" }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                string pruneRejection = PrunedContentGuard.Validate(parameters);
                if (pruneRejection != null)
                    return Error(pruneRejection);

                var (filePath, newContent, error) = ExtractAndValidateParameters(parameters);
                if (!string.IsNullOrEmpty(error))
                    return Error(error);

                if (!_vsDependencies.IsSolutionOpen)
                    return Error("No solution is currently open.");

                string solutionDir = _vsDependencies.GetSolutionDirectory();

                if (!_pathResolver.TryResolveFilePath(filePath, solutionDir, out string absolutePath))
                    return Error($"Failed to resolve file path: {filePath}");

                try
                {
                    _fileSystem.ValidateFilePath(absolutePath);
                }
                catch (ArgumentException ex)
                {
                    return Error($"Invalid file path: {ex.Message}");
                }

                if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                    return Error($"File '{absolutePath}' is outside the solution directory.");

                if (!_fileSystem.FileExists(absolutePath))
                    return Error($"File not found: {filePath}");

                _pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath);

                var (originalContent, fileEncoding, hasBom) = await _fileSystem.ReadAllTextWithDetectedEncodingAsync(absolutePath, cancellationToken).ConfigureAwait(false);

                string separator = originalContent.Contains("\r\n") ? "\r\n" : "\n";
                string normalizedContent = newContent.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", separator);

                if (IsNoOp(originalContent, normalizedContent))
                {
                    InternalLogger.Debug(
                        $"replace_file_content: no-op on {absolutePath}, the file already holds the requested content; nothing written.");

                    return new ApplyCodeEditResponse
                    {
                        Success = true,
                        FilePath = relativePath ?? absolutePath,
                        IsNoOp = true
                    };
                }

                await _snapshotManager.SnapshotFileAsync(absolutePath, SnapshotChangeStatus.BeforeModify, cancellationToken).ConfigureAwait(false);

                await _fileSystem.WriteAllBytesWithEncodingAsync(absolutePath, normalizedContent, fileEncoding, hasBom, cancellationToken);

                string[] syntaxErrors = null;
                var checker = _syntaxFactory.GetChecker(absolutePath);
                if (checker != null && !checker.IsSyntaxValid(normalizedContent, out var errors))
                {
                    syntaxErrors = errors.Select(e => $"{e.Id}: {e.GetMessage()}").ToArray();
                    InternalLogger.Info($"Syntax errors detected after replacement in {absolutePath}:\n{string.Join("\n", syntaxErrors)}");
                }

                return new ApplyCodeEditResponse
                {
                    Success = true,
                    FilePath = relativePath ?? absolutePath,
                    SyntaxErrors = syntaxErrors
                };
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error in {ToolName}: {ex}");
                return Error($"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// True when the normalized text is what the file already holds (P2-6).
        /// </summary>
        private static bool IsNoOp(string originalContent, string normalizedContent)
        {
            if (string.Equals(originalContent, normalizedContent, StringComparison.Ordinal))
                return true;

            if (LineSplitter.HasTrailingEol(originalContent) != LineSplitter.HasTrailingEol(normalizedContent))
                return false;

            string[] originalLines = LineSplitter.Split(originalContent);
            string[] normalizedLines = LineSplitter.Split(normalizedContent);
            if (originalLines.Length != normalizedLines.Length)
                return false;

            for (int i = 0; i < originalLines.Length; i++)
            {
                if (!string.Equals(originalLines[i], normalizedLines[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var filePath = (parameters?.TryGetValue("file_path", out var f) == true ? f?.ToString() : "") ?? "";
            return $"Replacing content in `{filePath}`... ";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is ApplyCodeEditResponse response)
            {
                if (!response.Success)
                    return $"Replacement failed: {response.ErrorMessage}";

                if (response.IsNoOp)
                    return "No changes needed.";

                string msg = "Replacement completed";
                if (response.SyntaxErrors != null && response.SyntaxErrors.Length > 0)
                    msg += $" with {response.SyntaxErrors.Length} syntax {Pluralizer.Pluralize(response.SyntaxErrors.Length, "error", "errors")}";

                msg += ".";
                return msg;
            }
            return "Replacement completed.";
        }

        private (string filePath, string newContent, string error) ExtractAndValidateParameters(
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

        private static ApplyCodeEditResponse Error(string message)
        {
            return new ApplyCodeEditResponse
            {
                Success = false,
                ErrorMessage = message
            };
        }
    }

    internal class ApplyCodeEditResponse : IToolFileOpenResult
    {
        string IToolFileOpenResult.FileToOpen => FilePath;

        bool IToolFileOpenResult.WroteFile => Success && !IsNoOp;

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("file_path")]
        public string FilePath { get; set; }

        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }

        [JsonProperty("syntax_errors", NullValueHandling = NullValueHandling.Ignore)]
        public string[] SyntaxErrors { get; set; }

        /// <summary>
        /// Set when the call would have written exactly the text the file already holds (docs plan, P2-6).
        /// </summary>
        [JsonIgnore]
        internal bool IsNoOp { get; set; }
    }
}
