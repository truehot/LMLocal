using System;
using System.Collections.Generic;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// Source-file extensions per language, used to decide which files count as project source files.
    /// </summary>
    internal static class SourceFileExtensions
    {
        private static readonly HashSet<string> DotNet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".vb", ".fs", ".xaml", ".resx"
        };

        private static readonly HashSet<string> Cpp = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cpp", ".cc", ".cxx", ".c++", ".c",
            ".h", ".hh", ".hpp", ".hxx", ".h++",
            ".inl", ".ipp", ".def", ".idl"
        };

        /// <summary>
        /// Returns true when <paramref name="extension"/> (e.g. ".cs") is a source file for the given <paramref name="language"/>. 
        /// </summary>
        public static bool IsSourceFile(string extension, string language)
        {
            if (string.IsNullOrEmpty(extension))
                return false;

            if (language != null && language.IndexOf("C++", StringComparison.OrdinalIgnoreCase) >= 0)
                return Cpp.Contains(extension);

            return DotNet.Contains(extension);
        }
    }
}
