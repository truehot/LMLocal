using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace LMLocal.Infrastructure.Syntax
{
    public static class SyntaxCheckerExtensions
    {
        public static string GetErrorReport(this IEnumerable<Diagnostic> errors)
        {
            if (errors == null || !errors.Any())
                return "No syntax errors.";

            return string.Join("\n", errors.Select(e =>
                $"[Line {e.Location.GetLineSpan().StartLinePosition.Line + 1}] {e.GetMessage()}"));
        }
    }
}
