namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Typed contract between a file-writing tool result and <see cref="ToolFileOpener"/>: replaces the former reflection on the C# property name <c>FilePath</c>.
    /// </summary>
    internal interface IToolFileOpenResult
    {
        /// <summary>
        /// Path of the file the call wrote (relative to the solution or absolute).
        /// </summary>
        string FileToOpen { get; }

        /// <summary>
        /// True only when the call actually wrote the file: false for any error (including old_lines/expected_line that did not match) and for no-ops.
        /// </summary>
        bool WroteFile { get; }
    }
}
