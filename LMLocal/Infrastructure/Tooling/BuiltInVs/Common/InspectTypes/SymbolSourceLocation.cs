using System.Linq;
using Microsoft.CodeAnalysis;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.InspectTypes
{
    /// <summary>
    /// Where a symbol is declared in source: the absolute file path and the 1-based line of its name.
    /// </summary>
    internal readonly struct SymbolSourceLocation
    {
        public SymbolSourceLocation(string filePath, int line)
        {
            FilePath = filePath;
            Line = line;
        }

        /// <summary>Absolute path of the source file.</summary>
        public string FilePath { get; }

        /// <summary>1-based line of the symbol name.</summary>
        public int Line { get; }

        /// <summary>
        /// First source location of <paramref name="symbol"/>.
        /// </summary>
        public static bool TryGet(ISymbol symbol, out SymbolSourceLocation location)
        {
            location = default(SymbolSourceLocation);
            var source = symbol?.Locations.FirstOrDefault(l => l.IsInSource);
            string filePath = source?.SourceTree?.FilePath;
            if (string.IsNullOrEmpty(filePath))
                return false;

            location = new SymbolSourceLocation(filePath, source.GetLineSpan().StartLinePosition.Line + 1);
            return true;
        }
    }
}
