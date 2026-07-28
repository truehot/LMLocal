using System;
using System.ComponentModel.Design;
using EnvDTE;
using LMLocal.Application.ChatSession;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.DependencyInjection;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace LMLocal.Commands
{
    /// <summary>
    /// Command handler for "Send to LM Local" context menu item in the code editor.
    /// </summary>
    internal sealed class SendToLMFromEditorCommand
    {
        public const int CommandId = 0x0200;
        public static readonly Guid CommandSet = new Guid("c29700c4-7786-468f-bf99-0ecb9d69343f");

        private readonly AsyncPackage _package;
        private readonly ISessionManager _sessionManager;

        private SendToLMFromEditorCommand(AsyncPackage package, OleMenuCommandService commandService)
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
                new SendToLMFromEditorCommand(package, commandService);
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

            string filePath = CodeCommandHelper.GetActiveDocumentPath();
            string language = CodeCommandHelper.GetLanguageFromDocument((Package.GetGlobalService(typeof(DTE)) as DTE)?.ActiveDocument);
            string relativePath = CodeCommandHelper.GetRelativePath(filePath);
            string selectedText = CodeCommandHelper.GetSelectedText();

            string markdownText = CodeCommandHelper.BuildMarkdownContent(
                selectedText,
                filePath,
                relativePath,
                language);

            if (string.IsNullOrWhiteSpace(markdownText))
            {
                InternalLogger.Warn("SendToLMFromEditor: no content to inject — aborting.");
                return;
            }

            _ = _package.JoinableTaskFactory.RunAsync(async () =>
            {
                await CodeCommandHelper.InjectIntoChatAsync(_package, markdownText);
            });
        }
    }
}
