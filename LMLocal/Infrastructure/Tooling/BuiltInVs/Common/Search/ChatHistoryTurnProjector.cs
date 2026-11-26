using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LMLocal.Core.Common;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// Reduces a logged message to what is worth searching: the user's own words, the answer text, and the file paths named there.
    /// The fence format itself is read by <see cref="MarkdownCodeBlockFormatter"/>, which also writes it.
    /// </summary>
    internal static class ChatHistoryTurnProjector
    {
        /// <summary>Separator <c>ChatHistoryManager.ConsolidateLastExchangeAsync</c> puts between the question and the tool-result dump.</summary>
        private const string ToolResultsSeparator = "\n\n---\n\n## Tool Results";

        /// <summary>Line of the consolidated tool-result dump that lists the files changed in the step.</summary>
        private const string ChangedFilesPrefix = "Changed in this step (re-read before use):";

        /// <summary>Marker the changed-files line puts after a deleted file.</summary>
        private const string DeletedSuffix = " (deleted)";

        /// <summary>File extensions by which a path inside prose is recognised.</summary>
        private const string FileExtensions =
            "csx|cs|md|markdown|jsonl|json|xml|xaml|config|csproj|vcxproj|sln|vsct|" +
            "js|mjs|cjs|jsx|ts|tsx|css|scss|html|htm|razor|cshtml|yml|yaml|txt|sql|ps1|sh";

        private static readonly Regex FilePathRegex = new Regex(
            @"(?<![\w.\-])(?:[\w.\-]+[\\/])*[\w\-]+\.(?:" + FileExtensions + @")\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// Splits a logged user message into the user's own text (every fenced block removed, inline code kept) and the paths
        /// of the files inserted into it (file fences and the "Changed in this step" line of a consolidated message).
        /// </summary>
        public static void ProjectQuestion(string raw, out string question, out List<string> files)
        {
            files = new List<string>();

            if (string.IsNullOrEmpty(raw))
            {
                question = string.Empty;
                return;
            }

            string text = KnowledgeScorer.NormalizeLineEndings(raw);

            int separator = text.IndexOf(ToolResultsSeparator, StringComparison.Ordinal);
            if (separator >= 0)
            {
                CollectChangedFiles(text.Substring(separator + ToolResultsSeparator.Length), files);
                text = text.Substring(0, separator);
            }

            text = MarkdownCodeBlockFormatter.RemoveCodeBlocks(text, files);
            question = text.Trim();
        }

        /// <summary>
        /// Splits a logged assistant answer into its prose (every fenced block removed, inline code kept) and the paths the prose names.
        /// </summary>
        public static void ProjectAnswer(string raw, out string answer, out List<string> files)
        {
            files = new List<string>();

            if (string.IsNullOrEmpty(raw))
            {
                answer = string.Empty;
                return;
            }

            answer = MarkdownCodeBlockFormatter.RemoveCodeBlocks(KnowledgeScorer.NormalizeLineEndings(raw)).Trim();
            CollectPaths(answer, files);
        }

        /// <summary>
        /// Adds the paths of the "Changed in this step" line of a consolidated tool-result dump.
        /// </summary>
        private static void CollectChangedFiles(string dump, List<string> files)
        {
            if (string.IsNullOrEmpty(dump))
                return;

            foreach (string rawLine in dump.Split('\n'))
            {
                string line = rawLine.Trim();
                if (!line.StartsWith(ChangedFilesPrefix, StringComparison.Ordinal))
                    continue;

                string list = line.Substring(ChangedFilesPrefix.Length).Trim();
                foreach (string item in list.Split(','))
                {
                    string path = item.Trim();
                    if (path.EndsWith(DeletedSuffix, StringComparison.Ordinal))
                        path = path.Substring(0, path.Length - DeletedSuffix.Length).Trim();

                    AddUnique(files, path);
                }
            }
        }

        /// <summary>
        /// Adds the file paths that appear in prose, in order of first appearance and without duplicates.
        /// </summary>
        private static void CollectPaths(string text, List<string> files)
        {
            if (string.IsNullOrEmpty(text))
                return;

            foreach (Match match in FilePathRegex.Matches(text))
                AddUnique(files, match.Value);
        }

        private static void AddUnique(List<string> files, string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            foreach (string existing in files)
            {
                if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            files.Add(path);
        }
    }
}
