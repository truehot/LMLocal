using System;
using System.Collections.Generic;
using System.Text;

namespace LMLocal.Application.SubAgents.ReadRefs
{
    /// <summary>
    /// A parsed ref marker pointing at a solution-relative file and one or more line ranges.
    ///
    /// Grammar:
    /// <code>
    /// marker      ::= "@" ( quoted-path | bare-path ) ":" ranges
    /// quoted-path ::= '"' path-no-quote '"'
    /// bare-path   ::= [ drive ] [^":\s]+     (drive ::= LETTER ":" followed by '\' or '/')
    /// ranges      ::= range ("," range)*
    /// range       ::= NUMBER | NUMBER "-" NUMBER
    /// </code>
    /// A marker may be wrapped in emphasis (surrounding backticks or <c>**</c>); the parser consumes it.
    /// </summary>
    internal sealed class FileRefMarker
    {
        public FileRefMarker(string path, IReadOnlyList<LineRange> ranges)
            : this(path, ranges, 0, 0)
        {
        }

        public FileRefMarker(string path, IReadOnlyList<LineRange> ranges, int startIndex, int length)
        {
            Path = path ?? string.Empty;
            Ranges = ranges ?? Array.Empty<LineRange>();
            StartIndex = startIndex;
            Length = length;
        }

        /// <summary>Solution-relative file path exactly as returned by read_file_lines.</summary>
        public string Path { get; }

        public IReadOnlyList<LineRange> Ranges { get; }

        /// <summary>Start index of the marker span inside the source text (includes a consumed leading backtick).</summary>
        public int StartIndex { get; }

        /// <summary>Length of the marker span inside the source text.</summary>
        public int Length { get; }

        /// <summary>
        /// Canonical textual form: <c>@path:1-120</c> or <c>@"path with spaces.cs":10-20</c>.
        /// </summary>
        public string Format()
        {
            var sb = new StringBuilder();
            sb.Append('@');

            if (Path.IndexOf(' ') >= 0 || Path.IndexOf('\t') >= 0 || Path.IndexOf('"') >= 0)
            {
                sb.Append('"').Append(Path).Append('"');
            }
            else
            {
                sb.Append(Path);
            }


            sb.Append(':');
            for (int i = 0; i < Ranges.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append(Ranges[i].ToString());
            }

            return sb.ToString();
        }

        /// <summary>
        /// Finds every well-formed ref marker in <paramref name="text"/>, in order of appearance.
        /// </summary>
        public static IReadOnlyList<FileRefMarker> ParseAll(string text) => ParseAll(text, out _);

        /// <summary>
        /// Parses every well-formed ref marker and, in the same pass, counts marker-like tokens that failed to parse (observability only).
        /// </summary>
        internal static IReadOnlyList<FileRefMarker> ParseAll(string text, out int malformedCount)
        {
            malformedCount = 0;
            var markers = new List<FileRefMarker>();
            if (string.IsNullOrEmpty(text))
                return markers;

            int i = 0;
            while (i < text.Length)
            {
                int at = text.IndexOf('@', i);
                if (at < 0)
                    break;

                if (TryParseAt(text, at, out var marker))
                {
                    markers.Add(marker);
                    i = marker.StartIndex + marker.Length;
                }
                else
                {
                    if (LooksLikeIntendedMarker(text, at))
                        malformedCount++;

                    i = at + 1;
                }
            }

            return markers;
        }

        /// <summary>
        /// Attempts to parse a marker whose '@' sits at <paramref name="at"/>.
        /// </summary>
        private static bool TryParseAt(string text, int at, out FileRefMarker marker)
        {
            marker = null;
            if (string.IsNullOrEmpty(text) || at < 0 || at >= text.Length || text[at] != '@')
                return false;

            if (at > 0 && !IsBoundary(text[at - 1]))
                return false;

            int spanStart = at;
            int p = at + 1;

            string path;
            if (p < text.Length && text[p] == '"')
            {
                int close = text.IndexOf('"', p + 1);
                if (close < 0)
                    return false;

                path = text.Substring(p + 1, close - (p + 1));
                if (path.Length == 0)
                    return false;

                p = close + 1;
            }
            else
            {
                int pathStart = p;

                // Absolute Windows path ("D:\..." or "D:/..."): the drive colon is part of the path, not the range separator.
                if (p + 2 < text.Length && char.IsLetter(text[p]) && text[p + 1] == ':' && (text[p + 2] == '\\' || text[p + 2] == '/'))
                    p += 2;

                while (p < text.Length && text[p] != ':' && text[p] != '"' && !char.IsWhiteSpace(text[p]))
                    p++;

                if (p == pathStart)
                    return false;

                path = text.Substring(pathStart, p - pathStart);
            }

            if (p >= text.Length || text[p] != ':')
                return false;

            p++;

            while (p < text.Length && (text[p] == ' ' || text[p] == '\t'))
                p++;

            var ranges = new List<LineRange>();
            if (!TryParseRange(text, ref p, out var first))
                return false;

            ranges.Add(first);

            while (p < text.Length && text[p] == ',')
            {
                p++;
                while (p < text.Length && (text[p] == ' ' || text[p] == '\t'))
                    p++;

                if (!TryParseRange(text, ref p, out var next))
                    return false;

                ranges.Add(next);
            }

            int spanEnd = p;

            bool leadingBacktick = spanStart > 0 && text[spanStart - 1] == '`';
            bool trailingBacktick = spanEnd < text.Length && text[spanEnd] == '`';
            if (leadingBacktick && trailingBacktick)
            {
                spanStart--;
                spanEnd++;
            }

            bool leadingBold = spanStart > 1 && text[spanStart - 1] == '*' && text[spanStart - 2] == '*';
            bool trailingBold = spanEnd + 1 < text.Length && text[spanEnd] == '*' && text[spanEnd + 1] == '*';
            if (leadingBold && trailingBold)
            {
                spanStart -= 2;
                spanEnd += 2;
            }

            int spanLength = spanEnd - spanStart;
            marker = new FileRefMarker(path, ranges, spanStart, spanLength);
            return true;
        }

        private static bool LooksLikeIntendedMarker(string text, int at)
        {
            if (at > 0 && !IsBoundary(text[at - 1]))
                return false;

            if (at + 1 >= text.Length || char.IsWhiteSpace(text[at + 1]))
                return false;

            int end = Math.Min(text.Length, at + 300);
            for (int j = at + 1; j < end; j++)
            {
                char c = text[j];
                if (c == '\n' || c == '\r')
                    return false;

                if (c == ':')
                {
                    int k = j + 1;
                    while (k < end && (text[k] == ' ' || text[k] == '\t'))
                        k++;

                    return k < end && text[k] >= '0' && text[k] <= '9';
                }
            }

            return false;
        }

        private static bool IsBoundary(char c)
            => char.IsWhiteSpace(c) || c == '(' || c == '[' || c == '`' || c == '"' || c == '>' || c == '*';

        private static bool TryParseRange(string text, ref int p, out LineRange range)
        {
            range = default;

            if (!TryParseNumber(text, ref p, out int start, out _))
                return false;

            int afterStart = p;
            if (p < text.Length && text[p] == '-')
            {
                p++;
                if (TryParseNumber(text, ref p, out int end, out bool endOverflowed))
                {
                    if (end < start)
                    {
                        p = afterStart;
                        return false;
                    }

                    range = new LineRange(start, end);
                    return true;
                }

                if (endOverflowed)
                {
                    p = afterStart;
                    return false;
                }

                p = afterStart;
            }

            range = new LineRange(start, start);
            return true;
        }

        private static bool TryParseNumber(string text, ref int p, out int value, out bool overflowed)
        {
            value = 0;
            overflowed = false;
            int start = p;
            long acc = 0;

            while (p < text.Length && text[p] >= '0' && text[p] <= '9')
            {
                acc = acc * 10 + (text[p] - '0');

                if (acc > int.MaxValue)
                {
                    p = start;
                    overflowed = true;
                    return false;
                }

                p++;
            }

            if (p == start)
                return false;

            value = (int)acc;
            return true;
        }
    }
}
