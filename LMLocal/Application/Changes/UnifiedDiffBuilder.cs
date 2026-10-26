using System;
using System.Collections.Generic;
using LMLocal.Core.Common;

namespace LMLocal.Application.Changes
{
    /// <summary>
    /// Builds git-style unified diffs from two line arrays.
    /// </summary>
    internal static class UnifiedDiffBuilder
    {
        /// <summary>Number of unchanged lines shown around every change (same default as git).</summary>
        public const int DefaultContextLines = 3;

        /// <summary>
        /// Upper bound on the edit distance solved exactly for one changed block.
        /// </summary>
        internal const int MaxEditDistance = 2000;

        /// <summary>
        /// Splits file content into lines: "\r\n" and lone "\r" count as line breaks and the empty element produced by a trailing line break is dropped.
        /// </summary>
        public static string[] SplitLines(string content)
        {
            if (string.IsNullOrEmpty(content))
                return Array.Empty<string>();

            int lineCount = CountLines(content);
            if (lineCount == 0)
                return Array.Empty<string>();

            var lines = new string[lineCount];
            int index = 0;
            int start = 0;

            for (int i = 0; i < content.Length; i++)
            {
                char current = content[i];
                if (current != '\n' && current != '\r')
                    continue;

                lines[index++] = content.Substring(start, i - start);

                if (current == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                    i++;

                start = i + 1;
            }

            if (start < content.Length)
                lines[index] = content.Substring(start);

            return lines;
        }

        /// <summary>
        /// Counts the lines <see cref="SplitLines"/> will produce: every "\n", every "\r" and every "\r\n" terminates a line, and a line break at the very end of the content does not start a new one.
        /// </summary>
        private static int CountLines(string content)
        {
            int count = 0;
            int start = 0;

            for (int i = 0; i < content.Length; i++)
            {
                char current = content[i];
                if (current == '\n')
                {
                    count++;
                    start = i + 1;
                }
                else if (current == '\r')
                {
                    count++;
                    if (i + 1 < content.Length && content[i + 1] == '\n')
                        i++;

                    start = i + 1;
                }
            }

            if (start < content.Length)
                count++;

            return count;
        }

        /// <summary>
        /// Computes the hunks needed to turn <paramref name="originalLines"/> into <paramref name="modifiedLines"/>.
        /// </summary>
        public static List<DiffHunk> Compute(
            string[] originalLines,
            string[] modifiedLines,
            int contextLines = DefaultContextLines)
        {
            var original = originalLines ?? Array.Empty<string>();
            var modified = modifiedLines ?? Array.Empty<string>();

            if (contextLines < 0)
                contextLines = 0;

            List<EditOp> ops = BuildEditScript(original, modified);
            return BuildHunks(ops, contextLines);
        }

        /// <summary>Total number of added ('+') lines across the hunks.</summary>
        public static int CountAdded(IReadOnlyList<DiffHunk> hunks)
        {
            if (hunks == null)
                return 0;

            int count = 0;
            for (int i = 0; i < hunks.Count; i++)
            {
                var lines = hunks[i].Lines;
                for (int j = 0; j < lines.Count; j++)
                {
                    if (lines[j].Kind == DiffLineKind.Added)
                        count++;
                }
            }

            return count;
        }

        /// <summary>Total number of removed ('-') lines across the hunks.</summary>
        public static int CountRemoved(IReadOnlyList<DiffHunk> hunks)
        {
            if (hunks == null)
                return 0;

            int count = 0;
            for (int i = 0; i < hunks.Count; i++)
            {
                var lines = hunks[i].Lines;
                for (int j = 0; j < lines.Count; j++)
                {
                    if (lines[j].Kind == DiffLineKind.Removed)
                        count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Total number of rendered lines (headers included) across the hunks.
        /// </summary>
        public static int CountRenderedLines(IReadOnlyList<DiffHunk> hunks)
        {
            if (hunks == null)
                return 0;

            int count = 0;
            for (int i = 0; i < hunks.Count; i++)
                count += hunks[i].RenderedLineCount;

            return count;
        }

        private struct EditOp
        {
            public DiffLineKind Kind;
            public string Text;
        }

        private static EditOp ContextOp(string text) => new EditOp { Kind = DiffLineKind.Context, Text = text ?? string.Empty };

        private static EditOp AddedOp(string text) => new EditOp { Kind = DiffLineKind.Added, Text = text ?? string.Empty };

        private static EditOp RemovedOp(string text) => new EditOp { Kind = DiffLineKind.Removed, Text = text ?? string.Empty };

        private static List<EditOp> BuildEditScript(string[] a, string[] b)
        {
            var ops = new List<EditOp>(Math.Max(a.Length, b.Length) + 1);

            int maxPrefix = Math.Min(a.Length, b.Length);
            int prefix = 0;
            while (prefix < maxPrefix && string.Equals(a[prefix], b[prefix], StringComparison.Ordinal))
                prefix++;

            int maxSuffix = maxPrefix - prefix;
            int suffix = 0;
            while (suffix < maxSuffix &&
                   string.Equals(a[a.Length - 1 - suffix], b[b.Length - 1 - suffix], StringComparison.Ordinal))
            {
                suffix++;
            }

            for (int i = 0; i < prefix; i++)
                ops.Add(ContextOp(a[i]));

            int aStart = prefix;
            int aEnd = a.Length - suffix;
            int bStart = prefix;
            int bEnd = b.Length - suffix;

            int n = aEnd - aStart;
            int m = bEnd - bStart;

            if (n == 0)
            {
                for (int i = bStart; i < bEnd; i++)
                    ops.Add(AddedOp(b[i]));
            }
            else if (m == 0)
            {
                for (int i = aStart; i < aEnd; i++)
                    ops.Add(RemovedOp(a[i]));
            }
            else
            {
                List<EditOp> middle = BuildMiddleEditScript(a, aStart, aEnd, b, bStart, bEnd);
                if (middle != null)
                {
                    ops.AddRange(middle);
                }
                else
                {
                    InternalLogger.Debug(
                        $"UnifiedDiffBuilder: edit distance exceeds {MaxEditDistance} for a {n}x{m} block, using a coarse replacement hunk.");

                    for (int i = aStart; i < aEnd; i++)
                        ops.Add(RemovedOp(a[i]));
                    for (int i = bStart; i < bEnd; i++)
                        ops.Add(AddedOp(b[i]));
                }
            }

            for (int i = a.Length - suffix; i < a.Length; i++)
                ops.Add(ContextOp(a[i]));

            return ops;
        }

        /// <summary>
        /// Myers O(ND) diff of a[ aStart..aEnd ) against b[ bStart..bEnd ), covering an empty prefix/suffix only.
        /// </summary>
        private static List<EditOp> BuildMiddleEditScript(string[] a, int aStart, int aEnd, string[] b, int bStart, int bEnd)
        {
            int n = aEnd - aStart;
            int m = bEnd - bStart;
            int maxD = Math.Min(n + m, MaxEditDistance);
            int offset = maxD + 1;

            var v = new int[2 * maxD + 3];
            for (int i = 0; i < v.Length; i++)
                v[i] = -1;
            v[offset + 1] = 0;

            var trace = new List<int[]>(maxD + 1);
            int finalD = -1;

            for (int d = 0; d <= maxD; d++)
            {
                for (int k = -d; k <= d; k += 2)
                {
                    int x;
                    if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]))
                        x = v[offset + k + 1];
                    else
                        x = v[offset + k - 1] + 1;

                    int y = x - k;
                    while (x < n && y < m && x >= 0 && y >= 0 &&
                           string.Equals(a[aStart + x], b[bStart + y], StringComparison.Ordinal))
                    {
                        x++;
                        y++;
                    }

                    v[offset + k] = x;

                    if (x >= n && y >= m)
                    {
                        finalD = d;
                        break;
                    }
                }

                if (finalD >= 0)
                    break;

                var snapshot = new int[d + 1];
                for (int k = -d; k <= d; k += 2)
                    snapshot[(k + d) / 2] = v[offset + k];
                trace.Add(snapshot);
            }

            if (finalD < 0)
                return null;

            var reversed = new List<EditOp>(n + m);
            int cx = n;
            int cy = m;

            for (int d = finalD; d > 0; d--)
            {
                int k = cx - cy;
                int prevK;
                if (k == -d || (k != d && GetAfter(trace, d - 1, k - 1) < GetAfter(trace, d - 1, k + 1)))
                    prevK = k + 1;
                else
                    prevK = k - 1;

                int prevX = GetAfter(trace, d - 1, prevK);
                int prevY = prevX - prevK;

                int snakeX = prevK == k + 1 ? prevX : prevX + 1;
                int snakeY = prevK == k + 1 ? prevY + 1 : prevY;

                while (cx > snakeX && cy > snakeY)
                {
                    reversed.Add(ContextOp(a[aStart + cx - 1]));
                    cx--;
                    cy--;
                }

                if (prevK == k + 1)
                    reversed.Add(AddedOp(b[bStart + prevY]));
                else
                    reversed.Add(RemovedOp(a[aStart + prevX]));

                cx = prevX;
                cy = prevY;
            }

            while (cx > 0 && cy > 0)
            {
                reversed.Add(ContextOp(a[aStart + cx - 1]));
                cx--;
                cy--;
            }

            reversed.Reverse();
            return reversed;
        }

        /// <summary>
        /// Reads V after <paramref name="step"/> for the given diagonal <paramref name="k"/>.
        /// </summary>
        private static int GetAfter(List<int[]> trace, int step, int k)
        {
            if (step < 0 || step >= trace.Count || k < -step || k > step)
                return -1;

            int index = (k + step) / 2;
            int[] snapshot = trace[step];
            if (index < 0 || index >= snapshot.Length)
                return -1;

            return snapshot[index];
        }

        private static List<DiffHunk> BuildHunks(List<EditOp> ops, int contextLines)
        {
            var result = new List<DiffHunk>();
            if (ops == null || ops.Count == 0)
                return result;

            var oldBefore = new int[ops.Count + 1];
            var newBefore = new int[ops.Count + 1];
            for (int i = 0; i < ops.Count; i++)
            {
                oldBefore[i + 1] = oldBefore[i] + (ops[i].Kind != DiffLineKind.Added ? 1 : 0);
                newBefore[i + 1] = newBefore[i] + (ops[i].Kind != DiffLineKind.Removed ? 1 : 0);
            }

            int index = 0;
            while (index < ops.Count)
            {
                if (ops[index].Kind == DiffLineKind.Context)
                {
                    index++;
                    continue;
                }

                int firstChange = index;
                int lastChange = index;
                int scan = index;

                while (scan < ops.Count)
                {
                    if (ops[scan].Kind != DiffLineKind.Context)
                    {
                        lastChange = scan;
                        scan++;
                        continue;
                    }

                    int runStart = scan;
                    while (scan < ops.Count && ops[scan].Kind == DiffLineKind.Context)
                        scan++;

                    int runLength = scan - runStart;

                    if (scan >= ops.Count || runLength > 2 * contextLines)
                        break;
                }

                int from = Math.Max(0, firstChange - contextLines);
                int to = Math.Min(ops.Count - 1, lastChange + contextLines);

                int oldCount = oldBefore[to + 1] - oldBefore[from];
                int newCount = newBefore[to + 1] - newBefore[from];
                int oldStart = oldCount == 0 ? oldBefore[from] : oldBefore[from] + 1;
                int newStart = newCount == 0 ? newBefore[from] : newBefore[from] + 1;

                var lines = new List<DiffLine>(to - from + 1);
                for (int i = from; i <= to; i++)
                    lines.Add(new DiffLine(ops[i].Kind, ops[i].Text));

                result.Add(new DiffHunk(oldStart, oldCount, newStart, newCount, lines));
                index = to + 1;
            }

            return result;
        }
    }
}
