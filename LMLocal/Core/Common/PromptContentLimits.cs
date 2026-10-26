namespace LMLocal.Core.Common
{
    /// <summary>
    /// Shared policy for how much file content may be packed into a single prompt.
    /// </summary>
    internal static class PromptContentLimits
    {
        /// <summary>
        /// Maximum number of files whose content is included in full.
        /// </summary>
        public const int MaxFilesWithContent = 10;

        /// <summary>
        /// Maximum total size of the included content, in bytes (200 KB).
        /// </summary>
        public const int MaxTotalContentBytes = 200 * 1024;
    }
}
