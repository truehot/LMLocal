using System;
using System.Collections.Generic;

namespace LMLocal.Application.SubAgents.ReadRefs
{
    /// <summary>
    /// A single search_file_content match that was returned to the model: solution-relative path, line number and the
    /// matched line text exactly as the tool returned it.
    /// </summary>
    internal readonly struct SearchRecord
    {
        public SearchRecord(string relativePath, int lineNumber, string text)
        {
            RelativePath = relativePath ?? string.Empty;
            LineNumber = lineNumber;
            Text = text ?? string.Empty;
        }

        public string RelativePath { get; }

        public int LineNumber { get; }

        public string Text { get; }
    }

    /// <summary>
    /// Per-run ledger of the search_file_content matches that were actually returned to the model.
    /// </summary>
    internal sealed class SearchLedger
    {
        private readonly Dictionary<string, SearchRecord> _records = new Dictionary<string, SearchRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _relativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public int Count => _records.Count;

        public void Add(SearchRecord record)
        {
            if (string.IsNullOrEmpty(record.RelativePath) || record.LineNumber < 1)
                return;

            _records[Key(record.RelativePath, record.LineNumber)] = record;
            _relativePaths.Add(ReadLedger.Normalize(record.RelativePath));
        }

        /// <summary>
        /// Resolves every line of <paramref name="range"/>; succeeds only when each line was returned by a search.
        /// </summary>
        public bool TryResolve(string path, LineRange range, out IReadOnlyList<SearchRecord> records)
        {
            records = null;
            if (string.IsNullOrEmpty(path) || !range.IsValid)
                return false;

            path = ToRecordedPath(ReadLedger.Normalize(path));

            var list = new List<SearchRecord>(range.End - range.Start + 1);
            for (int line = range.Start; line <= range.End; line++)
            {
                if (!_records.TryGetValue(Key(path, line), out var record))
                    return false;
                list.Add(record);
            }

            records = list;
            return true;
        }

        /// <summary>
        /// Maps an absolute marker path (subagents copy the full path from their task) to the recorded solution-relative path it ends with; the longest match wins.
        /// </summary>
        private string ToRecordedPath(string normalizedPath)
        {
            bool rooted = (normalizedPath.Length > 2 && normalizedPath[1] == ':') || normalizedPath.StartsWith("\\", StringComparison.Ordinal);
            if (!rooted || _relativePaths.Contains(normalizedPath))
                return normalizedPath;

            string best = null;
            foreach (string relative in _relativePaths)
            {
                if (normalizedPath.Length > relative.Length
                    && normalizedPath[normalizedPath.Length - relative.Length - 1] == '\\'
                    && normalizedPath.EndsWith(relative, StringComparison.OrdinalIgnoreCase)
                    && (best == null || relative.Length > best.Length))
                    best = relative;
            }

            return best ?? normalizedPath;
        }

        private static string Key(string path, int line) => ReadLedger.Normalize(path) + "|" + line;
    }
}
