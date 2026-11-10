using System;
using System.Collections.Generic;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// Selects one page of already-ranked search results for both grouping modes.
    /// </summary>
    internal static class SearchResultPaginator
    {
        /// <summary>
        /// File mode: the page that starts at match <paramref name="offset"/> and holds at most
        /// <paramref name="matchesPerPage"/> matches.
        /// </summary>
        public static List<SearchResult> SliceByMatches(IReadOnlyList<SearchResult> allResults, int offset, int matchesPerPage)
        {
            var page = new List<SearchResult>();
            if (allResults == null || allResults.Count == 0)
                return page;

            if (matchesPerPage <= 0)
                matchesPerPage = 1;
            if (offset < 0)
                offset = 0;

            int skipped = 0;
            int filled = 0;

            foreach (var result in allResults)
            {
                int available = result?.Matches?.Count ?? 0;
                int startIndex = 0;

                if (skipped < offset)
                {
                    int toSkip = Math.Min(offset - skipped, available);
                    skipped += toSkip;
                    startIndex = toSkip;
                }

                int remainingInFile = available - startIndex;
                if (remainingInFile <= 0)
                    continue;

                int take = Math.Min(remainingInFile, matchesPerPage - filled);
                if (take <= 0)
                    break;

                if (take == available)
                {
                    page.Add(result);
                }
                else
                {
                    page.Add(new SearchResult
                    {
                        FilePath = result.FilePath,
                        Matches = result.Matches.GetRange(startIndex, take),
                        MatchCount = take,
                        Score = result.Score
                    });
                }

                filled += take;
                if (filled >= matchesPerPage)
                    break;
            }

            return page;
        }

        /// <summary>
        /// Text mode: the page of groups that starts at group <paramref name="offset"/>.
        /// </summary>
        public static List<SearchTextGroupResult> SliceByGroups(IReadOnlyList<SearchTextGroupResult> groups, int offset, int groupsPerPage)
        {
            var page = new List<SearchTextGroupResult>();
            if (groups == null || groups.Count == 0)
                return page;

            if (groupsPerPage <= 0)
                groupsPerPage = 1;
            if (offset < 0)
                offset = 0;

            for (int i = offset; i < groups.Count && page.Count < groupsPerPage; i++)
                page.Add(groups[i]);

            return page;
        }
    }
}
