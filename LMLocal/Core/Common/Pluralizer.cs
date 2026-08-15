namespace LMLocal.Core.Common
{
    /// <summary>
    /// Selects the singular or plural form of a word based on a numeric count.
    /// </summary>
    internal static class Pluralizer
    {
        /// <summary>
        /// Returns <paramref name="singular"/> when <paramref name="count"/> is 1, otherwise <paramref name="plural"/>.
        /// </summary>
        public static string Pluralize(int count, string singular, string plural)
        {
            return count == 1 ? singular : plural;
        }
    }
}
