using System;
using System.ComponentModel.Design;
using EnvDTE;
using LMLocal.Application.ChatSession;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.DependencyInjection;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using Task = System.Threading.Tasks.Task;

namespace LMLocal
{
    /// <summary>
    /// Command handler for "Ask LM Local" context menu item in the code editor.
    /// </summary>
    internal sealed class AskLMLocalCommand
    {
        public const int CommandId = 0x0200;
        public static readonly Guid CommandSet = new Guid("c29700c4-7786-468f-bf99-0ecb9d69343f");

        private readonly AsyncPackage _package;
        private readonly ISessionManager _sessionManager;

        private AskLMLocalCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _sessionManager = ServiceConfiguration.GetService<ISessionManager>();

            var menuCommandID = new CommandID(CommandSet, CommandId);

            var menuItem = new OleMenuCommand(Execute, menuCommandID);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;

            commandService.AddCommand(menuItem);
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            if (await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService)
            {
                new AskLMLocalCommand(package, commandService);
            }
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            if (sender is OleMenuCommand menuCommand)
            {
                bool isBusy = _sessionManager?.IsSessionRunning ?? false;
                menuCommand.Visible = true;
                menuCommand.Enabled = !isBusy;
            }
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var textManager = Package.GetGlobalService(typeof(SVsTextManager)) as IVsTextManager;
            if (textManager == null) return;

            textManager.GetActiveView(1, null, out IVsTextView activeView);

            var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
            string filePath = dte?.ActiveDocument?.FullName;
            string language = GetLanguageFromDocument(dte?.ActiveDocument);
            string relativePath = GetRelativePath(filePath);

            string selectedText = null;
            if (activeView != null)
            {
                activeView.GetSelectedText(out string selText);
                if (!string.IsNullOrWhiteSpace(selText))
                {
                    selectedText = selText;
                }
            }

            if (string.IsNullOrWhiteSpace(selectedText) && dte?.ActiveDocument?.Selection != null)
            {
                try
                {
                    var textSelection = (TextSelection)dte.ActiveDocument.Selection;
                    if (textSelection != null && !string.IsNullOrWhiteSpace(textSelection.Text))
                    {
                        selectedText = textSelection.Text;
                    }
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"AskLMLocal: DTE selection fallback failed: {ex.Message}");
                }
            }

            string markdownText = BuildMarkdownContent(selectedText, filePath, relativePath, language);

            if (string.IsNullOrWhiteSpace(markdownText))
            {
                InternalLogger.Warn("AskLMLocal: no content to inject — aborting.");
                return;
            }

            _ = _package.JoinableTaskFactory.RunAsync(async () =>
            {
                await ShowAndInjectAsync(markdownText);
            });
        }

        private string BuildMarkdownContent(string selectedText, string filePath, string relativePath, string language)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string commentPath = !string.IsNullOrEmpty(relativePath)
                ? $"// file: {relativePath}"
                : !string.IsNullOrEmpty(filePath) ? $"// file: {filePath}" : null;

            if (!string.IsNullOrWhiteSpace(selectedText))
            {
                return FormatCodeBlock(selectedText, language, commentPath);
            }

            string fullContent = ReadFullDocumentContent();
            if (!string.IsNullOrWhiteSpace(fullContent))
            {
                return FormatCodeBlock(fullContent, language, commentPath);
            }

            return null;
        }

        private static string FormatCodeBlock(string code, string language, string commentPath)
        {
            string langTag = !string.IsNullOrEmpty(language) ? language : "";
            string result = $"```{langTag}\n";

            if (!string.IsNullOrEmpty(commentPath))
            {
                result += $"{commentPath}\n";
            }

            result += $"{code}\n```";
            return result;
        }

        private string ReadFullDocumentContent()
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
                InternalLogger.Warn($"AskLMLocal: ReadFullDocumentContent failed: {ex.Message}");
            }
            return null;
        }

        private string GetRelativePath(string fullPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (string.IsNullOrEmpty(fullPath))
                return null;

            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
                string solutionFile = dte?.Solution?.FullName;
                if (!string.IsNullOrEmpty(solutionFile))
                {
                    string solutionDir = System.IO.Path.GetDirectoryName(solutionFile);
                    if (!string.IsNullOrEmpty(solutionDir) && fullPath.StartsWith(solutionDir, StringComparison.OrdinalIgnoreCase))
                    {
                        string relative = fullPath.Substring(solutionDir.Length)
                            .TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                        return relative;
                    }
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"AskLMLocal: GetRelativePath failed for '{fullPath}': {ex.Message}");
            }
            return fullPath;
        }

        private string GetLanguageFromDocument(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (document == null || string.IsNullOrEmpty(document.FullName))
                return null;

            string lang = MarkdownLanguageHelper.GetLanguageFromExtension(document.FullName);
            return string.IsNullOrEmpty(lang) ? null : lang;
        }

        private async Task ShowAndInjectAsync(string markdownText)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            ToolWindowPane window = _package.FindToolWindow(typeof(MainWindow), 0, false);
            if (window == null)
            {
                window = _package.FindToolWindow(typeof(MainWindow), 0, true);
            }

            if (window?.Frame is IVsWindowFrame frame)
            {
                ErrorHandler.ThrowOnFailure(frame.Show());
            }

            if (window is MainWindow mainWindow)
            {
                await mainWindow.InjectPromptAsync(markdownText + "\n\n");
            }
        }
    }
}
