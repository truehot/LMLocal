using System;
using System.Collections.Generic;
using System.IO;
using LMLocal.Common;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Resolves and manipulates file system paths used by Visual Studio integration.
    /// Provides helpers to compute relative paths, resolve file paths against a solution
    /// directory, and check whether a path is located inside a given directory.
    /// </summary>
    internal interface IPathResolver
    {
        /// <summary>
        /// Determines whether <paramref name="filePath"/> is located inside <paramref name="directory"/>.
        /// Paths are normalized and comparison is case-insensitive; trailing directory separators are handled.
        /// </summary>
        bool IsPathInsideDirectory(string filePath, string directory);


        /// <summary>
        /// Computes a relative path from <paramref name="basePath"/> to <paramref name="absolutePath"/>.
        /// Returns false when either path is null/empty or when paths are on different roots (drives/UNC).
        /// returns true.
        /// </summary>/// 
        bool TryGetRelativePath(string absolutePath, string basePath, out string relativePath);

        /// <summary>
        /// Resolves <paramref name="filePath"/> to an absolute path. If <paramref name="filePath"/> is rooted (absolute),
        /// it is normalized and returned. If <paramref name="filePath"/> is relative, it is combined with <paramref name="solutionDir"/>.
        /// <paramref name="solutionDir"/> is always required; it is ignored for rooted paths but must be provided.
        /// Returns false if <paramref name="filePath"/> or <paramref name="solutionDir"/> is null/empty, or if resolution fails.
        /// </summary>
        bool TryResolveFilePath(string filePath, string solutionDir, out string resolvedPath);
    }

    internal class PathResolver : IPathResolver
    {

        public bool TryGetRelativePath(string absolutePath, string basePath, out string relativePath)
        {
            relativePath = null;

            if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(absolutePath))
                return false;

            string fullBase = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullFile = Path.GetFullPath(absolutePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            string baseRoot = Path.GetPathRoot(fullBase);
            string fileRoot = Path.GetPathRoot(fullFile);
            if (!string.Equals(baseRoot, fileRoot, StringComparison.OrdinalIgnoreCase))
                return false;

            // Additional safeguard for drive letter
            bool origBaseHasDrive = basePath.Length >= 2 && basePath[1] == ':';
            bool origFileHasDrive = absolutePath.Length >= 2 && absolutePath[1] == ':';
            if (origBaseHasDrive != origFileHasDrive)
                return false;

            string[] baseParts = fullBase
                                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string[] fileParts = fullFile
                                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            string[] origFileParts = absolutePath
                                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            int common = 0;
            while (common < baseParts.Length && common < fileParts.Length && string.Equals(baseParts[common], fileParts[common], StringComparison.OrdinalIgnoreCase))
            {
                common++;
            }

            var relParts = new List<string>();
            for (int i = common; i < baseParts.Length; i++)
                relParts.Add("..");

            for (int i = common; i < fileParts.Length; i++)
                relParts.Add(origFileParts[i]);

            string relative = string.Join(Path.DirectorySeparatorChar.ToString(), relParts);
            relativePath = string.IsNullOrEmpty(relative) ? "." : relative;
            return true;
        }

        public bool TryResolveFilePath(string filePath, string solutionDir, out string resolvedPath)
        {
            resolvedPath = null;

            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(solutionDir))
                return false;

            try
            {
                if (Path.IsPathRooted(filePath))
                {
                    resolvedPath = Path.GetFullPath(filePath);
                    return true;
                }

                resolvedPath = Path.GetFullPath(Path.Combine(solutionDir, filePath));
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"TryResolveFilePath failed for filePath='{filePath}', solutionDir='{solutionDir}': {ex.Message}");
                resolvedPath = null;
                return false;
            }
        }

        public bool IsPathInsideDirectory(string filePath, string directory)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(directory))
                return false;

            string normalizedFile;
            string normalizedDir;
            try
            {
                normalizedFile = Path.GetFullPath(filePath);
                normalizedDir = Path.GetFullPath(directory);
            }
            catch (Exception ex)
            {
                InternalLogger.Debug($"IsPathInsideDirectory normalization failed for filePath='{filePath}', directory='{directory}': {ex.Message}");
                return false;
            }

            if (!normalizedDir.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
                !normalizedDir.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                normalizedDir += Path.DirectorySeparatorChar;
            }

            return normalizedFile.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase);
        }
    }
}
