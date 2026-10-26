using System;
using System.Collections.Generic;
using System.Text;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// Builds a single Markdown prompt from several open documents.
    /// </summary>
    internal static class OpenDocumentsPromptBuilder
    {
        /// <summary>
        /// A candidate open document: its path plus a lazy reader for the editor buffer content.
        /// </summary>
        internal sealed class DocumentItem
        {
            public DocumentItem(string path, Func<string> readContent)
            {
                Path = path;
                ReadContent = readContent;
            }

            /// <summary>
            /// Full path of the document. Empty for a document that has never been saved.
            /// </summary>
            public string Path { get; private set; }

            /// <summary>
            /// Returns the document content, or null/empty when it is unavailable.
            /// </summary>
            public Func<string> ReadContent { get; private set; }
        }

        /// <summary>
        /// Builds the combined Markdown for the given documents, or null when nothing can be included.
        /// </summary>
        public static string Build(IEnumerable<DocumentItem> items)
        {
            if (items == null)
                return null;

            var parts = new List<string>();
            var includedPaths = new List<string>();
            int totalBytes = 0;

            foreach (var item in items)
            {
                if (item == null)
                    continue;

                if (string.IsNullOrEmpty(item.Path))
                {
                    InternalLogger.Warn("SendAll: skipping an open document without a file path (never saved).");
                    continue;
                }

                if (SourceFileFilter.ShouldExclude(item.Path))
                {
                    InternalLogger.Warn($"SendAll: skipping excluded file '{item.Path}'.");
                    continue;
                }

                if (includedPaths.Count >= PromptContentLimits.MaxFilesWithContent)
                {
                    parts.Add(MarkdownCodeBlockFormatter.BuildTruncatedFileFence(item.Path));
                    InternalLogger.Warn(
                        $"SendAll: '{item.Path}' exceeds the {PromptContentLimits.MaxFilesWithContent}-file limit — sent as a truncated marker.");
                    continue;
                }

                string content = ReadSafely(item);
                if (string.IsNullOrWhiteSpace(content))
                {
                    parts.Add(MarkdownCodeBlockFormatter.BuildTruncatedFileFence(item.Path));
                    InternalLogger.Warn($"SendAll: no content available for '{item.Path}' — sent as a truncated marker.");
                    continue;
                }

                int contentBytes = Encoding.UTF8.GetByteCount(content);
                if (totalBytes + contentBytes > PromptContentLimits.MaxTotalContentBytes)
                {
                    parts.Add(MarkdownCodeBlockFormatter.BuildTruncatedFileFence(item.Path));
                    InternalLogger.Warn(
                        $"SendAll: '{item.Path}' exceeds the {PromptContentLimits.MaxTotalContentBytes}-byte limit — sent as a truncated marker.");
                    continue;
                }

                string formatted = MarkdownCodeBlockFormatter.FormatFileAsMarkdown(content, item.Path);
                if (string.IsNullOrEmpty(formatted))
                {
                    parts.Add(MarkdownCodeBlockFormatter.BuildTruncatedFileFence(item.Path));
                    continue;
                }

                parts.Add(formatted);
                includedPaths.Add(item.Path);
                totalBytes += contentBytes;
            }

            if (includedPaths.Count > 0)
            {
                InternalLogger.Info(
                    $"SendAll: sending {includedPaths.Count} document(s), {totalBytes} bytes: {string.Join("; ", includedPaths)}");
            }

            return parts.Count == 0 ? null : string.Join("\n\n", parts);
        }

        private static string ReadSafely(DocumentItem item)
        {
            if (item.ReadContent == null)
                return null;

            try
            {
                return item.ReadContent();
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"SendAll: failed to read '{item.Path}': {ex.Message}");
                return null;
            }
        }
    }
}
