using System;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// Shared helper that maps file extensions to Markdown fence language tags.
    /// </summary>
    internal static class MarkdownLanguageHelper
    {
        /// <summary>
        /// Returns the Markdown fence language tag for the given file path, or an empty string if the extension is not recognised.
        /// </summary>
        public static string GetLanguageFromExtension(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return "";

            string ext = System.IO.Path.GetExtension(filePath)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(ext))
                return "";

            switch (ext)
            {
                case ".cs": return "csharp";
                case ".js": return "javascript";
                case ".jsx": return "jsx";
                case ".mjs": return "javascript";
                case ".ts": return "typescript";
                case ".tsx": return "tsx";
                case ".py": return "python";
                case ".rb": return "ruby";
                case ".go": return "go";
                case ".rs": return "rust";
                case ".java": return "java";
                case ".kt": return "kotlin";
                case ".swift": return "swift";
                case ".cpp":
                case ".cc":
                case ".cxx":
                case ".c++": return "cpp";
                case ".c": return "c";
                case ".h":
                case ".hh":
                case ".hpp":
                case ".hxx":
                case ".h++":
                case ".inl":
                case ".ipp": return "cpp";
                case ".xml":
                case ".xaml":
                case ".csproj":
                case ".vcxproj":
                case ".vcxproj.filters":
                case ".sln":
                case ".config": return "xml";
                case ".json": return "json";
                case ".html":
                case ".htm": return "html";
                case ".css":
                case ".scss":
                case ".less": return "css";
                case ".md":
                case ".markdown": return "markdown";
                case ".yaml":
                case ".yml": return "yaml";
                case ".sql": return "sql";
                case ".sh":
                case ".bash": return "bash";
                case ".ps1": return "powershell";
                case ".dockerfile":
                case ".docker": return "dockerfile";
                case ".razor": return "razor";
                case ".cshtml": return "cshtml";
                default: return "";
            }
        }
    }
}
