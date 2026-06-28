using System.Collections.Generic;
using System.Linq;
using LMLocal.Infrastructure.Persistence;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace LMLocal.Infrastructure.Syntax
{

    /// <summary>
    /// Provides fast syntax checking for C# source code using Roslyn.
    /// </summary>
    public interface ISyntaxChecker
    {
        /// <summary>
        /// Checks whether the given C# source code is syntactically valid.
        /// </summary>
        bool IsSyntaxValid(string sourceCode, out List<Diagnostic> errors);

        /// <summary>
        /// Checks whether the C# file at the specified path is syntactically valid.
        /// </summary>
        bool IsFileSyntaxValid(string filePath, out List<Diagnostic> errors);

        bool IsSupported(string filePath);
    }

    /// <summary>
    /// Fast syntax checker using Roslyn's C# parser.
    /// Uses IFileSystem for file access to support testability.
    /// </summary>
    internal class CSharpSyntaxChecker : ISyntaxChecker
    {
        private readonly IFileSystem _fileSystem;

        public CSharpSyntaxChecker(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? throw new System.ArgumentNullException(nameof(fileSystem));
        }

        public bool IsSyntaxValid(string sourceCode, out List<Diagnostic> errors)
        {
            if (string.IsNullOrEmpty(sourceCode))
            {
                errors = new List<Diagnostic>();
                return false;
            }

            var tree = CSharpSyntaxTree.ParseText(sourceCode);
            var diagnostics = tree.GetDiagnostics();
            errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            return errors.Count == 0;
        }

        public bool IsFileSyntaxValid(string filePath, out List<Diagnostic> errors)
        {
            errors = new List<Diagnostic>();

            if (string.IsNullOrEmpty(filePath))
                return false;

            if (!_fileSystem.FileExists(filePath))
                return false;

            try
            {
                string source = _fileSystem.ReadAllText(filePath);
                return IsSyntaxValid(source, out errors);
            }
            catch
            {
                return false;
            }
        }

        public bool IsSupported(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;
            string extension = _fileSystem.GetFileExtension(filePath);
            if (string.IsNullOrEmpty(extension))
                return false;
            return extension.Equals(".cs", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
