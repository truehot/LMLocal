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
        /// One test result line of the VSTest console logger: "  Passed Name [2 s]", "  Failed Name [5 ms]",
        /// "  Skipped Name". The name is anything followed by the duration in brackets, or a single token without it, so
        /// prose such as "Failed to load the adapter." is not a result line.
        /// </summary>
        private static readonly Regex ResultLineRegex = new Regex(
            @"^\s*(?<outcome>Passed|Failed|Skipped)\s+(?:(?<name>.+?)\s+\[[^\]]*\]|(?<name>\S+))\s*$",
            RegexOptions.Compiled);

        private static readonly Regex RunSummaryLineRegex = new Regex(
            @"^\s*(?:Test Run (?:Failed|Successful|Aborted)|Total tests:)",
            RegexOptions.Compiled);

        private const string BlockSeparator = "\n\n";
        private const string BlockTruncatedMarker = "\n  [details truncated]";

        /// <summary>
        /// Smallest share of the budget a failed block gets before blocks start being dropped: enough for the name
        /// and the error message, with the stack trace cut first.
        /// </summary>
        private const int MinCharsPerBlock = 600;

        /// <summary>
        /// Budget kept for the names of the failed tests whose blocks did not fit.
        /// </summary>
        private const int MoreFailedTailChars = 800;

        /// <summary>
        /// Extracts from the full output only the blocks of failed tests (the "Failed" / "[FAIL]" line and its details).
        /// </summary>
        public static string ExtractFailedDetails(string fullOutput, int maxChars = 0)
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
                        if (IsBlockEnd(next))
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

            return FormatFailedBlocks(errorBlocks, maxChars);
        }

        /// <summary>
        /// Joins failed blocks within <paramref name="maxChars"/> (no limit when it is not positive).
        /// </summary>
        private static string FormatFailedBlocks(List<string> blocks, int maxChars)
        {
            if (blocks.Count == 0)
                return null;

            string joined = string.Join(BlockSeparator, blocks);
            if (maxChars <= 0 || joined.Length <= maxChars)
                return joined;

            int separatorsLength = BlockSeparator.Length * (blocks.Count - 1);
            int perBlock = Math.Max(MinCharsPerBlock, (maxChars - separatorsLength) / blocks.Count);
            var shortened = new List<string>(blocks.Count);
            foreach (string block in blocks)
                shortened.Add(ShortenBlock(block, perBlock));

            joined = string.Join(BlockSeparator, shortened);
            if (joined.Length <= maxChars)
                return joined;

            int blocksBudget = Math.Max(MinCharsPerBlock, maxChars - MoreFailedTailChars);
            var sb = new StringBuilder();
            int shown = 0;
            while (shown < shortened.Count)
            {
                string block = shortened[shown];
                int separator = sb.Length == 0 ? 0 : BlockSeparator.Length;
                if (sb.Length + separator + block.Length > blocksBudget)
                {
                    if (shown == 0)
                    {
                        sb.Append(ShortenBlock(block, blocksBudget));
                        shown++;
                    }
                    break;
                }

                if (separator > 0)
                    sb.Append(BlockSeparator);
                sb.Append(block);
                shown++;
            }

            if (shown < blocks.Count)
            {
                var names = new List<string>();
                for (int i = shown; i < blocks.Count; i++)
                    names.Add(GetFailedTestName(blocks[i]));

                string tail = $"{BlockSeparator}... and {blocks.Count - shown} more failed: {string.Join(", ", names)}";
                if (tail.Length > MoreFailedTailChars)
                    tail = tail.Substring(0, MoreFailedTailChars - 3) + "...";
                sb.Append(tail);
            }

            return sb.ToString();
        }

        private static string ShortenBlock(string block, int maxChars)
        {
            if (block.Length <= maxChars)
                return block;

            int keep = Math.Max(0, maxChars - BlockTruncatedMarker.Length);
            return block.Substring(0, keep).TrimEnd() + BlockTruncatedMarker;
        }

        /// <summary>
        /// Test name from the first line of a failed block, or the whole trimmed line when it has another format.
        /// </summary>
        private static string GetFailedTestName(string block)
        {
            int lineEnd = block.IndexOfAny(new[] { '\r', '\n' });
            string firstLine = lineEnd < 0 ? block : block.Substring(0, lineEnd);

            var match = ResultLineRegex.Match(firstLine);
            return match.Success ? match.Groups["name"].Value : firstLine.Trim();
        }

        /// <summary>
        /// A line that ends a failed block: the next test result, an xUnit marker or a run summary line.
        /// </summary>
        private static bool IsBlockEnd(string line)
        {
            if (line.Contains("[FAIL]") || line.Contains("[PASS]") || line.Contains("[SKIP]") ||
                line.Contains("Passed:") || line.Contains("Failed:") || line.Contains("Skipped:"))
                return true;

            return ResultLineRegex.IsMatch(line) || RunSummaryLineRegex.IsMatch(line);
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
        /// Detects a line that starts a failed-test block: an xUnit "[FAIL]" marker or a VSTest result line whose outcome
        /// is "Failed".
        /// </summary>
        private static bool IsFailedTestLine(string line)
        {
            if (line.Contains("[FAIL]"))
                return true;

            var match = ResultLineRegex.Match(line);
            return match.Success && match.Groups["outcome"].Value == "Failed";
        }
    }
}
