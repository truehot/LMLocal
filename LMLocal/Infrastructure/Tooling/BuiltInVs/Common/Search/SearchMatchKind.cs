namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// Kind of code declaration matched on a line.
    /// </summary>
    public enum SearchMatchKind
    {
        Other = 0,
        Type = 1,
        Function = 2,
        Property = 3,
        Field = 4,
        Member = 5
    }

    /// <summary>
    /// Weights used when scoring a file's relevance for search_file_content.
    /// </summary>
    internal static class DeclarationWeights
    {
        public const int Type = 8;
        public const int Function = 6;
        public const int Property = 3;
        public const int Member = 2;
        public const int Field = 1;
        public const int ExactWordBonus = 2;

        public static int WeightOf(SearchMatchKind kind)
        {
            switch (kind)
            {
                case SearchMatchKind.Type:
                    return Type;
                case SearchMatchKind.Function:
                    return Function;
                case SearchMatchKind.Property:
                    return Property;
                case SearchMatchKind.Member:
                    return Member;
                case SearchMatchKind.Field:
                    return Field;
                default:
                    return 0;
            }
        }
    }
}
