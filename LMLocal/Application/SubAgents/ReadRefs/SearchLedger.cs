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
    /// A ref marker resolves against it only for lines that were returned, so a fabricated location is never expanded.
    /// </summary>
    internal sealed class SearchLedger
    {
        private readonly Dictionary<string, SearchRecord> _records = new Dictionary<string, SearchRecord>(StringComparer.OrdinalIgnoreCase);

        public int Count => _records.Count;

        public void Add(SearchRecord record)
        {
            if (string.IsNullOrEmpty(record.RelativePath) || record.LineNumber < 1)
                return;

            _records[Key(record.RelativePath, record.LineNumber)] = record;
        }

        /// <summary>
        /// Resolves every line of <paramref name="range"/>; succeeds only when each line was returned by a search.
        /// </summary>
        public bool TryResolve(string path, LineRange range, out IReadOnlyList<SearchRecord> records)
        {
            records = null;
            if (string.IsNullOrEmpty(path) || !range.IsValid)
                return false;

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

        private static string Key(string path, int line) => ReadLedger.Normalize(path) + "|" + line;
    }
}
