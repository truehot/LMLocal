using System;
using System.Collections.Generic;
using System.Text;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// Single source of truth for building and reading the Markdown fenced code blocks that are sent to the model:
    /// the same class writes a file fence with its "file: ..." comment and parses it back.
    /// </summary>
    internal static class MarkdownCodeBlockFormatter
    {
        /// <summary>Suffix of the file comment of a fence built by <see cref="BuildTruncatedFileFence"/>; stripped when such a comment is parsed back.</summary>
        private const string TruncatedFileSuffix = " (content truncated, file too large)";

        /// <summary>Prefix of the comment line that identifies the file a fence was built from.</summary>
        private const string FileCommentPrefix = "file:";

        /// <summary>Shortest run of backticks that opens or closes a fenced block (CommonMark).</summary>
        private const int MinFenceLength = 3;

        /// <summary>
        /// Wraps code text in a Markdown fenced code block with an optional file comment line.
        /// </summary>
        public static string BuildFence(string code, string language, string fileComment = null)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;

            string langTag = ResolveFenceLanguage(language);
            string result = $"````{langTag}\n";

            if (!string.IsNullOrEmpty(fileComment))
                result += $"{fileComment}\n";

            result += $"{code}\n````";
            return result;
        }

        /// <summary>
        /// Formats file content as a Markdown fenced code block with a language-aware "file: ..." comment.
        /// </summary>
        public static string FormatFileAsMarkdown(string content, string filePath, string displayPath = null)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;

            string lang = MarkdownLanguageHelper.GetLanguageFromExtension(filePath);
            return BuildFence(content, lang, BuildFileComment(filePath, displayPath));
        }

        /// <summary>
        /// Builds a fenced code block for a file whose content is too large to include.
        /// </summary>
        public static string BuildTruncatedFileFence(string filePath, string displayPath = null)
        {
            string lang = MarkdownLanguageHelper.GetLanguageFromExtension(filePath);
            string langTag = ResolveFenceLanguage(lang);
            string path = !string.IsNullOrEmpty(displayPath) ? displayPath : filePath;

            if (string.IsNullOrWhiteSpace(path))
                return $"````{langTag}\n(content truncated, file too large)\n````";

            string comment = WrapInComment($"file: {path}{TruncatedFileSuffix}", lang);
            return $"````{langTag}\n{comment}\n````";
        }

        /// <summary>
        /// Returns the file path of a fence's first content line when that line is a "file: ..." comment in any of the
        /// comment syntaxes <see cref="BuildFence"/> can emit, or <c>null</c> when the line is not a file comment.
        /// </summary>
        public static string ParseFileComment(string line)
        {
            string text = UnwrapComment(line);
            if (text == null || !text.StartsWith(FileCommentPrefix, StringComparison.OrdinalIgnoreCase))
                return null;

            string path = text.Substring(FileCommentPrefix.Length).Trim();
            if (path.EndsWith(TruncatedFileSuffix, StringComparison.Ordinal))
                path = path.Substring(0, path.Length - TruncatedFileSuffix.Length).Trim();

            return path.Length == 0 ? null : path;
        }

        /// <summary>
        /// Removes every fenced code block and keeps only the prose around them.
        /// </summary>
        public static string RemoveCodeBlocks(string text, IList<string> filePaths = null)
        {
            return RemoveBlocks(text, filePaths);
        }

        /// <summary>
        /// Resolves the Markdown fence language tag. Unknown languages fall back to "text" so a fence is never emitted with an empty info string.
        /// </summary>
        private static string ResolveFenceLanguage(string language)
        {
            return !string.IsNullOrEmpty(language) ? language : "text";
        }

        private static string BuildFileComment(string filePath, string displayPath)
        {
            string path = !string.IsNullOrEmpty(displayPath) ? displayPath : filePath;
            if (string.IsNullOrEmpty(path))
                return null;

            string lang = MarkdownLanguageHelper.GetLanguageFromExtension(filePath);
            return WrapInComment($"file: {path}", lang);
        }

        /// <summary>
        /// Removes fenced blocks line by line, collecting the paths of the ones that open with a file comment.
        /// </summary>
        private static string RemoveBlocks(string text, IList<string> filePaths)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;

            StringBuilder kept = null;
            int position = 0;
            int lineStart = 0;

            while (lineStart < text.Length)
            {
                int lineEnd = text.IndexOf('\n', lineStart);
                int lineContentEnd = lineEnd < 0 ? text.Length : lineEnd;

                int fenceLength = CountOpeningFence(text, lineStart, lineContentEnd);
                if (fenceLength == 0)
                {
                    if (lineEnd < 0)
                        break;

                    lineStart = lineEnd + 1;
                    continue;
                }

                int blockEnd = FindBlockEnd(text, lineStart, fenceLength);
                string filePath = ReadFileCommentOf(text, lineContentEnd, blockEnd);

                if (kept == null)
                    kept = new StringBuilder(text.Length);

                kept.Append(text, position, lineStart - position);
                position = blockEnd;

                if (filePath != null && filePaths != null)
                    filePaths.Add(filePath);

                lineStart = blockEnd;
            }

            if (kept == null)
                return text;

            kept.Append(text, position, text.Length - position);
            return kept.ToString();
        }

        /// <summary>
        /// Index just past the closing fence of the block that opens at <paramref name="fenceLineStart"/>, or the end of the text
        /// when the fence is never closed.
        /// </summary>
        private static int FindBlockEnd(string text, int fenceLineStart, int fenceLength)
        {
            int lineStart = text.IndexOf('\n', fenceLineStart);
            if (lineStart < 0)
                return text.Length;

            lineStart++;

            while (lineStart < text.Length)
            {
                int lineEnd = text.IndexOf('\n', lineStart);
                int lineContentEnd = lineEnd < 0 ? text.Length : lineEnd;

                if (IsClosingFence(text, lineStart, lineContentEnd, fenceLength))
                    return lineEnd < 0 ? text.Length : lineEnd + 1;

                if (lineEnd < 0)
                    return text.Length;

                lineStart = lineEnd + 1;
            }

            return text.Length;
        }

        /// <summary>
        /// File path when the first line after the opening fence is a file comment, otherwise <c>null</c>.
        /// </summary>
        private static string ReadFileCommentOf(string text, int fenceLineContentEnd, int blockEnd)
        {
            int contentStart = fenceLineContentEnd < text.Length ? fenceLineContentEnd + 1 : text.Length;
            if (contentStart >= blockEnd)
                return null;

            int contentEnd = text.IndexOf('\n', contentStart);
            if (contentEnd < 0 || contentEnd > blockEnd)
                contentEnd = blockEnd;

            return ParseFileComment(text.Substring(contentStart, contentEnd - contentStart));
        }

        /// <summary>
        /// Number of backticks of an opening fence (at least <see cref="MinFenceLength"/>, info string without backticks), or 0.
        /// </summary>
        private static int CountOpeningFence(string text, int lineStart, int lineEnd)
        {
            int i = SkipIndent(text, lineStart, lineEnd);
            int backticks = CountBackticks(text, i, lineEnd);

            if (backticks < MinFenceLength)
                return 0;

            for (int j = i + backticks; j < lineEnd; j++)
            {
                if (text[j] == '`')
                    return 0;
            }

            return backticks;
        }

        /// <summary>
        /// True when the line closes a fence opened with <paramref name="fenceLength"/> backticks: a run at least as long and nothing else after it.
        /// </summary>
        private static bool IsClosingFence(string text, int lineStart, int lineEnd, int fenceLength)
        {
            int i = SkipIndent(text, lineStart, lineEnd);
            int backticks = CountBackticks(text, i, lineEnd);

            if (backticks < fenceLength)
                return false;

            for (int j = i + backticks; j < lineEnd; j++)
            {
                if (!char.IsWhiteSpace(text[j]))
                    return false;
            }

            return true;
        }

        private static int SkipIndent(string text, int lineStart, int lineEnd)
        {
            int i = lineStart;
            while (i < lineEnd && (text[i] == ' ' || text[i] == '\t'))
                i++;
            return i;
        }

        private static int CountBackticks(string text, int start, int lineEnd)
        {
            int count = 0;
            while (start + count < lineEnd && text[start + count] == '`')
                count++;
            return count;
        }

        /// <summary>
        /// Returns the text inside a single-line comment in any syntax <see cref="WrapInComment"/> emits, or <c>null</c>.
        /// </summary>
        private static string UnwrapComment(string line)
        {
            if (string.IsNullOrEmpty(line))
                return null;

            string text = line.Trim();
            if (text.Length == 0)
                return null;

            if (text.Length >= 4 && text.StartsWith("/*", StringComparison.Ordinal) && text.EndsWith("*/", StringComparison.Ordinal))
                return text.Substring(2, text.Length - 4).Trim();

            if (text.Length >= 7 && text.StartsWith("<!--", StringComparison.Ordinal) && text.EndsWith("-->", StringComparison.Ordinal))
                return text.Substring(4, text.Length - 7).Trim();

            string[] prefixes = { "//", "--", "#", ";", "%", "'" };
            foreach (string prefix in prefixes)
            {
                if (text.StartsWith(prefix, StringComparison.Ordinal))
                    return text.Substring(prefix.Length).Trim();
            }

            return null;
        }

        /// <summary>
        /// Wraps the given text in a comment appropriate for the fence language, mirroring the comment syntax used by the front-end file.fence.js.
        /// </summary>
        private static string WrapInComment(string text, string lang)
        {
            switch (lang)
            {
                case "css":
                case "scss":
                case "less":
                case "sass":
                case "stylus":
                    return $"/* {text} */";

                case "html":
                case "xml":
                case "svg":
                case "xhtml":
                    return $"<!-- {text} -->";

                case "sql":
                case "plsql":
                case "psql":
                    return $"-- {text}";

                case "vbnet":
                case "vb":
                    return $"'{text}";

                case "clojure":
                case "cljs":
                    return $"; {text}";

                case "erlang":
                case "elixir":
                    return $"% {text}";

                case "makefile":
                case "cmake":
                    return $"# {text}";

                case "csharp":
                case "javascript":
                case "typescript":
                case "java":
                case "cpp":
                case "c":
                case "go":
                case "rust":
                case "php":
                case "vue":
                case "svelte":
                case "fsharp":
                case "swift":
                case "kotlin":
                case "dart":
                case "scala":
                case "groovy":
                case "csx":
                case "jsx":
                case "tsx":
                    return $"// {text}";

                default:
                    return $"# {text}";
            }
        }
    }
}
