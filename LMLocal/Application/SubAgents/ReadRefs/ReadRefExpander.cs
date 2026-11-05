using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;

namespace LMLocal.Application.SubAgents.ReadRefs
{
    /// <summary>
    /// Outcome of expanding ref markers in a final answer.
    /// </summary>
    internal sealed class ReadExpandResult
    {
        public string Content { get; set; }

        /// <summary>Ref markers that were covered by the read ledger and expanded into real content.</summary>
        public int ExpandedRefCount { get; set; }

        /// <summary>Ref markers that were not covered by the ledger and were left as-is.</summary>
        public int UnresolvedRefCount { get; set; }

        /// <summary>Marker-like tokens that could not be parsed (diagnostics only; not part of the public contract).</summary>
        public int MalformedRefCount { get; set; }
    }

    /// <summary>
    /// Replaces ref markers (<c>@path:lines</c>) in a SubAgent final answer with the real file content.
    /// </summary>
    internal sealed class ReadRefExpander
    {
        private static readonly Regex BulletPrefix = new Regex(@"^\s*(?:[-*+]\s+)?$", RegexOptions.CultureInvariant);
        private static readonly Regex NumberedPrefix = new Regex(@"^\s*\d+[.)]\s+$", RegexOptions.CultureInvariant);

        // A whole line that is only "[list prefix] path:line[,line...]" - a search marker without its leading '@'.
        private static readonly Regex BareSearchLocation = new Regex(
            @"^(?<prefix>[ \t]*(?:[-*+][ \t]+|\d+[.)][ \t]+)?)(?<path>[^\s""@:`*]+\.[A-Za-z0-9]+):(?<lines>\d+(?:,\d+)*)(?=[ \t]*\r?$)",
            RegexOptions.CultureInvariant | RegexOptions.Multiline);

        private readonly ReadLedger _ledger;
        private readonly SearchLedger _searchLedger;
        private readonly IFileSystem _fileSystem;

        public ReadRefExpander(ReadLedger ledger, IFileSystem fileSystem, SearchLedger searchLedger = null)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _searchLedger = searchLedger;
        }

        public async Task<ReadExpandResult> ExpandAsync(string content, CancellationToken cancellationToken)
        {
            var result = new ReadExpandResult { Content = content };
            if (string.IsNullOrEmpty(content))
                return result;

            content = AddMissingSearchMarkerPrefixes(content);

            var markers = FileRefMarker.ParseAll(content, out int malformedRefCount);
            result.MalformedRefCount = malformedRefCount;
            if (result.MalformedRefCount > 0)
                InternalLogger.Debug($"ReadRefExpander: {result.MalformedRefCount} malformed ref-like token(s) in the final answer.");

            if (markers.Count == 0)
                return result;

            var sb = new StringBuilder(content.Length + 256);
            int cursor = 0;

            foreach (var marker in markers)
            {
                sb.Append(content, cursor, marker.StartIndex - cursor);

                string rendered = null;
                try
                {
                    rendered = await TryRenderAsync(marker, cancellationToken).ConfigureAwait(false)
                        ?? TryRenderSearch(marker, content);
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"ReadRefExpander: failed to expand ref '{content.Substring(marker.StartIndex, marker.Length)}': {ex.Message}");
                    rendered = null;
                }

                if (rendered != null)
                {
                    sb.Append(rendered);
                    result.ExpandedRefCount++;
                }
                else
                {
                    result.UnresolvedRefCount++;
                    InternalLogger.Warn($"ReadRefExpander: unresolved ref '{content.Substring(marker.StartIndex, marker.Length)}'");
                    sb.Append(content, marker.StartIndex, marker.Length);
                }

                cursor = marker.StartIndex + marker.Length;
            }

            sb.Append(content, cursor, content.Length - cursor);
            result.Content = sb.ToString();
            return result;
        }

        private async Task<string> TryRenderAsync(FileRefMarker marker, CancellationToken cancellationToken)
        {
            string absolutePath = null;
            string headerPath = null;
            for (int i = 0; i < marker.Ranges.Count; i++)
            {
                if (!_ledger.TryResolve(marker.Path, marker.Ranges[i], out var record) || string.IsNullOrEmpty(record.AbsolutePath))
                    return null;

                if (absolutePath == null)
                    absolutePath = record.AbsolutePath;
                if (headerPath == null)
                    headerPath = string.IsNullOrEmpty(record.RelativePath) ? marker.Path : record.RelativePath;
            }

            if (absolutePath == null)
                return null;

            var sb = new StringBuilder();
            for (int i = 0; i < marker.Ranges.Count; i++)
            {
                var range = marker.Ranges[i];
                var rawLines = await _fileSystem
                    .ReadLinesRangeAsync(absolutePath, range.Start, range.End, cancellationToken)
                    .ConfigureAwait(false);

                var numbered = new List<NumberedLine>(rawLines?.Count ?? 0);
                if (rawLines != null)
                {
                    for (int n = 0; n < rawLines.Count; n++)
                        numbered.Add(new NumberedLine(range.Start + n, rawLines[n] ?? string.Empty));
                }

                if (i > 0)
                    sb.Append('\n');

                FileContentRenderer.AppendRefContentBlock(sb, headerPath, range.Start, range.End, numbered);
            }

            return sb.ToString().TrimEnd('\r', '\n');
        }

        /// <summary>
        /// Renders a marker whose every line was returned by search_file_content as location lines with the exact matched text.
        /// </summary>
        private string TryRenderSearch(FileRefMarker marker, string content)
        {
            if (_searchLedger == null || _searchLedger.Count == 0)
                return null;

            var lines = new List<SearchRecord>();
            for (int i = 0; i < marker.Ranges.Count; i++)
            {
                if (!_searchLedger.TryResolve(marker.Path, marker.Ranges[i], out var records))
                    return null;
                lines.AddRange(records);
            }

            if (lines.Count == 0)
                return null;

            string separator = BuildSeparator(content, marker.StartIndex);
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                    sb.Append(separator);
                FileContentRenderer.AppendSearchLocation(sb, lines[i].RelativePath, lines[i].LineNumber, lines[i].Text);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Models sometimes drop the leading <c>@</c> and write a bare <c>path:line</c> list item.
        /// </summary>
        private string AddMissingSearchMarkerPrefixes(string content)
        {
            if (_searchLedger == null || _searchLedger.Count == 0)
                return content;

            return BareSearchLocation.Replace(content, m =>
            {
                string path = m.Groups["path"].Value;
                foreach (var part in m.Groups["lines"].Value.Split(','))
                {
                    if (!int.TryParse(part, out int line) || !_searchLedger.TryResolve(path, new LineRange(line, line), out _))
                        return m.Value;
                }

                return m.Groups["prefix"].Value + "@" + path + ":" + m.Groups["lines"].Value;
            });
        }

        private static string BuildSeparator(string content, int markerStart)
        {
            int lineStart = markerStart > 0 ? content.LastIndexOf('\n', markerStart - 1) + 1 : 0;
            string before = content.Substring(lineStart, markerStart - lineStart);

            if (BulletPrefix.IsMatch(before))
                return "\n" + before;
            if (NumberedPrefix.IsMatch(before))
                return "\n" + new string(' ', before.Length);
            return "; ";
        }
    }
}
