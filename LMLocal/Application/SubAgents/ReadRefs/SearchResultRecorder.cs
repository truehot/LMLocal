using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;

namespace LMLocal.Application.SubAgents.ReadRefs
{
    /// <summary>
    /// Records successful <c>search_file_content</c> results (both 'file' and 'text' grouping) into the per-run <see cref="SearchLedger"/>.
    /// </summary>
    internal static class SearchResultRecorder
    {
        public const string SearchFileContentToolName = "search_file_content";

        /// <summary>Adds every returned match; returns the number of recorded lines.</summary>
        public static int TryRecord(SearchLedger ledger, object result)
        {
            if (ledger == null || result == null)
                return 0;

            int recorded = 0;

            if (result is SearchResultsResponse byFile && byFile.Success && byFile.Results != null)
            {
                foreach (var file in byFile.Results)
                {
                    if (file?.Matches == null || string.IsNullOrEmpty(file.FilePath))
                        continue;

                    foreach (var match in file.Matches)
                    {
                        if (match == null)
                            continue;
                        ledger.Add(new SearchRecord(file.FilePath, match.LineNumber, match.LineText));
                        recorded++;
                    }
                }
            }
            else if (result is SearchTextGroupResponse byText && byText.Success && byText.Results != null)
            {
                foreach (var group in byText.Results)
                {
                    if (group?.Files == null)
                        continue;

                    foreach (var file in group.Files)
                    {
                        if (file?.Lines == null || string.IsNullOrEmpty(file.FilePath))
                            continue;

                        foreach (var line in file.Lines)
                        {
                            ledger.Add(new SearchRecord(file.FilePath, line, group.Text));
                            recorded++;
                        }
                    }
                }
            }

            return recorded;
        }
    }
}
