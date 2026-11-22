namespace LMLocal.Core.Models
{
    /// <summary>
    /// One stack frame of a debugger stop.
    /// </summary>
    public sealed class DebugStackFrameInfo
    {
        /// <summary>Fully qualified method name, for example <c>SomeService.Process</c>. Null when unknown.</summary>
        public string FunctionName { get; set; }

        /// <summary>Full path of the source file. Null when the frame has no source information.</summary>
        public string File { get; set; }

        /// <summary>One-based line inside <see cref="File"/>, 0 when unknown.</summary>
        public int Line { get; set; }
    }
}
