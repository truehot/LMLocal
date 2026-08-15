using System.Collections.Generic;
using LMLocal.Core.Common;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Thin VS-layer adapter over the SourceFileFilter policy.
    /// </summary>
    internal static class VsFileFilter
    {
        /// <summary>
        /// Directory names that are pruned wherever they appear as a path component.
        /// </summary>
        public static HashSet<string> ExcludedDirectories => SourceFileFilter.ExcludedDirectories;

        /// <summary>
        /// Returns true when any path component (directory) is an excluded / generated directory.
        /// </summary>
        public static bool ShouldExcludePath(string path) => SourceFileFilter.ShouldExcludePath(path);

        /// <summary>
        /// Returns true when the file name refers to a binary, image, font, archive, document, media, minified or junk file.
        /// </summary>
        public static bool IsExcludedFile(string fileName) => SourceFileFilter.IsExcludedFile(fileName);

        /// <summary>
        /// Combined path + file filter.
        /// </summary>
        public static bool ShouldExclude(string path) => SourceFileFilter.ShouldExclude(path);
    }
}
