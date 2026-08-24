using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Projects
{
    /// <summary>
    /// Detects whether a .NET project file uses the SDK-style format (an Sdk attribute on the Project element).
    /// </summary>
    internal static class SdkProjectDetector
    {
        private const int HeaderLinesToRead = 30;

        private static readonly Regex SdkProjectRegex = new Regex(
            @"<Project\b[^>]*\bSdk\s*=\s*[""'](?<sdk>[^""']+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex XmlCommentRegex = new Regex(
            @"<!--.*?-->",
            RegexOptions.Singleline | RegexOptions.Compiled);

        public static async Task<bool> IsSdkStyleAsync(IFileSystem fileSystem, string projectPath, CancellationToken cancellationToken = default)
        {
            if (fileSystem == null) throw new ArgumentNullException(nameof(fileSystem));
            if (string.IsNullOrWhiteSpace(projectPath)) return false;

            try
            {
                var lines = await fileSystem.ReadLinesRangeAsync(projectPath, 1, HeaderLinesToRead, cancellationToken).ConfigureAwait(false);
                if (lines == null || lines.Count == 0)
                    return false;

                string projectHead = string.Join("\n", lines);
                projectHead = XmlCommentRegex.Replace(projectHead, string.Empty);

                var match = SdkProjectRegex.Match(projectHead);
                return match.Success && !string.IsNullOrWhiteSpace(match.Groups["sdk"].Value);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }
    }
}
