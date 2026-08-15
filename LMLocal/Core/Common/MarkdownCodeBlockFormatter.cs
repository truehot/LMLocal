namespace LMLocal.Core.Common
{
    /// <summary>
    /// Single source of truth for building Markdown fenced code blocks that are sent to the model.
    /// </summary>
    internal static class MarkdownCodeBlockFormatter
    {
        /// <summary>
        /// Wraps code text in a Markdown fenced code block with an optional file comment line.
        /// </summary>
        public static string BuildFence(string code, string language, string fileComment = null)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;

            string langTag = !string.IsNullOrEmpty(language) ? language : "";
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
            string langTag = !string.IsNullOrEmpty(lang) ? lang : "";
            string path = !string.IsNullOrEmpty(displayPath) ? displayPath : filePath;

            if (string.IsNullOrWhiteSpace(path))
                return $"````{langTag}\n(content truncated, file too large)\n````";

            string comment = WrapInComment($"file: {path} (content truncated, file too large)", lang);
            return $"````{langTag}\n{comment}\n````";
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
