using LMLocal.Core.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    internal interface IReadFileLines : IBuiltInTool
    {
    }

    internal class ReadFileLines : IReadFileLines
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IFileSystem _fileSystem;

        internal const int MaxFiles = 10;

        /// <summary>Files up to this many lines report total_lines even when the read stops before the end (the rest is read only to count lines).</summary>
        internal const int MaxCountedLines = 20000;

        /// <summary>Lines read from start_line when end_line is omitted.</summary>
        internal const int DefaultWindowLines = 200;
        internal const string FilesParameter = "files";

        private static readonly string EndLineDescription =
            "Last line number to read (1-indexed, inclusive, >= start_line). It is a line number, not a line count: " +
            "start_line 413 with end_line 442 reads 30 lines. " +
            $"Optional: omitted, up to {DefaultWindowLines} lines from start_line are read.";

        public string ToolName => "read_file_lines";
        public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

        public ReadFileLines(IVsDependencies vsDependencies, IPathResolver pathResolver, IFileSystem fileSystem)
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
                Description = "Reads line ranges from files and returns the lines as an array of [line_number, text] pairs, one per line. " +
                    "Read every range you already know in the SAME response, best as ONE call with 'files' (other files or other parts of one file); " +
                    "do not read one range per turn and decide on the next one after the result. " +
                    "file_path/start_line/end_line are for exactly one range. " +
                    $"When you do not know where the file or the needed part ends, omit end_line: up to {DefaultWindowLines} lines from start_line (default 1) are read, and has_more_results and total_lines tell whether and where to continue. " +
                    "A range you already know (get_symbol_info line..end_line, total_lines from a previous read or a search_file_content result, a search hit) is read as one entry, not in parts (not 1-260 and then 260-520). " +
                    $"A later call is only for: continuing after has_more_results=true, ranges found after the previous call, or more than {MaxFiles} ranges. " +
                    "Each item of 'content' is a two-element array of strings: the first element is the 1-based line number (as a string, e.g. \"60\"), the second is the exact raw source text of that line, with no line-number prefix and no trailing newline (example: [\"60\",\"    return x;\"]). A blank line is returned as [\"<line_number>\",\"\"]. If the requested end_line exceeds the total number of lines, the tool returns all available lines from start_line to the end of the file. has_more_results=true means additional lines exist after the returned content; to continue, call the tool again for the same file starting from the next line number after the last line shown in the returned content. total_lines is the file's line count (omitted for files over 20000 lines), so you know how far the file goes before reading on (empty content: start_line is past the end). With 'files' the response has 'files', one block per entry in request order with the same fields as a single read; an entry that fails has its own success=false and error_message, the others are still read. Only works inside the open solution directory — a path that resolves outside the solution is rejected with an explicit error. Use this tool to inspect a known file and line range without loading the entire content, or to visually confirm a fact after another tool has located it. Prefer search_file_content or symbol tools to locate unknown text, identifiers, or line numbers before using this tool. Independent: several calls can go in one response.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { "file_path", new ToolDetails { Type = "string", Description = "Relative path to the file. Required unless 'files' is passed." } },
                        { "start_line", new ToolDetails { Type = "integer", Description = "Starting line number (1-indexed, inclusive, >= 1). Optional, default 1." } },
                        { "end_line", new ToolDetails { Type = "integer", Description = EndLineDescription } },
                        {
                            FilesParameter, new ToolDetails
                            {
                                Type = "array",
                                Description = $"Several ranges to read in one call (up to {MaxFiles}), each like a single read; several ranges of one file are allowed. Use it for every range you already know instead of one call per range.",
                                Items = new ToolDetails
                                {
                                    Type = "object",
                                    Properties = new Dictionary<string, ToolDetails>
                                    {
                                        { "file_path", new ToolDetails { Type = "string", Description = "Relative path to the file." } },
                                        { "start_line", new ToolDetails { Type = "integer", Description = "Starting line number (1-indexed, inclusive, >= 1). Optional, default 1." } },
                                        { "end_line", new ToolDetails { Type = "integer", Description = EndLineDescription } }
                                    },
                                    Required = new List<string> { "file_path" }
                                }
                            }
                        }
                    },
                    Required = new List<string>()
                }
            };
        }

        public async Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            object filesObj = null;
            parameters?.TryGetValue(FilesParameter, out filesObj);
            if (LiteralListParameter.IsMissing(filesObj))
                return await ReadOneAsync(parameters, cancellationToken).ConfigureAwait(false);

            var (entries, error) = ExtractEntries(parameters, filesObj);
            if (error != null)
                return new ReadFilesResponse { Files = new List<FileLinesResponse>(), Success = false, ErrorMessage = error };

            var blocks = new List<FileLinesResponse>(entries.Count);
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                blocks.Add(await ReadOneAsync(entry, cancellationToken).ConfigureAwait(false));
            }

            bool any = blocks.Any(b => b.Success);
            return new ReadFilesResponse
            {
                Files = blocks,
                Success = any,
                ErrorMessage = any ? null : "No range was read: see error_message of each block."
            };
        }

        /// <summary>One read: the unchanged single-range contract.</summary>
        private async Task<FileLinesResponse> ReadOneAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            try
            {
                var (filePath, startLine, endLine, error) = ExtractAndValidateParameters(parameters);

                if (!string.IsNullOrEmpty(error))
                    return Error(error, parameters?.TryGetValue("file_path", out var fp) == true ? fp?.ToString() : "");

                if (!_vsDependencies.IsSolutionOpen)
                    return Error("No solution is currently open.", filePath);

                string solutionDir = _vsDependencies.GetSolutionDirectory();
                if (!_pathResolver.TryResolveFilePath(filePath, solutionDir, out string absolutePath) || string.IsNullOrEmpty(absolutePath))
                    return Error($"File not found: {filePath}", filePath);

                if (!_pathResolver.IsPathInsideDirectory(absolutePath, solutionDir))
                    return Error($"File '{absolutePath}' is outside the solution directory '{solutionDir}'. Only files inside the open solution can be read.", filePath);

                if (!_fileSystem.FileExists(absolutePath))
                    return Error($"File not found: {absolutePath}", filePath);

                if (!_pathResolver.TryGetRelativePath(absolutePath, solutionDir, out string relativePath))
                    relativePath = absolutePath;

                int readEnd = endLine == int.MaxValue ? int.MaxValue : endLine + 1;

                var (lines, totalLines) = await _fileSystem
                    .ReadLinesRangeWithTotalAsync(absolutePath, startLine, readEnd, cancellationToken, MaxCountedLines)
                    .ConfigureAwait(false);

                bool hasMore = lines.Count > (endLine - startLine + 1);
                if (hasMore && lines.Count > 0)
                    lines.RemoveAt(lines.Count - 1);

                return new FileLinesResponse
                {
                    Success = true,
                    FilePath = relativePath,
                    AbsolutePath = absolutePath,
                    Content = NumberLines(lines, startLine),
                    HasMoreResults = hasMore,
                    TotalLines = totalLines
                };
            }
            catch (Exception ex)
            {
                return new FileLinesResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    FilePath = parameters?.TryGetValue("file_path", out var fp) == true ? fp?.ToString() : "",
                    Content = null,
                    HasMoreResults = false
                };
            }
        }

        /// <summary>
        /// Reads 'files' (an array of {file_path, start_line, end_line}, or such an array serialized into a string).
        /// A top-level file_path, when also passed, becomes the first entry. Each entry is validated when it is read,
        /// so a bad entry fails only its own block.
        /// </summary>
        internal static (List<Dictionary<string, object>> Entries, string Error) ExtractEntries(Dictionary<string, object> parameters, object filesObj)
        {
            if (filesObj is JValue scalar)
                filesObj = scalar.Value;

            if (filesObj is string text)
            {
                try
                {
                    filesObj = JToken.Parse(text);
                }
                catch (JsonException)
                {
                    return (null, $"Parameter '{FilesParameter}' must be an array of objects {{file_path, start_line, end_line}}.");
                }
            }

            if (!(filesObj is IEnumerable items) || filesObj is string || filesObj is JObject)
                return (null, $"Parameter '{FilesParameter}' must be an array of objects {{file_path, start_line, end_line}}.");

            var entries = new List<Dictionary<string, object>>();
            if (parameters != null && parameters.TryGetValue("file_path", out object single) && !LiteralListParameter.IsMissing(single))
            {
                entries.Add(new Dictionary<string, object>
                {
                    ["file_path"] = single,
                    ["start_line"] = parameters.TryGetValue("start_line", out object s) ? s : null,
                    ["end_line"] = parameters.TryGetValue("end_line", out object e) ? e : null
                });
            }

            foreach (object item in items)
            {
                Dictionary<string, object> entry;
                if (item is JObject obj)
                    entry = obj.ToObject<Dictionary<string, object>>();
                else if (item is Dictionary<string, object> dict)
                    entry = dict;
                else
                    return (null, $"Each item of '{FilesParameter}' must be an object {{file_path, start_line, end_line}}.");
                entries.Add(entry);
            }

            if (entries.Count == 0)
                return (null, $"Parameter '{FilesParameter}' has no entries.");
            if (entries.Count > MaxFiles)
                return (null, $"At most {MaxFiles} ranges per call (got {entries.Count}). Split them into several calls.");

            return (entries, null);
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            if (parameters == null) return "Reading lines of ''... ";

            parameters.TryGetValue(FilesParameter, out object filesObj);
            if (!LiteralListParameter.IsMissing(filesObj))
            {
                var (entries, error) = ExtractEntries(parameters, filesObj);
                if (error != null || entries == null)
                    return "Reading files... ";
                return "Reading " + string.Join(", ", entries.Select(DescribeRange)) + "... ";
            }

            var file = parameters.TryGetValue("file_path", out var f) ? f?.ToString() : "";
            return $"Reading lines {DisplayLines(parameters)} of '{file}'... ";
        }

        private static string DescribeRange(Dictionary<string, object> entry)
        {
            var file = entry.TryGetValue("file_path", out var f) ? f?.ToString() : "";
            return $"'{file}' {DisplayLines(entry)}";
        }

        /// <summary>The range as it will be read: omitted bounds show their defaults, unparsable ones show '?'.</summary>
        private static string DisplayLines(Dictionary<string, object> entry)
        {
            string start = "1";
            int startValue = 1;
            if (entry.TryGetValue("start_line", out var s) && !LiteralListParameter.IsMissing(s))
                start = int.TryParse(s.ToString(), out startValue) ? startValue.ToString() : "?";

            if (!entry.TryGetValue("end_line", out var e) || LiteralListParameter.IsMissing(e))
                return start == "?" ? "?" : $"{start}-{(long)startValue + DefaultWindowLines - 1}";

            return $"{start}-{(int.TryParse(e.ToString(), out var endValue) ? endValue.ToString() : "?")}";
        }

        public string GetCompletionMessage(object result)
        {
            if (result is FileLinesResponse fileResult)
            {
                if (!fileResult.Success)
                    return $"Read lines failed: {fileResult.ErrorMessage}";

                var total = fileResult.Content?.Count ?? 0;
                return total > 0
                    ? $"Read {total} {Pluralizer.Pluralize(total, "line", "lines")}."
                    : "No lines found.";
            }

            if (result is ReadFilesResponse multi)
            {
                if (multi.Files == null || multi.Files.Count == 0)
                    return $"Read lines failed: {multi.ErrorMessage}";

                int ok = multi.Files.Count(b => b.Success);
                int lines = multi.Files.Where(b => b.Success).Sum(b => b.Content?.Count ?? 0);
                if (multi.Files.Count == 1)
                {
                    return ok == 1
                        ? $"Read {lines} {Pluralizer.Pluralize(lines, "line", "lines")} from 1 range."
                        : $"Read lines failed: {multi.Files[0].ErrorMessage}";
                }

                var message = $"Read {lines} {Pluralizer.Pluralize(lines, "line", "lines")} from {ok} of {multi.Files.Count} {Pluralizer.Pluralize(multi.Files.Count, "range", "ranges")}";
                int failed = multi.Files.Count - ok;
                if (failed > 0)
                    message += $" ({failed} failed: {multi.Files.First(b => !b.Success).ErrorMessage})";
                return message + ".";
            }

            return "Reading lines finished.";
        }

        /// <summary>
        /// Maps each raw line to a <see cref="FileLineEntry"/> carrying its 1-based line number and the exact raw line text (no prefix, no trailing newline).
        /// </summary>
        private static List<FileLineEntry> NumberLines(List<string> lines, int startLine)
        {
            var result = new List<FileLineEntry>(lines?.Count ?? 0);
            if (lines == null)
                return result;

            for (int i = 0; i < lines.Count; i++)
            {
                result.Add(new FileLineEntry
                {
                    LineNumber = startLine + i,
                    Text = lines[i] ?? string.Empty
                });
            }
            return result;
        }

        private (string filePath, int startLine, int endLine, string error) ExtractAndValidateParameters(
            Dictionary<string, object> parameters)
        {
            if (parameters == null)
                return (null, 0, 0, "Parameters cannot be null.");

            if (!parameters.TryGetValue("file_path", out object filePathObj) || !(filePathObj is string))
                return (null, 0, 0, "file_path parameter is required and must be a string.");

            int startLine = 1;
            if (parameters.TryGetValue("start_line", out object startLineObj) && !LiteralListParameter.IsMissing(startLineObj) && !TryParseInt(startLineObj, out startLine))
                return (null, 0, 0, "start_line parameter must be an integer.");

            int endLine;
            if (!parameters.TryGetValue("end_line", out object endLineObj) || LiteralListParameter.IsMissing(endLineObj))
                endLine = startLine < 1 ? startLine : (int)Math.Min((long)startLine + DefaultWindowLines - 1, int.MaxValue - 1);
            else if (!TryParseInt(endLineObj, out endLine))
                return (null, 0, 0, "end_line parameter must be an integer.");

            if (startLine < 1)
                return (null, 0, 0, "start_line must be a positive integer (>= 1).");

            if (endLine < 1)
                return (null, 0, 0, "end_line must be a positive integer (>= 1).");

            if (endLine < startLine)
            {
                long countedEnd = (long)startLine + endLine - 1;
                return (null, 0, 0,
                    $"end_line must be greater than or equal to start_line (got start_line {startLine}, end_line {endLine}). " +
                    $"end_line is the last line number to read, not a line count: to read {endLine} lines from line {startLine}, pass end_line {countedEnd}, or omit end_line.");
            }

            return ((string)filePathObj, startLine, endLine, null);
        }

        private bool TryParseInt(object value, out int result) => int.TryParse(value?.ToString(), out result);

        private static FileLinesResponse Error(string message, string filePath = "")
        {
            return new FileLinesResponse
            {
                Success = false,
                ErrorMessage = message,
                FilePath = filePath,
                Content = null,
                HasMoreResults = false
            };
        }

        public class FileLinesResponse
        {
            [JsonProperty("file_path")]
            public string FilePath { get; set; }

            [JsonProperty("content")]
            public List<FileLineEntry> Content { get; set; }

            [JsonProperty("has_more_results")]
            public bool HasMoreResults { get; set; }

            /// <summary>
            /// Number of lines in the file; omitted for files longer than <see cref="MaxCountedLines"/> when the read stopped before the end.
            /// </summary>
            [JsonProperty("total_lines", NullValueHandling = NullValueHandling.Ignore)]
            public int? TotalLines { get; set; }

            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
            public string ErrorMessage { get; set; }

            /// <summary>
            /// Absolute on-disk path of the file that was read.
            /// </summary>
            [JsonIgnore]
            public string AbsolutePath { get; set; }
        }

        /// <summary>
        /// Response of a call with 'files': one <see cref="FileLinesResponse"/> block per entry, in request order.
        /// Success is true when at least one block was read.
        /// </summary>
        public class ReadFilesResponse
        {
            [JsonProperty("files")]
            public List<FileLinesResponse> Files { get; set; }

            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
            public string ErrorMessage { get; set; }
        }

        /// <summary>
        /// One line of a read. Serialized as a pair of strings ["60","text"] to keep the response short.
        /// </summary>
        [JsonConverter(typeof(FileLineEntryConverter))]
        public class FileLineEntry
        {
            public int LineNumber { get; set; }

            public string Text { get; set; }
        }

        internal sealed class FileLineEntryConverter : JsonConverter<FileLineEntry>
        {
            public override void WriteJson(JsonWriter writer, FileLineEntry value, JsonSerializer serializer)
            {
                if (value == null)
                {
                    writer.WriteNull();
                    return;
                }

                writer.WriteStartArray();
                writer.WriteValue(value.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
                writer.WriteValue(value.Text ?? string.Empty);
                writer.WriteEndArray();
            }

            public override FileLineEntry ReadJson(JsonReader reader, Type objectType, FileLineEntry existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                var token = JToken.Load(reader);
                return TryParse(token, out var entry) ? entry : null;
            }

            /// <summary>Reads a line in either form: ["60","text"] or {"line_number":60,"text":"text"}.</summary>
            internal static bool TryParse(JToken token, out FileLineEntry entry)
            {
                entry = null;
                if (token is JArray pair && pair.Count == 2
                    && int.TryParse(pair[0]?.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int number))
                {
                    entry = new FileLineEntry { LineNumber = number, Text = pair[1]?.Type == JTokenType.Null ? string.Empty : pair[1]?.ToString() ?? string.Empty };
                    return true;
                }

                if (token is JObject obj && obj["line_number"] != null)
                {
                    entry = new FileLineEntry { LineNumber = obj["line_number"].Value<int>(), Text = obj["text"]?.Value<string>() ?? string.Empty };
                    return true;
                }

                return false;
            }
        }
    }
}
