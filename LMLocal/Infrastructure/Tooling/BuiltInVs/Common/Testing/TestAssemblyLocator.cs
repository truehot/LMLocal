using System;
using System.Collections.Generic;
using System.IO;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Testing
{
    /// <summary>
    /// Turns the project configuration's raw <c>OutputPath</c> into the absolute build output directory and the path
    /// of the project's own assembly inside it.
    /// </summary>
    internal static class TestAssemblyLocator
    {
        /// <summary>
        /// Absolute output directory without a trailing separator, or null when it cannot be resolved.
        /// </summary>
        public static string ResolveOutputDirectory(string projectDirectory, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory) || string.IsNullOrWhiteSpace(outputPath))
                return null;

            try
            {
                string fullPath = Path.IsPathRooted(outputPath)
                    ? Path.GetFullPath(outputPath)
                    : Path.GetFullPath(Path.Combine(Path.GetFullPath(projectDirectory), outputPath));

                string trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return trimmed.Length == 0 ? null : trimmed;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (PathTooLongException)
            {
                return null;
            }
        }

        /// <summary>
        /// Candidate paths of the project's test assembly, most trusted first: the <c>OutputPath</c> directory when it
        /// is known, otherwise the legacy <c>bin\&lt;configuration&gt;</c> layout when the project configuration is known.
        /// </summary>
        public static List<string> ResolveAssemblyCandidates(
            string projectDirectory,
            string projectFileName,
            string outputPath,
            string configurationName)
        {
            var candidates = new List<string>();

            string assemblyName = GetAssemblyName(projectFileName);
            if (assemblyName == null || string.IsNullOrWhiteSpace(projectDirectory))
                return candidates;

            string outputDirectory = ResolveOutputDirectory(projectDirectory, outputPath);
            if (outputDirectory != null)
            {
                AddCandidate(candidates, outputDirectory, assemblyName);
                return candidates;
            }

            if (!string.IsNullOrWhiteSpace(configurationName) && IsSafeDirectoryName(configurationName))
            {
                string fallbackOutputPath = Path.Combine("bin", configurationName);
                AddCandidate(candidates, ResolveOutputDirectory(projectDirectory, fallbackOutputPath), assemblyName);
            }

            return candidates;
        }

        private static void AddCandidate(List<string> candidates, string outputDirectory, string assemblyName)
        {
            if (string.IsNullOrEmpty(outputDirectory))
                return;

            try
            {
                string assemblyPath = Path.Combine(outputDirectory, assemblyName + ".dll");
                if (!candidates.Contains(assemblyPath))
                    candidates.Add(assemblyPath);
            }
            catch (ArgumentException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        private static string GetAssemblyName(string projectFileName)
        {
            if (string.IsNullOrWhiteSpace(projectFileName))
                return null;

            string assemblyName;
            try
            {
                assemblyName = Path.GetFileNameWithoutExtension(projectFileName);
            }
            catch (ArgumentException)
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(assemblyName) ? null : assemblyName;
        }

        private static bool IsSafeDirectoryName(string value)
        {
            return value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                   value.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) < 0;
        }
    }
}
