namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// Determines how search results are grouped in the response.
    /// </summary>
    internal enum SearchGrouping
    {
        /// <summary>Group matches by file (default behaviour).</summary>
        File = 0,

        /// <summary>Group identical matching lines together, showing occurrence counts and file locations.</summary>
        Text = 1
    }
}
