using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LMLocal.Application.SubAgents.ReadRefs
{
    /// <summary>
    /// How a marker range was resolved against the read ledger.
    /// </summary>
    internal enum ReadResolveRule
    {
        /// <summary>Every line of the range was read.</summary>
        Covered,

        /// <summary>The range starts in a read and stays within the file; a few of its lines were not read.</summary>
        ExtendedWithinFile,

        /// <summary>The range ran past the end of the file and was cut at its last line, which was read.</summary>
        ClampedAtEndOfFile
    }

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

        /// <summary>Most lines of a marker the model did not see that <see cref="ReadResolveRule.ExtendedWithinFile"/> still expands.</summary>
        internal const int MaxUnreadLines = 200;

        /// <summary>
        /// Resolves a marker (path + range) against the ledger.
        /// </summary>
        public bool TryResolve(string path, LineRange range, out ReadRecord record)
            => TryResolve(path, range, out record, out _, out _);

        /// <summary>Resolves a marker range; <paramref name="resolved"/> is the range to expand (cut when it ran past the end of the file).</summary>
        public bool TryResolve(string path, LineRange range, out ReadRecord record, out LineRange resolved, out ReadResolveRule rule)
        {
            record = default;
            resolved = range;
            rule = ReadResolveRule.Covered;
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

            if (TryResolveByUnion(normalized, range, out record))
                return true;

            return TryResolveBeyondRead(normalized, range, out record, out resolved, out rule);
        }

        /// <summary>A range that starts in a read and ends after it: cut at the end of the file, expanded whole when few of its lines were unread.</summary>
        private bool TryResolveBeyondRead(string normalizedPath, LineRange range, out ReadRecord record, out LineRange resolved, out ReadResolveRule rule)
        {
            record = default;
            resolved = range;
            rule = ReadResolveRule.Covered;

            ReadRecord? sample = null;
            int? totalLines = null;
            var reads = new List<ReadRecord>();
            for (int i = 0; i < _records.Count; i++)
            {
                var candidate = _records[i];
                if (!Matches(candidate, normalizedPath))
                    continue;

                reads.Add(candidate);
                if (!sample.HasValue || (string.IsNullOrEmpty(sample.Value.AbsolutePath) && !string.IsNullOrEmpty(candidate.AbsolutePath)))
                    sample = candidate;
                if (candidate.TotalLines.HasValue)
                    totalLines = candidate.TotalLines;
            }

            if (reads.Count == 0)
                return false;

            var merged = Merge(reads);
            if (!merged.Any(m => m.Start <= range.Start && range.Start <= m.End))
                return false;

            int end = range.End;
            if (totalLines.HasValue && end > totalLines.Value)
                end = totalLines.Value;

            var last = merged[merged.Count - 1];
            if (last.ReachedEnd && end > last.End)
                end = last.End;

            if (end < range.Start)
                return false;

            var effective = new LineRange(range.Start, end);
            int unread = effective.End - effective.Start + 1 - CoveredLines(merged, effective);
            if (unread > 0 && (!totalLines.HasValue || unread > MaxUnreadLines))
                return false;

            record = new ReadRecord(sample.Value.RelativePath, effective.Start, effective.End, sample.Value.AbsolutePath, totalLines);
            resolved = effective;
            rule = unread > 0 ? ReadResolveRule.ExtendedWithinFile : ReadResolveRule.ClampedAtEndOfFile;
            return true;
        }

        /// <summary>Merges the reads of one file into sorted, non-overlapping intervals that keep whether they reached the end of the file.</summary>
        private static List<(int Start, int End, bool ReachedEnd)> Merge(List<ReadRecord> reads)
        {
            reads.Sort((a, b) => a.StartLine.CompareTo(b.StartLine));
            var merged = new List<(int Start, int End, bool ReachedEnd)>();
            foreach (var read in reads)
            {
                if (merged.Count > 0 && read.StartLine <= merged[merged.Count - 1].End + 1)
                {
                    var previous = merged[merged.Count - 1];
                    if (read.EndLine > previous.End)
                        merged[merged.Count - 1] = (previous.Start, read.EndLine, read.ReachedEnd);
                    else if (read.EndLine == previous.End && read.ReachedEnd)
                        merged[merged.Count - 1] = (previous.Start, previous.End, true);
                    continue;
                }

                merged.Add((read.StartLine, read.EndLine, read.ReachedEnd));
            }

            return merged;
        }

        private static int CoveredLines(List<(int Start, int End, bool ReachedEnd)> merged, LineRange range)
        {
            int covered = 0;
            foreach (var interval in merged)
            {
                int start = Math.Max(interval.Start, range.Start);
                int end = Math.Min(interval.End, range.End);
                if (end >= start)
                    covered += end - start + 1;
            }

            return covered;
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
















