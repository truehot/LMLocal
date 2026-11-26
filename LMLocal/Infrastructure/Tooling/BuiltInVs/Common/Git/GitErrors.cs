using System;
using System.Collections.Generic;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git
{
    /// <summary>A git command failed or the request cannot be satisfied (a model-facing error, not a crash).</summary>
    internal sealed class GitToolException : Exception
    {
        public GitToolException(string message) : base(message) { }
    }

    internal static class GitProcessGuard
    {
        /// <summary>Turns a non-zero git exit code into a short, model-facing error message.</summary>
        public static void EnsureSuccess(GitProcessResult result, string context)
        {
            if (result == null)
                throw new GitToolException(context + ": no result from git.");

            if (result.ExitCode == 0)
                return;

            string details = FirstLines(result.StandardError, 4);
            if (string.IsNullOrWhiteSpace(details))
                details = FirstLines(result.StandardOutput, 2);

            throw new GitToolException(string.IsNullOrWhiteSpace(details)
                ? context + $" (git exited with code {result.ExitCode})."
                : context + ": " + details);
        }

        private static string FirstLines(string text, int count)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var lines = new List<string>();
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                    continue;
                lines.Add(line);
                if (lines.Count == count)
                    break;
            }

            return string.Join(" ", lines);
        }
    }
}
