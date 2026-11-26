namespace LMLocal.Application.SubAgents.ReadRefs
{
    /// <summary>
    /// A record of an actual <c>read_file_lines</c> result that was returned to the model.
    /// </summary>
    internal readonly struct ReadRecord
    {
        public ReadRecord(string relativePath, int startLine, int endLine, string absolutePath = null, int? totalLines = null, bool reachedEnd = false)
        {
            RelativePath = relativePath ?? string.Empty;
            AbsolutePath = absolutePath;
            StartLine = startLine;
            EndLine = endLine;
            TotalLines = totalLines;
            ReachedEnd = reachedEnd;
        }

        public string RelativePath { get; }

        /// <summary>Absolute on-disk path captured when the read happened (null when unknown).</summary>
        public string AbsolutePath { get; }

        public int StartLine { get; }

        public int EndLine { get; }

        /// <summary>The file's line count reported by the read (null when not reported).</summary>
        public int? TotalLines { get; }

        /// <summary>The read returned the last line of the file.</summary>
        public bool ReachedEnd { get; }

        public LineRange Range => new LineRange(StartLine, EndLine);

        public bool Contains(LineRange range) => range.Start >= StartLine && range.End <= EndLine;
    }
}
