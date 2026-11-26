using System;
using System.IO;

namespace LMLocal.Infrastructure.Tooling
{
    /// <summary>
    /// Full paths for child processes. A bare name in ProcessStartInfo is also looked up in the current directory
    /// of the VS process before System32 and PATH, so a planted taskkill.exe/dotnet.exe there would run instead.
    /// </summary>
    internal static class ExecutablePath
    {
        /// <summary>A Windows tool from the system directory, e.g. taskkill.exe.</summary>
        public static string InSystemDirectory(string fileName)
        {
            return Path.Combine(Environment.SystemDirectory, fileName);
        }

        /// <summary>First rooted PATH entry that contains <paramref name="fileName"/>; never the current or a relative directory. Null when absent.</summary>
        public static string FindInPath(string fileName)
        {
            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string entry in pathValue.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(entry))
                    continue;

                string directory = entry.Trim().Trim('"');
                if (directory.Length == 0 || directory == "." || !Path.IsPathRooted(directory))
                    continue;

                string candidate;
                try { candidate = Path.Combine(directory, fileName); }
                catch (ArgumentException) { continue; }

                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }
    }
}
