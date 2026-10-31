using System;
using System.Collections.Generic;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// The single "text → lines" splitter of the file tools.
    /// </summary>
    internal static class LineSplitter
    {
        /// <summary>
        /// Splits text into lines: "\r\n", a lone "\r" and a lone "\n" count as line breaks and the empty element produced by a trailing line break is dropped.
        /// </summary>
        public static string[] Split(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<string>();

            int lineCount = CountLines(text);
            if (lineCount == 0)
                return Array.Empty<string>();

            var lines = new string[lineCount];
            int index = 0;
            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char current = text[i];
                if (current != '\n' && current != '\r')
                    continue;

                lines[index++] = text.Substring(start, i - start);

                if (current == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;

                start = i + 1;
            }

            if (start < text.Length)
                lines[index] = text.Substring(start);

            return lines;
        }

        /// <summary>
        /// Joins lines back into text so that <see cref="Split"/> returns exactly the same lines.
        /// </summary>
        public static string Join(string separator, IReadOnlyList<string> lines, bool endsWithLineBreak)
        {
            if (lines == null || lines.Count == 0)
                return string.Empty;

            bool lastLineIsBlank = string.IsNullOrEmpty(lines[lines.Count - 1]);
            if (!endsWithLineBreak && !lastLineIsBlank)
                return string.Join(separator, lines);

            var terminated = new string[lines.Count + 1];
            for (int i = 0; i < lines.Count; i++)
                terminated[i] = lines[i];
            terminated[lines.Count] = string.Empty;

            return string.Join(separator, terminated);
        }

        /// <summary>
        /// True when the text ends with a line break ("\n" or "\r")..
        /// </summary>
        public static bool HasTrailingEol(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            char last = text[text.Length - 1];
            return last == '\n' || last == '\r';
        }

        /// <summary>
        /// Counts the lines <see cref="Split"/> will produce: every "\n", every "\r" and every "\r\n" terminates a line, and a line break at the very end of the text does not start a new one.
        /// </summary>
        private static int CountLines(string text)
        {
            int count = 0;
            int start = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char current = text[i];
                if (current == '\n')
                {
                    count++;
                    start = i + 1;
                }
                else if (current == '\r')
                {
                    count++;
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                        i++;

                    start = i + 1;
                }
            }

            if (start < text.Length)
                count++;

            return count;
        }
    }
}
