using System;
using System.Collections.Generic;
using System.IO;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// Policy for deciding which files and directories are irrelevant for LLM context: build outputs, dependency folders, binaries, images, fonts, archives, documents, media, minified and junk files.
    /// </summary>
    internal static class SourceFileFilter
    {
        private static readonly HashSet<string> _excludedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Build / runtime outputs
            "bin", "obj", ".vs", "dist", "build", "out", "artifacts", "TestResults", "CopilotBaseline",
            // Version control
            ".git", ".svn", ".hg",
            // Package managers / dependencies
            "node_modules", "packages", ".nuget", "bower_components",
            // Language / framework caches and generated folders
            "__pycache__", ".venv", "venv", ".idea", ".next", ".angular", ".pytest_cache", "coverage"
        };

        private static readonly HashSet<string> _binaryExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".dll", ".exe", ".pdb", ".obj", ".o", ".lib", ".a", ".dylib", ".so",
            ".pch", ".ilk", ".exp", ".nupkg", ".snupkg", ".cache", ".pyc", ".pyo",
            ".class", ".jar", ".war", ".dex", ".apk", ".msi", ".msp", ".cab",
            ".deps.json", ".runtimeconfig.json", ".rsp", ".suo", ".user",
            ".snk", ".pfx", ".cer", ".p12", ".key", ".pem"
        };

        private static readonly HashSet<string> _imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", ".webp", ".tiff", ".tif",
            ".jfif", ".avif", ".heic", ".heif", ".psd", ".ai", ".eps", ".raw", ".cr2"
        };

        private static readonly HashSet<string> _fontExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".ttf", ".otf", ".woff", ".woff2", ".eot", ".fnt"
        };

        private static readonly HashSet<string> _archiveExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".tgz", ".iso"
        };

        private static readonly HashSet<string> _documentExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp"
        };

        private static readonly HashSet<string> _mediaExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".mp4", ".wav", ".avi", ".mov", ".mkv", ".flv", ".webm", ".aac", ".flac", ".ogg", ".m4a", ".m4v", ".wmv"
        };

        private static readonly HashSet<string> _junkFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".ds_store", "thumbs.db", "desktop.ini"
        };

        private static readonly string[] _minifiedSuffixes = { ".min.js", ".min.css", ".udm.js" };

        /// <summary>
        /// Directory names that are pruned wherever they appear as a path component.
        /// </summary>
        public static HashSet<string> ExcludedDirectories => _excludedDirectories;

        /// <summary>
        /// Returns true when any path component (directory) is an excluded / generated directory.
        /// </summary>
        public static bool ShouldExcludePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string[] components = normalized.Split(Path.DirectorySeparatorChar);

            foreach (string component in components)
            {
                if (component.Length == 0)
                    continue;
                if (_excludedDirectories.Contains(component))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true when the file name refers to a binary, image, font, archive, document, media, minified or junk file.
        /// </summary>
        public static bool IsExcludedFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return false;

            if (_junkFileNames.Contains(fileName))
                return true;

            string ext = Path.GetExtension(fileName);
            return _binaryExtensions.Contains(ext)
                || _imageExtensions.Contains(ext)
                || _fontExtensions.Contains(ext)
                || _archiveExtensions.Contains(ext)
                || _documentExtensions.Contains(ext)
                || _mediaExtensions.Contains(ext)
                || IsMinifiedFile(fileName);
        }

        /// <summary>
        /// Combined path + file filter.
        /// </summary>
        public static bool ShouldExclude(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return ShouldExcludePath(path) || IsExcludedFile(Path.GetFileName(path));
        }

        private static bool IsMinifiedFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return false;

            foreach (string suffix in _minifiedSuffixes)
            {
                if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
