using System.Collections.Generic;
using System.Linq;
using LMLocal.Infrastructure.Persistence;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace LMLocal.Infrastructure.Syntax
{
    /// <summary>
    /// Fast syntax checker using Roslyn's C# parser. Uses IFileSystem for file access to support testability.
    /// </summary>
    internal class CSharpSyntaxChecker : ISyntaxChecker
    {
        private readonly IFileSystem _fileSystem;

        public CSharpSyntaxChecker(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem ?? throw new System.ArgumentNullException(nameof(fileSystem));
        }

        public bool IsSyntaxValid(string sourceCode, out List<SyntaxError> errors)
        {
            if (string.IsNullOrEmpty(sourceCode))
            {
                errors = new List<SyntaxError>();
                return false;
            }

            var tree = CSharpSyntaxTree.ParseText(sourceCode);
            var diagnostics = tree.GetDiagnostics();
            var errorDiagnostics = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            errors = errorDiagnostics.Select(d => new SyntaxError
            {
                Id = d.Id,
                StartLine = d.Location.GetLineSpan().StartLinePosition.Line + 1,
                StartColumn = d.Location.GetLineSpan().StartLinePosition.Character + 1,
                EndLine = d.Location.GetLineSpan().EndLinePosition.Line + 1,
                EndColumn = d.Location.GetLineSpan().EndLinePosition.Character + 1,
                Message = d.GetMessage(),
                Severity = "Error"
            }).ToList();
            return errors.Count == 0;
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
