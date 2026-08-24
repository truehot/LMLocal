using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Testing
{
    /// <summary>
    /// Pure parsing of `dotnet test` / `dotnet vstest` console output: summary statistics and failed-test detail blocks.
    /// </summary>
    internal static class TestOutputParser
    {
        public static (int Total, int Passed, int Failed, int Skipped) ParseStatisticsUniversal(string output)
        {
            int total = 0, passed = 0, failed = 0, skipped = 0;

            var totalMatch = Regex.Match(output, @"(?:Total tests:|total:)\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
            if (totalMatch.Success) int.TryParse(totalMatch.Groups[1].Value, out total);

            var passedMatch = Regex.Match(output, @"(?:Passed:|succeeded:)\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
            if (passedMatch.Success) int.TryParse(passedMatch.Groups[1].Value, out passed);

            var failedMatch = Regex.Match(output, @"(?:Failed:|failed:)\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
            if (failedMatch.Success) int.TryParse(failedMatch.Groups[1].Value, out failed);

            var skippedMatch = Regex.Match(output, @"(?:Skipped:|skipped:)\s*(\d+)", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
            if (skippedMatch.Success) int.TryParse(skippedMatch.Groups[1].Value, out skipped);

            if (total == 0 && (passed > 0 || failed > 0 || skipped > 0))
                total = passed + failed + skipped;

            return (total, passed, failed, skipped);
        }

        /// <summary>
        /// Extracts from the full output only the blocks related to failed tests lines with [FAIL] and following details).
        /// </summary>
        public static string ExtractFailedDetails(string fullOutput)
        {
            if (string.IsNullOrWhiteSpace(fullOutput))
                return null;

            var lines = fullOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var errorBlocks = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (IsFailedTestLine(line))
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(line);
                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        string next = lines[j];
                        if (next.Contains("[FAIL]") || next.Contains("[PASS]") || next.Contains("[SKIP]") ||
                            next.Contains("Passed:") || next.Contains("Failed:") || next.Contains("Skipped:"))
                            break;

                        if (string.IsNullOrWhiteSpace(next))
                        {
                            if (j + 1 < lines.Length && string.IsNullOrWhiteSpace(lines[j + 1]))
                                break;
                            sb.AppendLine();
                            continue;
                        }
                        sb.AppendLine(next);
                    }
                    errorBlocks.Add(sb.ToString().TrimEnd());
                }
            }

            if (errorBlocks.Count == 0)
                return null;

            return string.Join("\n\n", errorBlocks);
        }

        /// <summary>
        /// Extracts only diagnostic lines (compiler/MSBuild errors, warnings, [FAIL] markers, exception stack frames and final summary counters) and caps the result to maxChars.
        /// </summary>
        public static string ExtractDiagnosticSummary(string output, int maxChars = 6000, int tailLineCount = 40)
        {
            if (string.IsNullOrWhiteSpace(output))
                return null;
            if (maxChars <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxChars));

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
            var relevant = new List<string>();

            foreach (var raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.Length == 0)
                    continue;
                if (IsDiagnosticLine(line))
                    relevant.Add(line);
            }

            if (relevant.Count == 0)
            {
                for (int i = lines.Length - 1; i >= 0 && relevant.Count < tailLineCount; i--)
                {
                    string line = lines[i].TrimEnd();
                    if (line.Length != 0)
                        relevant.Add(line);
                }
                relevant.Reverse();
            }

            var sb = new StringBuilder();
            bool truncated = false;
            foreach (string line in relevant)
            {
                if (sb.Length + line.Length + Environment.NewLine.Length > maxChars)
                {
                    truncated = true;
                    break;
                }
                sb.AppendLine(line);
            }

            if (sb.Length == 0)
                return null;

            string result = sb.ToString().TrimEnd();
            return truncated
                ? result + Environment.NewLine + $"[diagnostic output truncated to {maxChars} chars]"
                : result;
        }

        private static bool IsDiagnosticLine(string line)
        {
            if (line.Contains(": error") || line.Contains("error CS") ||
                line.Contains("error MSB") || line.Contains("error NU"))
                return true;
            if (line.Contains("Exception") || line.Contains("[FAIL]") || line.Contains("Build FAILED"))
                return true;
            if (line.Contains(": warning") || line.Contains("Error(s)") || line.Contains("Warning(s)"))
                return true;

            string trimmed = line.TrimStart();
            return trimmed.StartsWith("at ", StringComparison.Ordinal);
        }

        /// <summary>
        /// Detects a line that starts a failed-test block: "[FAIL]" markers or a line mentioning "Failed" that is not a summary counter (e.g. "Passed! - Failed: 0, ...").
        /// </summary>
        private static bool IsFailedTestLine(string line)
        {
            if (line.Contains("[FAIL]"))
                return true;
            if (!line.Contains("Failed"))
                return false;

            if (line.Contains("Passed!") || Regex.IsMatch(line, @"Failed:\s*\d+"))
                return false;

            return true;
        }
    }
}
