
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using LMLocal.Core.Common;


namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Provides programming language and target framework for a Visual Studio project.
    /// </summary>
    internal static class ProjectMetadataProvider
    {
        private const int MaxProjectFileSizeBytes = 1 * 1024 * 1024; // 1 MB

        private class CacheEntry
        {
            public string Language { get; set; }
            public string TargetFramework { get; set; }
            public bool IsNativeTestProject { get; set; }
            public DateTime LastWriteTime { get; set; }
        }

        private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        private static readonly char[] _tagNameDelimiters = new[] { ' ', '>', '\t', '\r', '\n' };

        public static void ClearAll() => _cache.Clear();

        /// <summary>
        /// Gets language, target framework and native test flag for a project.
        /// </summary>
        public static (string Language, string TargetFramework, bool IsNativeTestProject) GetMetadata(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath))
                return ("Unknown", null, false);

            string actualPath = ResolveProjectFilePath(projectPath);
            if (actualPath == null)
                return ("Unknown", null, false);

            DateTime currentWriteTime = File.Exists(actualPath)
                ? File.GetLastWriteTime(actualPath)
                : DateTime.MinValue;

            if (_cache.TryGetValue(actualPath, out CacheEntry cached) && cached.LastWriteTime == currentWriteTime)
                return (cached.Language, cached.TargetFramework, cached.IsNativeTestProject);

            string language = DetectLanguageByExtension(actualPath);
            string targetFramework = null;
            bool isNativeTestProject = false;
            string content = null;
            bool needRead = (language == "C#" || language == "VB.NET" || language == "F#" || language == "C++") && File.Exists(actualPath);

            if (needRead)
            {
                try
                {
                    var fi = new FileInfo(actualPath);
                    if (fi.Length < MaxProjectFileSizeBytes)
                    {
                        content = File.ReadAllText(actualPath, Encoding.UTF8);
                        targetFramework = ExtractTargetFrameworkFromContent(content);
                        isNativeTestProject = ExtractIsNativeUnitTestFromContent(content);
                    }
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"ProjectMetadataProvider: Failed to read project file {actualPath}: {ex.Message}");
                }
            }

            if (language == null)
                language = DetectLanguageFromContent(content);

            var newEntry = new CacheEntry
            {
                Language = language ?? "Unknown",
                TargetFramework = targetFramework ?? "Unknown",
                IsNativeTestProject = isNativeTestProject,
                LastWriteTime = currentWriteTime
            };

            _cache.AddOrUpdate(actualPath, newEntry, (string key, CacheEntry existing) => newEntry);
            return (newEntry.Language, newEntry.TargetFramework, newEntry.IsNativeTestProject);
        }

        private static string ResolveProjectFilePath(string projectPath)
        {
            if (File.Exists(projectPath))
                return projectPath;

            if (Directory.Exists(projectPath))
            {
                try
                {
                    var files = Directory.GetFiles(projectPath, "*proj", SearchOption.TopDirectoryOnly);
                    if (files.Length > 0)
                        return files[0];
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"ProjectMetadataProvider: Failed to enumerate project files in {projectPath}: {ex.Message}");
                }
            }
            return null;
        }

        private static string DetectLanguageByExtension(string path)
        {
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return null;

            if (string.Equals(ext, ".csproj", StringComparison.OrdinalIgnoreCase)) return "C#";
            if (string.Equals(ext, ".vbproj", StringComparison.OrdinalIgnoreCase)) return "VB.NET";
            if (string.Equals(ext, ".fsproj", StringComparison.OrdinalIgnoreCase)) return "F#";
            if (string.Equals(ext, ".vcxproj", StringComparison.OrdinalIgnoreCase)) return "C++";
            if (string.Equals(ext, ".jsproj", StringComparison.OrdinalIgnoreCase)) return "JavaScript";
            if (string.Equals(ext, ".pyproj", StringComparison.OrdinalIgnoreCase)) return "Python";
            return null;
        }

        private static string ExtractTargetFrameworkFromContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;

            int idx = 0;
            while (idx < content.Length)
            {
                int tfStart = content.IndexOf("<TargetFramework", idx, StringComparison.OrdinalIgnoreCase);
                if (tfStart < 0) break;

                int gt = content.IndexOf('>', tfStart);
                if (gt < 0)
                {
                    idx = tfStart + "<TargetFramework".Length;
                    continue;
                }

                int lt = content.IndexOf('<', gt + 1);
                if (lt < 0) break;

                string rawValue = content.Substring(gt + 1, lt - gt - 1).Trim();
                if (!string.IsNullOrEmpty(rawValue))
                {
                    int tagNameStart = tfStart + 1;
                    int tagNameEnd = content.IndexOfAny(_tagNameDelimiters, tagNameStart);
                    if (tagNameEnd < 0) tagNameEnd = content.Length;

                    bool isPlural = false;
                    if (tagNameEnd - tagNameStart >= "TargetFrameworks".Length)
                    {
                        isPlural = string.Compare(content, tagNameStart, "TargetFrameworks", 0, "TargetFrameworks".Length, StringComparison.OrdinalIgnoreCase) == 0;
                    }

                    if (isPlural)
                    {
                        int semicolon = rawValue.IndexOf(';');
                        return semicolon > 0 ? rawValue.Substring(0, semicolon) : rawValue;
                    }
                    return rawValue;
                }
                idx = lt + 1;
            }
            return null;
        }

        /// <summary>
        /// Returns true when the project file contains <UseNativeUnitTest>true</UseNativeUnitTest>.
        /// </summary>
        private static bool ExtractIsNativeUnitTestFromContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return false;

            int idx = content.IndexOf("<UseNativeUnitTest", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;

            int gt = content.IndexOf('>', idx);
            if (gt < 0) return false;

            int lt = content.IndexOf('<', gt + 1);
            if (lt < 0) return false;

            string rawValue = content.Substring(gt + 1, lt - gt - 1).Trim();
            return string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string DetectLanguageFromContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;

            if (content.IndexOf("csharp", StringComparison.OrdinalIgnoreCase) >= 0) return "C#";
            if (content.IndexOf("vbnet", StringComparison.OrdinalIgnoreCase) >= 0) return "VB.NET";
            if (content.IndexOf("fsharp", StringComparison.OrdinalIgnoreCase) >= 0) return "F#";
            return null;
        }
    }

}

