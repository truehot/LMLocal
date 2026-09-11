using System;
using System.Collections.Generic;
using System.Linq;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// Splits already-ranked search results into pages for both grouping modes.
    /// file mode ranks in SearchFileContent (score, then match_count, then file_path);
    /// text mode ranks in SearchResultGrouper (occurrence_count, then text).
    /// </summary>
    internal static class SearchResultPaginator
    {
        /// <summary>
        /// File mode: splits ranked SearchResult entries by the number of matches per page.
        /// </summary>
        public static List<FileSearchPage> PaginateByMatches(IReadOnlyList<SearchResult> allResults, int matchesPerPage)
        {
            var pages = new List<FileSearchPage>();
            if (allResults == null || allResults.Count == 0)
                return pages;
            if (matchesPerPage <= 0)
                matchesPerPage = 1;

            var currentPage = new List<SearchResult>();
            int matchCount = 0;
            int totalMatches = allResults.Sum(r => r.MatchCount);
            int totalFiles = allResults.Count;

            foreach (var result in allResults)
            {
                int remainingSpaceOnPage = matchesPerPage - matchCount;

                if (result.MatchCount <= remainingSpaceOnPage)
                {
                    currentPage.Add(result);
                    matchCount += result.MatchCount;

                    if (matchCount == matchesPerPage)
                    {
                        pages.Add(new FileSearchPage
                        {
                            Results = currentPage,
                            TotalMatches = totalMatches,
                            TotalFiles = totalFiles
                        });
                        currentPage = new List<SearchResult>();
                        matchCount = 0;
                    }
                }
                else
                {
                    int matchOffset = 0;

                    while (matchOffset < result.Matches.Count)
                    {
                        int spaceAvailableOnPage = matchesPerPage - matchCount;
                        int chunkSize = Math.Min(spaceAvailableOnPage, result.Matches.Count - matchOffset);

                        var chunk = new SearchResult
                        {
                            FilePath = result.FilePath,
                            Matches = result.Matches.GetRange(matchOffset, chunkSize),
                            MatchCount = chunkSize,
                            Score = result.Score
                        };

                        currentPage.Add(chunk);
                        matchCount += chunkSize;
                        matchOffset += chunkSize;

                        if (matchCount == matchesPerPage)
                        {
                            pages.Add(new FileSearchPage
                            {
                                Results = currentPage,
                                TotalMatches = totalMatches,
                                TotalFiles = totalFiles
                            });
                            currentPage = new List<SearchResult>();
                            matchCount = 0;
                        }
                    }
                }
            }

            if (currentPage.Count > 0)
            {
                pages.Add(new FileSearchPage
                {
                    Results = currentPage,
                    TotalMatches = totalMatches,
                    TotalFiles = totalFiles
                });
            }

            return pages;
        }

        /// <summary>
        /// Text mode: splits ranked SearchTextGroupResult entries by the number of groups per page.
        /// </summary>
        public static List<TextSearchPage> PaginateByGroups(IReadOnlyList<SearchTextGroupResult> groups, int groupsPerPage)
        {
            var pages = new List<TextSearchPage>();
            if (groups == null || groups.Count == 0)
                return pages;
            if (groupsPerPage <= 0)
                groupsPerPage = 1;

            int totalMatches = SearchResultGrouper.CountOccurrences(groups);
            int totalFiles = SearchResultGrouper.CountDistinctFiles(groups);

            var currentPage = new List<SearchTextGroupResult>();
            foreach (var group in groups)
            {
                currentPage.Add(group);

                if (currentPage.Count == groupsPerPage)
                {
                    pages.Add(new TextSearchPage
                    {
                        Results = currentPage,
                        TotalMatches = totalMatches,
                        TotalFiles = totalFiles
                    });
                    currentPage = new List<SearchTextGroupResult>();
                }
            }

            if (currentPage.Count > 0)
            {
                pages.Add(new TextSearchPage
                {
                    Results = currentPage,
                    TotalMatches = totalMatches,
                    TotalFiles = totalFiles
                });
            }

            return pages;
        }
    }
}
