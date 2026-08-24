using System;
using System.Text;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Testing
{
    /// <summary>
    /// Builds command-line arguments for `dotnet test`, `dotnet vstest` and `dotnet build`,
    /// always targeting the project's own Debug configuration (no solution configuration
    /// and no platform are passed), and sanitizing the user-supplied test filter so it
    /// cannot break or inject into the command line.
    /// </summary>
    internal static class TestArgumentsBuilder
    {
        private const string AllowedFilterChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._ -;(),";

        public static string SanitizeFilter(string value)
        {
            return Sanitize(value, AllowedFilterChars);
        }

        public static string BuildSdkTestArguments(string projectPath, string filter = null, bool restore = false)
        {
            string args = restore
                ? $"test \"{projectPath}\" --verbosity normal"
                : $"test \"{projectPath}\" --no-restore --verbosity normal";

            if (!string.IsNullOrWhiteSpace(filter))
                args += $" --filter \"FullyQualifiedName~{filter}\"";
            return args;
        }

        public static string BuildLegacyVstestArguments(string dllPath, string filter)
        {
            string args = $"vstest \"{dllPath}\"";
            if (!string.IsNullOrWhiteSpace(filter))
                args += $" --TestCaseFilter:\"FullyQualifiedName~{filter}\"";
            args += " --logger:console;verbosity=normal";
            return args;
        }

        public static string BuildBuildArguments(string projectPath, bool restore = false)
        {
            return restore
                ? $"build \"{projectPath}\" --configuration Debug"
                : $"build \"{projectPath}\" --no-restore --configuration Debug";
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
