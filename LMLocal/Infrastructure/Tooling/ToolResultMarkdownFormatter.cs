using System;
using System.Collections.Generic;
using System.Text;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.SubAgents.ReadRefs;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Tooling
{
    /// <summary>
    /// Builds the "## Tool Results" section of a consolidated prompt: the files read in the step, one block per
    /// file, without the reads a later write made stale, plus a line naming the files the step changed.
    /// </summary>
    internal class ToolResultMarkdownFormatter : IToolResultMarkdownFormatter
    {
        private const string ReadFileLinesFunctionName = "read_file_lines";
        private const string SolutionOverviewFunctionName = "get_solution_overview";
        private const string ActiveDocumentFunctionName = "get_active_document";
        private const string DeleteFileFunctionName = "delete_file";
        private const string ReplaceFileLinesFunctionName = "replace_file_lines";
        private const string FormatDocumentFunctionName = "format_document";

        /// <summary>Paths of one file differ by case and by slash direction between the tools; compare that way.</summary>
        private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

        private static readonly HashSet<string> ReadFunctions = new HashSet<string>(StringComparer.Ordinal)
        {
            ReadFileLinesFunctionName, SolutionOverviewFunctionName, ActiveDocumentFunctionName
        };

        /// <summary>
        /// Tools whose result means the file changed. Their results render nothing: they only decide which reads
        /// are stale and what the "Changed in this step" line lists.
        /// </summary>
        private static readonly HashSet<string> WriteFunctions = new HashSet<string>(StringComparer.Ordinal)
        {
            "create_file", "replace_file_content", ReplaceFileLinesFunctionName, "insert_file_lines", DeleteFileFunctionName, FormatDocumentFunctionName
        };

        public string FormatToolResults(IEnumerable<(string FunctionName, string JsonContent)> toolResults)
        {
            List<Entry> entries = ParseEntries(toolResults);
            StepWrites writes = CollectWrites(entries);
            Dictionary<string, List<ReadPiece>> reads = CollectReads(entries, writes.LastWriteIndex);

            var sb = new StringBuilder();
            var emittedFiles = new HashSet<string>(PathComparer);

            foreach (Entry entry in entries)
            {
                switch (entry.FunctionName)
                {
                    case ReadFileLinesFunctionName:
                        foreach (ReadBlock block in ReadBlocks(entry.Json))
                        {
                            string key = NormalizePath(block.FilePath);
                            if (key.Length == 0 || emittedFiles.Contains(key))
                                continue;

                            // A file is emitted once, at the call that holds its first read the step left current.
                            if (!reads.TryGetValue(key, out List<ReadPiece> pieces) || pieces[0].Index != entry.Index)
                                continue;

                            emittedFiles.Add(key);
                            AppendMergedFile(sb, pieces);
                        }
                        break;

                    case ActiveDocumentFunctionName:
                        if (!Failed(entry.Json)
                            && !IsStale(NormalizePath(entry.Json["file_path"]?.Value<string>()), entry.Index, writes.LastWriteIndex))
                            FormatActiveDocumentResult(sb, entry.Json);
                        break;

                    case SolutionOverviewFunctionName:
                        if (!Failed(entry.Json))
                            FormatSolutionOverviewResult(sb, entry.Json);
                        break;
                }
            }

            if (writes.Changed.Count > 0)
            {
                sb.Append("Changed in this step (re-read before use): ")
                  .AppendLine(string.Join(", ", writes.Changed.ConvertAll(file => file.Deleted ? file.Path + " (deleted)" : file.Path)));
            }

            string body = sb.ToString().TrimEnd();
            return body.Length == 0 ? string.Empty : "## Tool Results" + Environment.NewLine + Environment.NewLine + body;
        }

        private static bool Failed(JObject json)
        {
            return json["success"]?.Value<bool>() == false;
        }

        /// <summary>
        /// Results of the tools this section renders or needs for the stale rule, in call order; the index is the
        /// position of the call in the step, which is also its execution order.
        /// </summary>
        private static List<Entry> ParseEntries(IEnumerable<(string FunctionName, string JsonContent)> toolResults)
        {
            var entries = new List<Entry>();
            if (toolResults == null)
                return entries;

            int index = 0;
            foreach (var (functionName, jsonContent) in toolResults)
            {
                int position = index++;
                if (string.IsNullOrWhiteSpace(jsonContent) || !IsKnown(functionName))
                    continue;

                JObject parsed;
                try
                {
                    parsed = JObject.Parse(jsonContent);
                }
                catch (JsonException)
                {
                    // Malformed JSON — skip
                    continue;
                }

                entries.Add(new Entry { Index = position, FunctionName = functionName, Json = parsed });
            }

            return entries;
        }

        private static bool IsKnown(string functionName)
        {
            return ReadFunctions.Contains(functionName) || WriteFunctions.Contains(functionName);
        }

        /// <summary>Reads of every file that the step left current, per normalized path, in read order.</summary>
        private static Dictionary<string, List<ReadPiece>> CollectReads(List<Entry> entries, Dictionary<string, int> lastWriteIndex)
        {
            var reads = new Dictionary<string, List<ReadPiece>>(PathComparer);

            foreach (Entry entry in entries)
            {
                if (entry.FunctionName != ReadFileLinesFunctionName)
                    continue;

                foreach (ReadBlock block in ReadBlocks(entry.Json))
                {
                    string key = NormalizePath(block.FilePath);
                    if (key.Length == 0 || IsStale(key, entry.Index, lastWriteIndex))
                        continue;

                    if (!reads.TryGetValue(key, out List<ReadPiece> pieces))
                    {
                        pieces = new List<ReadPiece>();
                        reads[key] = pieces;
                    }

                    pieces.Add(new ReadPiece { Index = entry.Index, Block = block });
                }
            }

            return reads;
        }

        private static StepWrites CollectWrites(List<Entry> entries)
        {
            var writes = new StepWrites();

            foreach (Entry entry in entries)
            {
                if (!WriteFunctions.Contains(entry.FunctionName))
                    continue;

                foreach ((string path, bool deleted) in SuccessfulWrites(entry.FunctionName, entry.Json))
                {
                    writes.Record(NormalizePath(path), path, deleted, entry.Index);
                }
            }

            return writes;
        }

        /// <summary>Files one write result reports as written, with the top level and the per-file ("files" array) shapes.</summary>
        private static IEnumerable<(string Path, bool Deleted)> SuccessfulWrites(string functionName, JObject json)
        {
            if (!DidWrite(functionName, json))
                yield break;

            if (json["files"] is JArray files && files.Count > 0)
            {
                foreach (JToken file in files)
                {
                    if (!(file is JObject fileJson) || !DidWrite(functionName, fileJson))
                        continue;

                    string path = fileJson["file_path"]?.Value<string>();
                    if (!string.IsNullOrWhiteSpace(path))
                        yield return (path.Trim(), functionName == DeleteFileFunctionName);
                }

                yield break;
            }

            string singlePath = json["file_path"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(singlePath))
                yield return (singlePath.Trim(), functionName == DeleteFileFunctionName);
        }

        /// <summary>
        /// True when this result changed the file: it did not fail, <c>format_document</c> did not skip it, and the
        /// edits of <c>replace_file_lines</c> were not all no-op.
        /// </summary>
        private static bool DidWrite(string functionName, JObject json)
        {
            if (json["success"]?.Value<bool>() == false)
                return false;

            if (functionName == FormatDocumentFunctionName && json["skipped"]?.Value<bool>() == true)
                return false;

            if (functionName == ReplaceFileLinesFunctionName && AllEditsAreNoOp(json))
                return false;

            return true;
        }

        private static bool AllEditsAreNoOp(JObject json)
        {
            if (!(json["edits"] is JArray edits) || edits.Count == 0)
                return false;

            foreach (JToken edit in edits)
            {
                if (!(edit is JObject editJson) || editJson["no_op"]?.Value<bool>() != true)
                    return false;
            }

            return true;
        }

        /// <summary>True when the file was written after this read, so the read no longer shows the file.</summary>
        private static bool IsStale(string key, int readIndex, Dictionary<string, int> lastWriteIndex)
        {
            return lastWriteIndex.TryGetValue(key, out int writeIndex) && writeIndex > readIndex;
        }

        /// <summary>Whole blocks of one read call: one per file in "files", or the single-file shape.</summary>
        private static List<ReadBlock> ReadBlocks(JObject json)
        {
            var blocks = new List<ReadBlock>();

            if (json["success"]?.Value<bool>() == false)
                return blocks;

            if (json["files"] is JArray files && files.Count > 0)
            {
                foreach (JToken file in files)
                {
                    // A failed block is skipped like a failed read.
                    if (file is JObject fileJson && fileJson["success"]?.Value<bool>() != false)
                        blocks.Add(ReadBlock.FromJson(fileJson));
                }

                return blocks;
            }

            blocks.Add(ReadBlock.FromJson(json));
            return blocks;
        }

        /// <summary>
        /// One block per file: the union of the lines of all its reads, a later read winning for the same number,
        /// sorted by line number.
        /// </summary>
        private static void AppendMergedFile(StringBuilder sb, List<ReadPiece> pieces)
        {
            ReadBlock latest = pieces[pieces.Count - 1].Block;

            if (latest.HasUnnumberedLines)
            {
                FileContentRenderer.AppendFileBlock(sb, latest.FilePath, latest.Lines, latest.HasMore);
                return;
            }

            var merged = new SortedDictionary<int, string>();
            bool hasMore = false;

            foreach (ReadPiece piece in pieces)
            {
                if (piece.Block.HasUnnumberedLines)
                    continue;

                if (piece.Block.Lines.Count > 0)
                    hasMore = piece.Block.HasMore;

                foreach (NumberedLine line in piece.Block.Lines)
                    merged[line.LineNumber] = line.Text;
            }

            var lines = new List<NumberedLine>(merged.Count);
            foreach (KeyValuePair<int, string> pair in merged)
                lines.Add(new NumberedLine(pair.Key, pair.Value));

            FileContentRenderer.AppendFileBlock(sb, pieces[0].Block.FilePath, lines, hasMore);
        }

        /// <summary>One normalized form for the "same file" comparisons of the tools.</summary>
        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('/', '\\');
        }

        /// <summary>
        /// Normalizes the raw content token into <see cref="NumberedLine"/> items so it can be rendered by the shared <see cref="FileContentRenderer"/>.
        /// </summary>
        private static List<NumberedLine> ExtractNumberedLines(JToken contentToken)
        {
            var result = new List<NumberedLine>();

            if (!(contentToken is JArray lines))
            {
                result.Add(new NumberedLine(0, contentToken?.Value<string>() ?? ""));
                return result;
            }

            foreach (var line in lines)
            {
                if (ReadFileLines.FileLineEntryConverter.TryParse(line, out var entry))
                {
                    result.Add(new NumberedLine(entry.LineNumber, entry.Text));
                }
                else
                {
                    result.Add(new NumberedLine(0, line?.ToString() ?? ""));
                }
            }

            return result;
        }

        private static void FormatActiveDocumentResult(StringBuilder sb, JObject obj)
        {
            var filePath = obj["file_path"]?.Value<string>() ?? "unknown";
            var content = obj["content"]?.Value<string>() ?? "";
            var lang = MarkdownLanguageHelper.GetLanguageFromExtension(filePath);

            sb.AppendLine($"**Active Document: `{filePath}`**");
            sb.AppendLine();
            sb.AppendLine($"````{lang}");
            sb.AppendLine(content);
            sb.AppendLine("````");
            sb.AppendLine();
        }

        private static void FormatSolutionOverviewResult(StringBuilder sb, JObject obj)
        {
            var solutionName = obj["solution_name"]?.Value<string>() ?? "Unknown";
            var totalProjects = obj["total_projects"]?.Value<int>() ?? 0;
            var totalFiles = obj["total_files"]?.Value<int>() ?? 0;
            var hasMore = obj["has_more_results"]?.Value<bool>() == true;

            sb.AppendLine($"**Solution:** {solutionName} ({totalProjects} {Pluralizer.Pluralize(totalProjects, "project", "projects")}, {totalFiles} {Pluralizer.Pluralize(totalFiles, "file", "files")})");
            sb.AppendLine();

            if (obj["projects"] is JArray projects)
            {
                foreach (var project in projects)
                {
                    var name = project["name"]?.Value<string>() ?? "";
                    var lang = project["language"]?.Value<string>() ?? "";
                    var fileCount = project["file_count"]?.Value<int>() ?? 0;
                    var isTest = project["is_test_project"]?.Value<bool>() == true;

                    var testTag = isTest ? " *(test)*" : "";
                    sb.AppendLine($"- **{name}** — {lang}, {fileCount} {Pluralizer.Pluralize(fileCount, "file", "files")}{testTag}");
                }
            }

            if (hasMore)
                sb.AppendLine("- *...and more (list truncated)*");

            sb.AppendLine();
        }

        /// <summary>One parsed result of the step, with its position in the call order.</summary>
        private sealed class Entry
        {
            public int Index;
            public string FunctionName;
            public JObject Json;
        }

        /// <summary>One read of a file, with the position of the call that made it.</summary>
        private sealed class ReadPiece
        {
            public int Index;
            public ReadBlock Block;
        }

        /// <summary>Content of one file of one read call.</summary>
        private sealed class ReadBlock
        {
            public string FilePath;
            public List<NumberedLine> Lines;
            public bool HasMore;

            /// <summary>Content that came as a plain string has no line numbers and cannot be merged.</summary>
            public bool HasUnnumberedLines;

            public static ReadBlock FromJson(JObject json)
            {
                List<NumberedLine> lines = ExtractNumberedLines(json["content"]);

                return new ReadBlock
                {
                    FilePath = json["file_path"]?.Value<string>() ?? "unknown",
                    Lines = lines,
                    HasMore = json["has_more_results"]?.Value<bool>() == true,
                    HasUnnumberedLines = lines.Exists(line => line.LineNumber <= 0)
                };
            }
        }

        /// <summary>A file the step wrote, as listed in the "Changed in this step" line.</summary>
        private sealed class ChangedFile
        {
            public string Path;
            public bool Deleted;
        }

        /// <summary>Where the step wrote: last write per file, and the files in order of first write.</summary>
        private sealed class StepWrites
        {
            /// <summary>Index of the last write per normalized path.</summary>
            public readonly Dictionary<string, int> LastWriteIndex = new Dictionary<string, int>(PathComparer);

            public readonly List<ChangedFile> Changed = new List<ChangedFile>();

            private readonly Dictionary<string, int> _changedIndex = new Dictionary<string, int>(PathComparer);

            public void Record(string key, string path, bool deleted, int index)
            {
                if (key.Length == 0)
                    return;

                LastWriteIndex[key] = index;

                if (_changedIndex.TryGetValue(key, out int position))
                {
                    Changed[position].Deleted = deleted;
                    return;
                }

                _changedIndex[key] = Changed.Count;
                Changed.Add(new ChangedFile { Path = path, Deleted = deleted });
            }
        }
    }
}
