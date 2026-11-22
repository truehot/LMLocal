namespace LMLocal.Core.Common
{
    /// <summary>
    /// A single Visual Studio Error List entry reduced to the fields a prompt needs.
    /// </summary>
    internal sealed class ErrorListEntry
    {
        /// <summary>
        /// Error code such as "CS0103" or "CA1822". Empty when the entry has none.
        /// </summary>
        public string ErrorCode { get; set; }

        /// <summary>
        /// Severity word ("error", "warning" or "message"). Null when the severity is unknown.
        /// </summary>
        public string Severity { get; set; }

        /// <summary>
        /// Message text of the entry.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Full path of the document the entry points at. Null or empty when the entry has no location
        /// (project-level build errors, MSBuild messages).
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Line as reported by the Error List table (0-based). The prompt builder prints it 1-based.
        /// </summary>
        public int Line { get; set; }

        /// <summary>
        /// Column as reported by the Error List table (0-based). The prompt builder prints it 1-based.
        /// </summary>
        public int Column { get; set; }

        /// <summary>
        /// Name of the project the entry belongs to. Empty when unknown.
        /// </summary>
        public string ProjectName { get; set; }
    }
}
