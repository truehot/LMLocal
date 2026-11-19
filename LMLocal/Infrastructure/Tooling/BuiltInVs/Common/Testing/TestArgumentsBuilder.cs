using System;
using System.Collections.Generic;
using System.Text;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Testing
{
    /// <summary>
    /// Builds command-line arguments for `dotnet test` and `dotnet vstest` and sanitizes every value that reaches
    /// the command line.
    /// </summary>
    internal static class TestArgumentsBuilder
    {
        private const string AllowedFilterChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._ -;(),";

        private const string AllowedConfigurationChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._- ";

        /// <summary>
        /// Separates the names of a normalized filter; never part of a name because the sanitizer drops it.
        /// </summary>
        private const char FilterTermSeparator = '|';

        /// <summary>
        /// VSTest property prefixes models copy from <c>dotnet test --filter</c> examples; the tool adds its own.
        /// </summary>
        private static readonly string[] VsTestPropertyPrefixes =
        {
            "FullyQualifiedName~", "FullyQualifiedName=", "Name~", "Name=", "ClassName~", "ClassName="
        };

        public static string SanitizeFilter(string value)
        {
            return Sanitize(value, AllowedFilterChars);
        }

        /// <summary>
        /// Turns the model's filter into name substrings joined by <c>|</c>, or null when nothing is left.
        /// </summary>
        public static string NormalizeFilter(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var terms = new List<string>();
            foreach (string rawTerm in SplitFilterTerms(value))
            {
                if (string.Equals(rawTerm, "OR", StringComparison.OrdinalIgnoreCase))
                    continue;

                string term = SanitizeFilter(StripVsTestPrefix(rawTerm).Trim('*'));
                if (!string.IsNullOrEmpty(term) && !terms.Contains(term))
                    terms.Add(term);
            }

            return terms.Count == 0 ? null : string.Join(FilterTermSeparator.ToString(), terms);
        }

        /// <summary>
        /// VSTest filter expression for a normalized filter: one <c>FullyQualifiedName~</c> condition per name, OR-ed.
        /// </summary>
        private static string BuildFilterExpression(string filter)
        {
            var conditions = new List<string>();
            foreach (string term in filter.Split(new[] { FilterTermSeparator }, StringSplitOptions.RemoveEmptyEntries))
                conditions.Add("FullyQualifiedName~" + term);

            return string.Join("|", conditions);
        }

        private static List<string> SplitFilterTerms(string value)
        {
            var terms = new List<string>();
            var current = new StringBuilder();
            int depth = 0;
            foreach (char c in value)
            {
                if (c == '(')
                    depth++;
                else if (c == ')' && depth > 0)
                    depth--;

                if (depth == 0 && (char.IsWhiteSpace(c) || c == FilterTermSeparator))
                {
                    if (current.Length > 0)
                        terms.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            if (current.Length > 0)
                terms.Add(current.ToString());

            return terms;
        }

        private static string StripVsTestPrefix(string term)
        {
            foreach (string prefix in VsTestPropertyPrefixes)
            {
                if (term.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return term.Substring(prefix.Length);
            }

            return term;
        }

        /// <summary>
        /// Configuration name safe to place in a quoted command-line argument.
        /// </summary>
        public static string SanitizeConfiguration(string value)
        {
            return Sanitize(value, AllowedConfigurationChars);
        }

        /// <summary>
        /// MSBuild platform to pass to <c>dotnet test</c>, or null for AnyCPU.
        /// </summary>
        public static string ResolveSdkPlatform(string platform)
        {
            string platformName = SanitizeConfiguration(platform);
            if (string.IsNullOrEmpty(platformName))
                return null;

            return string.Equals(platformName.Replace(" ", string.Empty), "AnyCPU", StringComparison.OrdinalIgnoreCase)
                ? null
                : platformName;
        }

        public static string BuildSdkTestArguments(string projectPath, string filter = null, string configuration = null, string platform = null)
        {
            string args = $"test \"{projectPath}\" --no-build --verbosity normal";

            string configurationName = SanitizeConfiguration(configuration);
            if (!string.IsNullOrEmpty(configurationName))
                args += $" -c \"{configurationName}\"";

            string platformName = ResolveSdkPlatform(platform);
            if (!string.IsNullOrEmpty(platformName))
                args += $" -p:Platform=\"{platformName}\"";

            if (!string.IsNullOrWhiteSpace(filter))
                args += $" --filter \"{BuildFilterExpression(filter)}\"";
            return args;
        }

        public static string BuildLegacyVstestArguments(string dllPath, string filter)
        {
            string args = $"vstest \"{dllPath}\"";
            if (!string.IsNullOrWhiteSpace(filter))
                args += $" --TestCaseFilter:\"{BuildFilterExpression(filter)}\"";
            args += " --logger:console;verbosity=normal";
            return args;
        }

        private static string Sanitize(string value, string allowedChars)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (allowedChars.IndexOf(c) >= 0)
                    sb.Append(c);
            }
            string result = sb.ToString().Trim();
            return result.Length == 0 ? null : result;
        }
    }
}
