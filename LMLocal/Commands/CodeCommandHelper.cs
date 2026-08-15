using System;
using System.Threading.Tasks;
using EnvDTE;
using LMLocal.Core.Common;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace LMLocal.Commands
{
    /// <summary>
    /// Shared helper for context menu code commands.
    /// </summary>
    internal static class CodeCommandHelper
    {
        /// <summary>
        /// Gets the selected text from the active editor view.
        /// </summary>
        public static string GetSelectedText()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (Package.GetGlobalService(typeof(SVsTextManager)) is IVsTextManager textManager)
            {
                textManager.GetActiveView(1, null, out IVsTextView activeView);
                if (activeView != null)
                {
                    activeView.GetSelectedText(out string selText);
                    if (!string.IsNullOrWhiteSpace(selText))
                        return selText;
                }
            }

            // Fallback
            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
                if (dte?.ActiveDocument?.Selection != null)
                {
                    var textSelection = (TextSelection)dte.ActiveDocument.Selection;
                    if (textSelection != null && !string.IsNullOrWhiteSpace(textSelection.Text))
                        return textSelection.Text;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"CodeCommandHelper.GetSelectedText (DTE fallback) failed: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Reads the full content of the active document.
        /// </summary>
        public static string ReadFullDocumentContent()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
                if (dte?.ActiveDocument != null)
                {
                    var textDoc = (TextDocument)dte.ActiveDocument.Object("TextDocument");
                    if (textDoc != null)
                    {
                        var editPoint = textDoc.StartPoint.CreateEditPoint();
                        return editPoint.GetText(textDoc.EndPoint);
                    }
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"CodeCommandHelper.ReadFullDocumentContent failed: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Gets the file path of the active document.
        /// </summary>
        public static string GetActiveDocumentPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
            return dte?.ActiveDocument?.FullName;
        }

        /// <summary>
        /// Gets a relative path from the solution directory.
        /// </summary>
        public static string GetRelativePath(string fullPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (string.IsNullOrEmpty(fullPath)) return null;

            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
                string solutionFile = dte?.Solution?.FullName;
                if (!string.IsNullOrEmpty(solutionFile))
                {
                    string solutionDir = System.IO.Path.GetDirectoryName(solutionFile);
                    if (!string.IsNullOrEmpty(solutionDir) && fullPath.StartsWith(solutionDir, StringComparison.OrdinalIgnoreCase))
                    {
                        return fullPath.Substring(solutionDir.Length)
                            .TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                    }
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"CodeCommandHelper.GetRelativePath failed for '{fullPath}': {ex.Message}");
            }
            return fullPath;
        }

        /// <summary>
        /// Builds markdown content from selected text or full document, using the shared file formatter.
        /// </summary>
        public static string BuildMarkdownContent(string selectedText, string filePath, string relativePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string content = !string.IsNullOrWhiteSpace(selectedText) ? selectedText : ReadFullDocumentContent();
            if (string.IsNullOrWhiteSpace(content))
                return null;

            return MarkdownCodeBlockFormatter.FormatFileAsMarkdown(content, filePath, relativePath);
        }

        /// <summary>
        /// Finds or creates the MainWindow tool window and shows it.
        /// </summary>
        public static async Task<MainWindow> FindAndShowMainWindowAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            ToolWindowPane window = package.FindToolWindow(typeof(MainWindow), 0, false)
                                ?? package.FindToolWindow(typeof(MainWindow), 0, true);

            if (window?.Frame is IVsWindowFrame frame)
                _ = frame.Show();

            return window as MainWindow;
        }

        /// <summary>
        /// Finds (or creates) the MainWindow, shows it, and injects markdown text.
        /// </summary>
        public static async Task InjectIntoChatAsync(
            AsyncPackage package,
            string markdownText,
            bool autoSend = false,
            string instructionTabId = null)
        {
            var mainWindow = await FindAndShowMainWindowAsync(package);
            if (mainWindow == null)
                return;

            if (autoSend)
            {
                await mainWindow.InjectAndAutoSendAsync(markdownText + "\n\n", instructionTabId);
            }
            else
            {
                await mainWindow.InjectPromptAsync(markdownText + "\n\n");
            }
        }
    }
}
