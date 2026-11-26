using System;
using System.Collections.Generic;
using System.Text;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git
{
    /// <summary>
    /// Builds the fixed git command line. The model never passes raw git arguments: every operation
    /// composes its command from these helpers, and user strings are escaped as single argv entries.
    /// </summary>
    internal static class GitArguments
    {
        /// <summary>Options applied to every git call: no fsmonitor hook, no gpg, no pager, no colours, literal UTF-8 paths.</summary>
        public static string Build(params string[] parts)
        {
            var result = new StringBuilder(
                "-c core.fsmonitor=false -c log.showSignature=false -c core.quotepath=false -c color.ui=never --no-pager");
            if (parts != null)
            {
                foreach (string part in parts)
                {
                    if (!string.IsNullOrEmpty(part))
                    {
                        if (!part.StartsWith(" ", StringComparison.Ordinal))
                            result.Append(' ');
                        result.Append(part);
                    }
                }
            }

            return result.ToString();
        }

        /// <summary>Appends the pathspec separator and the (already repository-relative) paths.</summary>
        public static string Paths(IEnumerable<string> repositoryRelativePaths)
        {
            var result = new StringBuilder(" --");
            if (repositoryRelativePaths == null)
                return result.ToString();

            foreach (string path in repositoryRelativePaths)
            {
                if (!string.IsNullOrEmpty(path))
                    result.Append(' ').Append(CommandLineEscaper.Escape(path));
            }

            return result.ToString();
        }

        /// <summary>Options shared by diff-producing commands.</summary>
        public static string DiffOptions(bool includeDiff, int contextLines, string ignoreWhitespace)
        {
            var result = new StringBuilder("--no-color --no-ext-diff --no-textconv -M");
            if (includeDiff)
                result.Append(" -U").Append(contextLines);

            switch (ignoreWhitespace)
            {
                case "cr_at_eol":
                    result.Append(" --ignore-cr-at-eol");
                    break;
                case "all":
                    result.Append(" -w");
                    break;
            }

            return result.ToString();
        }
    }
}
