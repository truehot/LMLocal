using System;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using LMLocal.Infrastructure.Persistence;
using Microsoft.VisualStudio.Shell;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Verifies that a Visual Studio document's in-memory buffer is in sync with the file on disk before a tool modifies and saves the document. 
    /// </summary>
    internal static class DocumentSyncGuard
    {
        /// <summary>
        /// Returns an error message if the document must not be modified/saved, or null
        /// when it is safe to proceed. Must be called on the VS UI thread.
        /// </summary>
        public static async Task<string> GetSyncErrorAsync(
            Document document,
            string absolutePath,
            IFileSystem fileSystem,
            CancellationToken cancellationToken = default)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            if (document == null)
                return "Failed to obtain document reference.";

            try
            {
                if (!document.Saved)
                    return "The document has unsaved changes in the editor. Save or discard them before running this tool.";
            }
            catch (Exception)
            {
                return "Failed to determine whether the document has unsaved changes.";
            }

            string bufferText;
            try
            {
                if (!(document.Object("TextDocument") is TextDocument textDocument))
                    return "The document is not a text document and cannot be safely checked for synchronization.";

                var editPoint = textDocument.StartPoint.CreateEditPoint();
                bufferText = editPoint.GetText(textDocument.EndPoint);
            }
            catch (Exception)
            {
                return "Failed to read the editor buffer to verify synchronization.";
            }

            string diskText;
            try
            {
                diskText = await fileSystem.ReadAllTextWithSharedReadAsync(absolutePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return "Failed to read the file from disk to verify synchronization.";
            }

            if (bufferText.Length == 0 && diskText.Length == 0)
            {
                try
                {
                    var (length, _) = fileSystem.GetFileInfo(absolutePath);
                    if (length > 0)
                        return "Failed to read the file from disk to verify synchronization.";
                }
                catch (Exception)
                {
                    return "Failed to access the file on disk to verify synchronization.";
                }
            }

            if (!string.Equals(NormalizeForComparison(bufferText), NormalizeForComparison(diskText), StringComparison.Ordinal))
                return "The file on disk differs from the editor buffer. It may have been modified outside Visual Studio (for example by Git or another editor). Reload the document before running this tool.";

            return null;
        }

        private static string NormalizeForComparison(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            if (value[0] == '\uFEFF')
                value = value.Substring(1);

            return value.Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }
}
