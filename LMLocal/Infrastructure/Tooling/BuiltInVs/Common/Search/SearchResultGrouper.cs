using System;
using System.Collections.Generic;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// Groups file-grouped search results by the matching line text. Pure, DTE-independent logic.
    /// </summary>
    internal static class SearchResultGrouper
    {
        /// <summary>
        /// Collapses file-grouped results into distinct matching lines, each with its occurrence count and the files (with line numbers) where it was found.
        /// </summary>
        public static List<SearchTextGroupResult> GroupByText(IReadOnlyList<SearchResult> results)
        {
            var byText = new Dictionary<string, Dictionary<string, List<int>>>(StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            if (results != null)
            {
                foreach (var result in results)
                {
                    if (result?.Matches == null)
                        continue;

                    foreach (var match in result.Matches)
                    {
                        string text = match.LineText ?? string.Empty;

                        if (!byText.TryGetValue(text, out var byFile))
                        {
                            byFile = new Dictionary<string, List<int>>(StringComparer.Ordinal);
                            byText[text] = byFile;
                            counts[text] = 0;
                        }

                        counts[text]++;

                        string file = result.FilePath ?? string.Empty;
                        if (!byFile.TryGetValue(file, out var lines))
                        {
                            lines = new List<int>();
                            byFile[file] = lines;
                        }

                        lines.Add(match.LineNumber);
                    }
                }
            }

            var groups = new List<SearchTextGroupResult>(byText.Count);
            foreach (var pair in byText)
            {
                var files = new List<SearchTextGroupFileInfo>(pair.Value.Count);
                foreach (var filePair in pair.Value)
                {
                    filePair.Value.Sort();
                    files.Add(new SearchTextGroupFileInfo
                    {
                        FilePath = filePair.Key,
                        Lines = filePair.Value
                    });
                }

                files.Sort((a, b) => string.CompareOrdinal(a.FilePath, b.FilePath));

                groups.Add(new SearchTextGroupResult
                {
                    Text = pair.Key,
                    OccurrenceCount = counts[pair.Key],
                    Files = files
                });
            }

            groups.Sort((a, b) =>
            {
                int c = b.OccurrenceCount.CompareTo(a.OccurrenceCount);
                if (c != 0) return c;
                return string.CompareOrdinal(a.Text, b.Text);
            });

            return groups;
        }

        /// <summary>
        /// Number of distinct files referenced by the given text groups.
        /// </summary>
        public static int CountDistinctFiles(IReadOnlyList<SearchTextGroupResult> groups)
        {
            var files = new HashSet<string>(StringComparer.Ordinal);
            if (groups != null)
            {
                foreach (var group in groups)
                {
                    if (group?.Files == null)
                        continue;

                    foreach (var file in group.Files)
                        files.Add(file.FilePath ?? string.Empty);
                }
            }

            return files.Count;
        }

        /// <summary>
        /// Total number of occurrences across the given text groups.
        /// </summary>
        public static int CountOccurrences(IReadOnlyList<SearchTextGroupResult> groups)
        {
            int total = 0;
            if (groups != null)
            {
                foreach (var group in groups)
                    total += group?.OccurrenceCount ?? 0;
            }

            return total;
        }
    }
}
