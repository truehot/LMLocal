using System;
using System.Collections.Generic;
using System.Text;

namespace LMLocal.Application.Changes
{
    /// <summary>
    /// Kind of a single line inside a unified-diff hunk.
    /// </summary>
    internal enum DiffLineKind
    {
        /// <summary>Unchanged line, rendered with the ' ' prefix.</summary>
        Context = 0,

        /// <summary>Line present only in the modified side, rendered with the '+' prefix.</summary>
        Added = 1,

        /// <summary>Line present only in the original side, rendered with the '-' prefix.</summary>
        Removed = 2
    }

    /// <summary>
    /// One rendered line of a unified-diff hunk: the marker kind plus the verbatim line text.
    /// </summary>
    internal readonly struct DiffLine : IEquatable<DiffLine>
    {
        private readonly string _text;

        public DiffLine(DiffLineKind kind, string text)
        {
            Kind = kind;
            _text = text;
        }

        /// <summary>Marker of the line (' ', '+' or '-').</summary>
        public DiffLineKind Kind { get; }

        /// <summary>
        /// Exact line text of the source file, without the marker and without a line terminator.
        /// </summary>
        public string Text => _text ?? string.Empty;

        public bool Equals(DiffLine other)
        {
            return Kind == other.Kind && string.Equals(Text, other.Text, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is DiffLine other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Kind * 397) ^ StringComparer.Ordinal.GetHashCode(Text);
        }

        public static bool operator ==(DiffLine left, DiffLine right) => left.Equals(right);

        public static bool operator !=(DiffLine left, DiffLine right) => !left.Equals(right);
    }

    /// <summary>
    /// A contiguous unified-diff hunk: an "@@ -a,b +c,d @@" header plus the lines it covers.
    /// </summary>
    internal sealed class DiffHunk
    {
        public DiffHunk(int oldStart, int oldCount, int newStart, int newCount, IReadOnlyList<DiffLine> lines)
        {
            OldStart = oldStart;
            OldCount = oldCount;
            NewStart = newStart;
            NewCount = newCount;
            Lines = lines ?? Array.Empty<DiffLine>();
        }

        /// <summary>1-based first line of the hunk on the original side; 0 when the hunk has no original lines.</summary>
        public int OldStart { get; }

        /// <summary>Number of original-side lines covered by the hunk (context + removed).</summary>
        public int OldCount { get; }

        /// <summary>1-based first line of the hunk on the modified side; 0 when the hunk has no modified lines.</summary>
        public int NewStart { get; }

        /// <summary>Number of modified-side lines covered by the hunk (context + added).</summary>
        public int NewCount { get; }

        /// <summary>Lines of the hunk in file order.</summary>
        public IReadOnlyList<DiffLine> Lines { get; }

        /// <summary>
        /// Number of physical lines this hunk occupies when rendered as text: the "@@" header plus every line.
        /// </summary>
        public int RenderedLineCount => Lines.Count + 1;

        /// <summary>
        /// Renders the hunk as git-style unified-diff text.
        /// </summary>
        public string ToText()
        {
            var sb = new StringBuilder(EstimateTextLength());
            sb.Append("@@ -").Append(OldStart).Append(',').Append(OldCount)
              .Append(" +").Append(NewStart).Append(',').Append(NewCount)
              .Append(" @@\n");

            for (int i = 0; i < Lines.Count; i++)
            {
                switch (Lines[i].Kind)
                {
                    case DiffLineKind.Added:
                        sb.Append('+');
                        break;
                    case DiffLineKind.Removed:
                        sb.Append('-');
                        break;
                    default:
                        sb.Append(' ');
                        break;
                }

                sb.Append(Lines[i].Text).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>
        /// Upper bound of the rendered text length, used as the <see cref="StringBuilder"/> capacity so that a hunk is built into one buffer instead of growing through repeated reallocations.
        /// </summary>
        private int EstimateTextLength()
        {
            // "@@ -a,b +c,d @@\n" — 12 fixed characters plus up to 7 digits for each of the four counters.
            int length = 12 + 4 * 7;

            for (int i = 0; i < Lines.Count; i++)
                length += Lines[i].Text.Length + 2; // marker + text + newline

            return length;
        }
    }
}
