using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace LMLocal.Infrastructure.VisualStudio
{
    /// <summary>
    /// Provides methods to open files in the Visual Studio editor.
    /// </summary>
    public static class FileViewer
    {
        private static readonly Dictionary<string, IVsWindowFrame> _openFiles = new Dictionary<string, IVsWindowFrame>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Opens a file in the Visual Studio editor. If the file is already open, activates the existing window.
        /// </summary>

        public static async Task OpenFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (_openFiles.TryGetValue(filePath, out var existingFrame) && IsFrameAlive(existingFrame))
            {
                int hrs = existingFrame.Show();
                if (ErrorHandler.Succeeded(hrs))
                    return;

                _openFiles.Remove(filePath);
            }

            var openDoc = (IVsUIShellOpenDocument)Package.GetGlobalService(typeof(SVsUIShellOpenDocument)) ?? throw new InvalidOperationException("IVsUIShellOpenDocument not available");

            Guid logicalView = Guid.Empty;
            int hr = openDoc.OpenDocumentViaProject(
                filePath,
                ref logicalView,
                out _,
                out _,
                out _,
                out IVsWindowFrame frame);

            if (hr == 0 && frame != null)
            {
                frame.Show();
                _openFiles[filePath] = frame;
            }
        }

        /// <summary>
        /// Checks whether the given IVsWindowFrame is still valid (the window is still open).
        /// </summary>
        private static bool IsFrameAlive(IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                frame.GetProperty((int)__VSFPROPID.VSFPROPID_Caption, out _);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Closes all files opened through this FileViewer. Useful for cleanup when the extension is unloaded.
        /// </summary>
        public static async Task CloseAllAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            foreach (var kvp in _openFiles)
            {
                if (IsFrameAlive(kvp.Value))
                {
                    try
                    {
                        kvp.Value.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave);
                    }
                    catch
                    {
                        InternalLogger.Info($"Failed to close frame for {kvp.Key}");
                    }
                }
            }
            _openFiles.Clear();
        }
    }
}
