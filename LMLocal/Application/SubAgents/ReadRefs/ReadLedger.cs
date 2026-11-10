using System;
using System.Collections.Generic;
using System.Text;

namespace LMLocal.Application.SubAgents.ReadRefs
{
    /// <summary>
    /// Per-run ledger of the read_file_lines ranges that were actually returned to the model.
    /// </summary>
    internal sealed class ReadLedger
    {
        private readonly List<ReadRecord> _records = new List<ReadRecord>();

        public int Count => _records.Count;

        public IReadOnlyList<ReadRecord> Records => _records;

        public void Add(ReadRecord record)
        {
            if (string.IsNullOrEmpty(record.RelativePath))
                return;

            _records.Add(record);
        }

        /// <summary>
        /// Resolves a marker (path + range) against the ledger.
        /// </summary>
        public bool TryResolve(string path, LineRange range, out ReadRecord record)
        {
            record = default;
            if (string.IsNullOrEmpty(path) || !range.IsValid)
                return false;

            string normalized = Normalize(path);

            for (int i = 0; i < _records.Count; i++)
            {
                var candidate = _records[i];
                if (!Matches(candidate, normalized))
                    continue;

                if (!candidate.Contains(range))
                    continue;

                record = candidate;
                return true;
            }

            return TryResolveByUnion(normalized, range, out record);
        }

        /// <summary>
        /// Resolves <paramref name="range"/> against the union of the records for a normalized path.
        /// </summary>
        private bool TryResolveByUnion(string normalizedPath, LineRange range, out ReadRecord record)
        {
            record = default;

            ReadRecord? sample = null;
            var intervals = new List<LineRange>();
            for (int i = 0; i < _records.Count; i++)
            {
                var candidate = _records[i];
                if (!Matches(candidate, normalizedPath))
                    continue;

                if (!sample.HasValue)
                    sample = candidate;

                intervals.Add(candidate.Range);
            }

            if (intervals.Count == 0)
                return false;

            intervals.Sort((a, b) => a.Start.CompareTo(b.Start));

            int unionStart = intervals[0].Start;
            int unionEnd = intervals[0].End;

            for (int i = 1; i < intervals.Count; i++)
            {
                var next = intervals[i];
                if (next.Start <= unionEnd + 1)
                {
                    if (next.End > unionEnd)
                        unionEnd = next.End;

                    continue;
                }

                if (IsCovered(unionStart, unionEnd, range))
                {
                    record = new ReadRecord(sample.Value.RelativePath, unionStart, unionEnd, sample.Value.AbsolutePath);
                    return true;
                }

                unionStart = next.Start;
                unionEnd = next.End;
            }

            if (IsCovered(unionStart, unionEnd, range))
            {
                record = new ReadRecord(sample.Value.RelativePath, unionStart, unionEnd, sample.Value.AbsolutePath);
                return true;
            }

            return false;
        }

        /// <summary>
        /// A marker path matches a record by its solution-relative path or by its absolute path.
        /// </summary>
        private static bool Matches(ReadRecord candidate, string normalizedPath)
            => string.Equals(Normalize(candidate.RelativePath), normalizedPath, StringComparison.OrdinalIgnoreCase)
               || (!string.IsNullOrEmpty(candidate.AbsolutePath)
                   && string.Equals(Normalize(candidate.AbsolutePath), normalizedPath, StringComparison.OrdinalIgnoreCase));

        private static bool IsCovered(int unionStart, int unionEnd, LineRange range)
            => range.Start >= unionStart && range.End <= unionEnd;

        /// <summary>
        /// Path normalization used only for ledger lookup: separators unified to '\', runs of separators collapsed, trimmed, case-insensitive.
        /// </summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            string unified = path.Replace('/', '\\').Trim();
            if (unified.IndexOf("\\\\", StringComparison.Ordinal) < 0)
                return unified;

            var sb = new StringBuilder(unified.Length);
            char previous = '\0';
            foreach (char c in unified)
            {
                if (c == '\\' && previous == '\\')
                    continue;

                sb.Append(c);
                previous = c;
            }

            return sb.ToString();
        }
    }
}
















