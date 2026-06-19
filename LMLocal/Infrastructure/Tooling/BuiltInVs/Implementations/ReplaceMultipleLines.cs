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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    internal interface IReplaceMultipleLines : IBuiltInTool { }

    internal class ReplaceMultipleLines : IReplaceMultipleLines
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly ISnapshotManager _snapshotManager;
        private readonly IFileSystem _fileSystem;

        private const bool AutoExtend = true;

        public string ToolName => "Replace_Multiple_Lines";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.FullAccess;

        public ReplaceMultipleLines(
            IVsDependencies vsDependencies,
            IPathResolver pathResolver,
            ISnapshotManager snapshotManager,
            IFileSystem fileSystem)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = @"Applies multiple line replacements in one operation. Creates a single backup for undo. Replacements are applied from bottom to top to avoid index shifts. Automatically extends the file with empty lines when start_line or end_line exceeds the current line count. new_lines can be a string (with \n) or an array of strings. Response fields: success (bool), error_message (string), file_path (string), errors (array of {index (int), error (string)} or null). Lines are 1-indexed.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        ["file_path"] = new ToolDetails { Type = "string", Description = "Absolute or relative path to file." },
                        ["replacements"] = new ToolDetails
                        {
                            Type = "array",
                            Description = "List of replacements, each with start_line, end_line (1-indexed inclusive), and new_lines (string or array of strings)."
                        }
                    },
                    Required = new List<string> { "file_path", "replacements" }
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                var (filePath, replacements, error) = ExtractAndValidateParameters(parameters);
                if (error != null)
                    return Error(error);

                if (!_vsDependencies.IsSolutionOpen)
                    return Error("No solution is currently open.");

                string solutionDir = _vsDependencies.GetSolutionDirectory();
                if (!_pathResolver.TryResolveFilePath(filePath, solutionDir, out string absolutePath))
                    return Error($"Failed to resolve file path: {filePath}");

                if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                    return Error($"File '{absolutePath}' is outside the solution directory.");

                try { _fileSystem.ValidateFilePath(absolutePath); }
                catch (ArgumentException ex) { return Error($"Invalid file path: {ex.Message}"); }

                if (!_fileSystem.FileExists(absolutePath))
                    return Error($"File not found: {filePath}");

                var lines = new List<string>();
                await _fileSystem.ReadLinesAsync(absolutePath, (_, line) => lines.Add(line), cancellationToken).ConfigureAwait(false);

                var sortedReplacements = replacements.OrderByDescending(r => r.StartLine).ToList();
                var validationErrors = new List<ReplacementError>();

                var extendedLines = new List<string>(lines);
                int currentMaxLine = extendedLines.Count;

                foreach (var rep in sortedReplacements)
                {
                    if (rep.StartLine < 1)
                    {
                        validationErrors.Add(new ReplacementError { Index = rep.Index, Error = $"start_line {rep.StartLine} is less than 1." });
                        continue;
                    }

                    if (rep.EndLine < rep.StartLine)
                    {
                        validationErrors.Add(new ReplacementError { Index = rep.Index, Error = $"end_line {rep.EndLine} is less than start_line {rep.StartLine}." });
                        continue;
                    }

                    while (currentMaxLine < rep.StartLine - 1)
                    {
                        extendedLines.Add("");
                        currentMaxLine++;
                    }

                    if (rep.EndLine > currentMaxLine)
                    {
                        while (currentMaxLine < rep.EndLine)
                        {
                            extendedLines.Add("");
                            currentMaxLine++;
                        }
                    }
                }

                if (validationErrors.Any())
                {
                    return new ReplaceMultipleLinesResponse
                    {
                        Success = false,
                        FilePath = absolutePath,
                        Errors = validationErrors,
                        ErrorMessage = "Some replacements have invalid parameters. No changes were saved."
                    };
                }

                var modifiedLines = new List<string>(extendedLines);
                foreach (var rep in sortedReplacements)
                {
                    int startIdx = rep.StartLine - 1;
                    int endIdx = rep.EndLine - 1;
                    while (modifiedLines.Count <= endIdx)
                        modifiedLines.Add("");
                    int removeCount = endIdx - startIdx + 1;

                    if (string.IsNullOrEmpty(rep.NewLinesText))
                    {
                        modifiedLines.RemoveRange(startIdx, removeCount);
                    }
                    else
                    {
                        string[] newLineArray = rep.NewLinesText.Replace("\r", "").Split('\n');
                        modifiedLines.RemoveRange(startIdx, removeCount);
                        modifiedLines.InsertRange(startIdx, newLineArray);
                    }
                }

                await _snapshotManager.SnapshotFileAsync(absolutePath, SnapshotChangeStatus.BeforeModify, cancellationToken).ConfigureAwait(false);

                string originalContent = await _fileSystem.ReadAllTextWithSharedReadAsync(absolutePath, cancellationToken).ConfigureAwait(false);
                string separator = originalContent.Contains("\r\n") ? "\r\n" : "\n";

                string newContent = string.Join(separator, modifiedLines);
                await _fileSystem.WriteAllBytesAsync(absolutePath, Encoding.UTF8.GetBytes(newContent), cancellationToken).ConfigureAwait(false);

                _pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath);
                return new ReplaceMultipleLinesResponse
                {
                    Success = true,
                    FilePath = relativePath ?? absolutePath
                };
            }
            catch (OperationCanceledException)
            {
                return Error("Operation was cancelled.");
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error in {ToolName}: {ex}");
                return Error($"Error: {ex.Message}");
            }
        }

        private (string filePath, List<ReplacementSpec> replacements, string error) ExtractAndValidateParameters(Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, null, "Parameters cannot be null.");

            if (!parameters.TryGetValue("file_path", out var fp) || !(fp is string filePath) || string.IsNullOrEmpty(filePath))
                return (null, null, "file_path parameter is required and must be a non-empty string.");

            if (!parameters.TryGetValue("replacements", out var repsObj) || repsObj == null)
                return (null, null, "replacements array is required.");

            object[] replacementsArray = null;
            if (repsObj is JArray jArray)
                replacementsArray = jArray.ToObject<object[]>();
            else if (repsObj is object[] arr)
                replacementsArray = arr;
            else
                return (null, null, "replacements must be an array.");

            var replacementsList = new List<ReplacementSpec>();
            for (int i = 0; i < replacementsArray.Length; i++)
            {
                var repDict = replacementsArray[i] as Dictionary<string, object>;
                if (repDict == null && replacementsArray[i] is JObject jObj)
                    repDict = jObj.ToObject<Dictionary<string, object>>();

                if (repDict == null)
                    return (null, null, $"Replacement at index {i} must be an object.");

                if (!TryGetInt(repDict, "start_line", out int start))
                    return (null, null, $"Replacement {i}: start_line is missing or invalid.");
                if (!TryGetInt(repDict, "end_line", out int end))
                    return (null, null, $"Replacement {i}: end_line is missing or invalid.");

                if (!repDict.TryGetValue("new_lines", out var nlObj) || nlObj == null)
                    return (null, null, $"Replacement {i}: new_lines is missing or null.");

                string newLinesText = null;
                if (nlObj is string str)
                    newLinesText = str;
                else if (nlObj is object[] arrLines)
                    newLinesText = string.Join("\n", arrLines.Select(x => x?.ToString() ?? ""));
                else if (nlObj is JArray jArrLines)
                    newLinesText = string.Join("\n", jArrLines.Select(x => x?.ToString() ?? ""));
                else
                    return (null, null, $"Replacement {i}: new_lines must be a string or an array of strings.");

                replacementsList.Add(new ReplacementSpec
                {
                    Index = i,
                    StartLine = start,
                    EndLine = end,
                    NewLinesText = newLinesText
                });
            }

            if (replacementsList.Count == 0)
                return (null, null, "Replacements array cannot be empty.");

            return (filePath, replacementsList, null);
        }

        private static bool TryGetInt(Dictionary<string, object> dict, string key, out int value)
        {
            value = 0;
            return dict.TryGetValue(key, out var obj) && int.TryParse(obj?.ToString(), out value);
        }

        private static ReplaceMultipleLinesResponse Error(string message)
        {
            return new ReplaceMultipleLinesResponse
            {
                Success = false,
                ErrorMessage = message
            };
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            var file = parameters?.TryGetValue("file_path", out var f) == true ? f?.ToString() : "file";
            return $"Replacing multiple lines in '{file}'...";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is ReplaceMultipleLinesResponse resp)
                return resp.Success ? "All replacements applied successfully." : $"Failed: {resp.ErrorMessage}";
            return "Replacements finished.";
        }

        private class ReplacementSpec
        {
            public int Index { get; set; }
            public int StartLine { get; set; }
            public int EndLine { get; set; }
            public string NewLinesText { get; set; }
        }
    }

    public class ReplaceMultipleLinesResponse
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("file_path")] public string FilePath { get; set; }
        [JsonProperty("error_message")] public string ErrorMessage { get; set; }
        [JsonProperty("errors")] public List<ReplacementError> Errors { get; set; }
    }

    public class ReplacementError
    {
        [JsonProperty("index")] public int Index { get; set; }
        [JsonProperty("error")] public string Error { get; set; }
    }
}