using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security;
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
        bool TryGetRelativeNormalizedPath(string absoluteNormalizedPath, string baseNormalizedPath, out string relativeNormalizedPath);


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

        private struct PathSegment
        {
            public int Start;
            public int Length;
            public PathSegment(int start, int length) { Start = start; Length = length; }
        }

        public bool TryGetRelativeNormalizedPath(string absoluteNormalizedPath, string baseNormalizedPath, out string relativePath)
        {
            relativePath = null;
            if (string.IsNullOrEmpty(baseNormalizedPath) || string.IsNullOrEmpty(absoluteNormalizedPath))
                return false;


            string baseDir = baseNormalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string file = absoluteNormalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!file.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                return false;


            if (file.Length == baseDir.Length)
            {
                relativePath = ".";
                return true;
            }
            if (file[baseDir.Length] != Path.DirectorySeparatorChar)
                return false;


            relativePath = file.Substring(baseDir.Length + 1);
            return true;
        }

        public bool TryGetRelativePath(string absolutePath, string basePath, out string relativePath)
        {
            relativePath = null;
            if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(absolutePath))
                return false;

            try
            {
                string fullBase = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string fullFile = Path.GetFullPath(absolutePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                string baseRoot = Path.GetPathRoot(fullBase);
                string fileRoot = Path.GetPathRoot(fullFile);

                if (!string.Equals(baseRoot, fileRoot, StringComparison.OrdinalIgnoreCase))
                    return false;

                var baseSegments = new List<PathSegment>();
                var fileSegments = new List<PathSegment>();

                GetSegments(fullBase, baseRoot.Length, baseSegments);
                GetSegments(fullFile, fileRoot.Length, fileSegments);

                int common = 0;
                while (common < baseSegments.Count && common < fileSegments.Count)
                {
                    var segBase = baseSegments[common];
                    var segFile = fileSegments[common];

                    if (!CompareSegments(fullBase, segBase, fullFile, segFile))
                        break;

                    common++;
                }

                int ups = baseSegments.Count - common;
                int remaining = fileSegments.Count - common;

                if (ups > 0)
                {
                    relativePath = null;
                    return false;
                }

                if (ups == 0 && remaining == 0)
                {
                    relativePath = ".";
                    return true;
                }

                var parts = new string[ups + remaining];
                for (int i = 0; i < ups; i++)
                    parts[i] = "..";

                for (int i = 0; i < remaining; i++)
                {
                    var seg = fileSegments[common + i];
                    parts[ups + i] = fullFile.Substring(seg.Start, seg.Length);
                }

                relativePath = string.Join(Path.DirectorySeparatorChar.ToString(), parts);
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (PathTooLongException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (SecurityException) { return false; }
        }

        private static void GetSegments(string path, int startOffset, List<PathSegment> output)
        {
            output.Clear();
            int start = startOffset;

            for (int i = startOffset; i < path.Length; i++)
            {
                if (path[i] == Path.DirectorySeparatorChar || path[i] == Path.AltDirectorySeparatorChar)
                {
                    if (i > start)
                        output.Add(new PathSegment(start, i - start));
                    start = i + 1;
                }
            }
            if (start < path.Length)
                output.Add(new PathSegment(start, path.Length - start));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CompareSegments(string strA, PathSegment segA, string strB, PathSegment segB)
        {
            if (segA.Length != segB.Length)
                return false;

            return string.Compare(strA, segA.Start, strB, segB.Start, segA.Length, StringComparison.OrdinalIgnoreCase) == 0;
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

            if (string.Equals(normalizedFile, normalizedDir, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!normalizedDir.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
                !normalizedDir.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                normalizedDir += Path.DirectorySeparatorChar;
            }

            return normalizedFile.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase);
        }
    }
}
