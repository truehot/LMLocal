using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;

namespace LMLocal.Infrastructure.Syntax
{
    /// <summary>
    /// Fast syntax checker using Roslyn's Visual Basic parser.
    /// </summary>
    internal class VisualBasicSyntaxChecker : ISyntaxChecker
    {
        public bool IsSyntaxValid(string sourceCode, out List<SyntaxError> errors)
        {
            if (string.IsNullOrEmpty(sourceCode))
            {
                errors = new List<SyntaxError>();
                return false;
            }

            var tree = VisualBasicSyntaxTree.ParseText(sourceCode);
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
    }
}