using System.Collections.Generic;
using System.Text;
using LMLocal.Core.Common;

namespace LMLocal.Application.SubAgents.ReadRefs
{
    /// <summary>
    /// Single source of truth for rendering file content as Markdown: numbered blocks (tool output in chat history) and verbatim fenced blocks with a header (ref expansion).
    /// </summary>
    internal static class FileContentRenderer
    {
        /// <summary>
        /// Appends lines as "N: text" joined by '\n' straight into <paramref name="sb"/> (no intermediate strings).
        /// </summary>
        public static void AppendNumberedLines(StringBuilder sb, IEnumerable<NumberedLine> lines)
        {
            if (sb == null || lines == null)
                return;

            bool first = true;
            foreach (var line in lines)
            {
                if (!first)
                    sb.Append('\n');
                first = false;

                if (line.LineNumber > 0)
                    sb.Append(line.LineNumber).Append(": ");

                sb.Append(line.Text ?? string.Empty);
            }
        }

        /// <summary>
        /// Appends lines verbatim - the exact line text only, no line-number prefix - joined by '\n' straight into <paramref name="sb"/>.
        /// </summary>
        public static void AppendVerbatimLines(StringBuilder sb, IEnumerable<NumberedLine> lines)
        {
            if (sb == null || lines == null)
                return;

            bool first = true;
            foreach (var line in lines)
            {
                if (!first)
                    sb.Append('\n');
                first = false;

                sb.Append(line.Text ?? string.Empty);
            }
        }

        /// <summary>
        /// Appends the standard fenced file block.
        /// </summary>
        public static void AppendFileBlock(StringBuilder sb, string filePath, IEnumerable<NumberedLine> lines, bool hasMore)
        {
            var lang = MarkdownLanguageHelper.GetLanguageFromExtension(filePath);

            sb.Append("**`").Append(filePath).Append("`**");
            if (hasMore)
                sb.Append(" *(truncated, more lines available)*");
            sb.AppendLine();
            sb.AppendLine();
            sb.Append("````").Append(lang).AppendLine();
            AppendNumberedLines(sb, lines);
            sb.AppendLine();
            sb.Append("````").AppendLine();
            sb.AppendLine();
        }

        /// <summary>
        /// Appends a search-location line <c>path:line: `text`</c> with the matched line text verbatim as inline code.
        /// </summary>
        public static void AppendSearchLocation(StringBuilder sb, string filePath, int lineNumber, string text)
        {
            sb.Append(filePath).Append(':').Append(lineNumber).Append(':');
            if (string.IsNullOrEmpty(text))
                return;

            int longestRun = 0, run = 0;
            foreach (char c in text)
            {
                run = c == '`' ? run + 1 : 0;
                if (run > longestRun)
                    longestRun = run;
            }

            string fence = new string('`', longestRun + 1);
            bool pad = text[0] == '`' || text[text.Length - 1] == '`';

            sb.Append(' ').Append(fence);
            if (pad) sb.Append(' ');
            sb.Append(text);
            if (pad) sb.Append(' ');
            sb.Append(fence);
        }

        /// <summary>
        /// Appends a ref-expansion block: a <c>File: &lt;path&gt;, lines &lt;start&gt;-&lt;end&gt;</c> header, then a fenced block of <b>verbatim</b> lines (no line numbers) with <b>no trailing newline</b>.
        /// </summary>
        public static void AppendRefContentBlock(StringBuilder sb, string filePath, int startLine, int endLine, IEnumerable<NumberedLine> lines)
        {
            var lang = MarkdownLanguageHelper.GetLanguageFromExtension(filePath);
            if (string.IsNullOrEmpty(lang))
                lang = "text";

            sb.Append("File: ").Append(filePath).Append(", lines ").Append(startLine).Append('-').Append(endLine).Append('\n');
            sb.Append("````").Append(lang).Append('\n');
            AppendVerbatimLines(sb, lines);
            sb.Append('\n');
            sb.Append("````");
        }
    }
}
