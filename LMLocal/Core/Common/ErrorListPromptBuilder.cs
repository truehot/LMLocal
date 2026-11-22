using System;
using System.Collections.Generic;
using System.Text;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// Builds the prompt that accompanies the selected Visual Studio Error List entries.
    /// Pure formatting: knows nothing about Visual Studio, DTE or the Error List API.
    /// </summary>
    internal static class ErrorListPromptBuilder
    {
        private const string LocationIndent = "   ";

        /// <summary>
        /// Builds the instruction plus the numbered list of entries, or null when there is nothing to send.
        /// </summary>
        /// <param name="instruction">Command instruction placed above the list. May be null or empty.</param>
        /// <param name="entries">Entries as selected in the Error List, in display order.</param>
        /// <param name="toRelativePath">Optional path converter (full path to solution-relative path). May be null.</param>
        public static string Build(
            string instruction,
            IReadOnlyList<ErrorListEntry> entries,
            Func<string, string> toRelativePath)
        {
            if (entries == null || entries.Count == 0)
                return null;

            var lines = new List<string>();
            int printable = 0;
            int shown = 0;

            foreach (var entry in entries)
            {
                if (entry == null || IsEmpty(entry))
                    continue;

                printable++;

                if (shown >= PromptContentLimits.MaxErrorListEntries)
                    continue;

                lines.Add(RenderEntry(shown + 1, entry, toRelativePath));
                shown++;
            }

            if (shown == 0)
                return null;

            int skipped = printable - shown;
            if (skipped > 0)
            {
                lines.Add($"…and {skipped} more {(skipped == 1 ? "entry" : "entries")} not shown");
                InternalLogger.Warn(
                    $"ExplainError: only the first {PromptContentLimits.MaxErrorListEntries} of {printable} selected entries were sent.");
            }

            var result = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(instruction))
                result.Append(instruction.Trim()).Append("\n\n");

            result.Append(string.Join("\n", lines));
            return result.ToString();
        }

        /// <summary>
        /// An entry that carries no information at all: no message, no code, no location, no project.
        /// </summary>
        private static bool IsEmpty(ErrorListEntry entry)
        {
            return string.IsNullOrWhiteSpace(entry.Text)
                && string.IsNullOrWhiteSpace(entry.ErrorCode)
                && string.IsNullOrWhiteSpace(entry.Severity)
                && string.IsNullOrWhiteSpace(entry.FilePath)
                && string.IsNullOrWhiteSpace(entry.ProjectName);
        }

        private static string RenderEntry(int index, ErrorListEntry entry, Func<string, string> toRelativePath)
        {
            var result = new StringBuilder();
            result.Append(index).Append(". ").Append(BuildHead(entry));

            string location = BuildLocation(entry, toRelativePath);
            if (location != null)
                result.Append("\n").Append(LocationIndent).Append(location);

            string project = BuildProject(entry);
            if (project != null)
                result.Append("\n").Append(LocationIndent).Append(project);

            return result.ToString();
        }

        /// <summary>
        /// "error CS0103: The name 'foo' does not exist in the current context" — every part except the message is optional.
        /// </summary>
        private static string BuildHead(ErrorListEntry entry)
        {
            string severity = Trimmed(entry.Severity);
            string code = Trimmed(entry.ErrorCode);
            string text = Trimmed(entry.Text);

            string prefix;
            if (severity != null && code != null)
                prefix = $"{severity} {code}";
            else
                prefix = severity ?? code;

            if (prefix == null)
                return text ?? "(no message)";

            return text == null ? prefix : $"{prefix}: {text}";
        }

        /// <summary>
        /// "File: LMLocal\Commands\X.cs, line 42, column 7" — omitted for entries without a document.
        /// </summary>
        private static string BuildLocation(ErrorListEntry entry, Func<string, string> toRelativePath)
        {
            string fullPath = Trimmed(entry.FilePath);
            if (fullPath == null)
                return null;

            // The Error List reports 0-based positions; the prompt shows them the way Visual Studio does.
            int line = (entry.Line >= 0 ? entry.Line : 0) + 1;
            int column = (entry.Column >= 0 ? entry.Column : 0) + 1;

            return $"File: {ToDisplayPath(fullPath, toRelativePath)}, line {line}, column {column}";
        }

        private static string BuildProject(ErrorListEntry entry)
        {
            string project = Trimmed(entry.ProjectName);
            return project == null ? null : $"Project: {project}";
        }

        /// <summary>
        /// Applies the caller's path converter. A converter that throws or returns nothing degrades to the full path.
        /// </summary>
        private static string ToDisplayPath(string fullPath, Func<string, string> toRelativePath)
        {
            if (toRelativePath == null)
                return fullPath;

            try
            {
                string converted = toRelativePath(fullPath);
                return string.IsNullOrWhiteSpace(converted) ? fullPath : converted;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"ExplainError: cannot relativize '{fullPath}': {ex.Message}");
                return fullPath;
            }
        }

        private static string Trimmed(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
