using System.Collections.Generic;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;

namespace LMLocal.Application.SubAgents.ReadRefs
{
    /// <summary>
    /// Records successful <c>read_file_lines</c> results into the per-run <see cref="ReadLedger"/>.
    /// </summary>
    internal static class ReadResultRecorder
    {
        public const string ReadFileLinesToolName = "read_file_lines";

        /// <summary>
        /// Adds a <see cref="ReadRecord"/> for a successful read_file_lines result; a result with several ranges
        /// ('files') adds one record per successful block.
        /// </summary>
        public static bool TryRecord(ReadLedger ledger, object result)
        {
            if (ledger == null)
                return false;

            if (result is ReadFileLines.ReadFilesResponse multi)
            {
                bool any = false;
                foreach (var block in multi.Files ?? new List<ReadFileLines.FileLinesResponse>())
                    any |= TryRecord(ledger, block);
                return any;
            }

            if (!TryExtract(result, out string relativePath, out string absolutePath, out int startLine, out int endLine))
                return false;

            var response = (ReadFileLines.FileLinesResponse)result;
            int? totalLines = response.TotalLines;
            bool reachedEnd = !response.HasMoreResults || (totalLines.HasValue && endLine >= totalLines.Value);
            ledger.Add(new ReadRecord(relativePath, startLine, endLine, absolutePath, totalLines, reachedEnd));
            return true;
        }

        /// <summary>
        /// Extracts the solution-relative path, the absolute on-disk path and the actually returned range from a read_file_lines result.
        /// </summary>
        public static bool TryExtract(object result, out string relativePath, out string absolutePath, out int startLine, out int endLine)
        {
            relativePath = null;
            absolutePath = null;
            startLine = 0;
            endLine = 0;

            if (result is ReadFileLines.FileLinesResponse response)
                return TryExtractFromResponse(response, out relativePath, out absolutePath, out startLine, out endLine);

            return false;
        }

        private static bool TryExtractFromResponse(
            ReadFileLines.FileLinesResponse response,
            out string relativePath,
            out string absolutePath,
            out int startLine,
            out int endLine)
        {
            relativePath = null;
            absolutePath = null;
            startLine = 0;
            endLine = 0;

            if (response?.Success != true)
                return false;

            var content = response.Content;
            if (content == null || content.Count == 0)
                return false;

            int start = content[0]?.LineNumber ?? 0;
            int end = content[content.Count - 1]?.LineNumber ?? 0;
            if (string.IsNullOrEmpty(response.FilePath)
                || string.IsNullOrEmpty(response.AbsolutePath)
                || start < 1
                || end < start)
                return false;

            relativePath = response.FilePath;
            absolutePath = response.AbsolutePath;
            startLine = start;
            endLine = end;
            return true;
        }
    }
}
