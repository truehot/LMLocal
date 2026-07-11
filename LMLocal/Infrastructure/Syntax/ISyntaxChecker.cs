using System.Collections.Generic;

namespace LMLocal.Infrastructure.Syntax
{
    /// <summary>
    /// Represents a syntax error found during syntax validation.
    /// </summary>
    public class SyntaxError
    {
        public string Id { get; set; }
        public int StartLine { get; set; }
        public int StartColumn { get; set; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }
        public string Message { get; set; }
        public string Severity { get; set; } = "Error";

        public string GetMessage() => Message;
    }

    /// <summary>
    /// Provides fast syntax checking for source code files.
    /// </summary>
    public interface ISyntaxChecker
    {
        /// <summary>
        /// Checks whether the given source code is syntactically valid.
        /// </summary>
        bool IsSyntaxValid(string sourceCode, out List<SyntaxError> errors);

        bool IsSupported(string filePath);
    }
}
