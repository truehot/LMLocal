using System;
using System.Collections.Generic;
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
    /// Command handler for "Send All Open Files to LM Local" in the editor document tab context menu.
    /// </summary>
    internal sealed class SendAllOpenFilesCommand
    {
        public const int CommandId = 0x020C;
        public static readonly Guid CommandSet = new Guid("c29700c4-7786-468f-bf99-0ecb9d69343f");

        private readonly AsyncPackage _package;
        private readonly ISessionManager _sessionManager;

        private SendAllOpenFilesCommand(AsyncPackage package, OleMenuCommandService commandService)
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
                new SendAllOpenFilesCommand(package, commandService);
            }
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            if (sender is OleMenuCommand menuCommand)
            {
                bool isBusy = _sessionManager?.IsSessionRunning ?? false;
                menuCommand.Visible = true;
                menuCommand.Enabled = !isBusy && HasOpenDocuments();
            }
        }

        /// <summary>
        /// True when at least one open document has a path and is not filtered out as junk.
        /// </summary>
        private static bool HasOpenDocuments()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return CollectOpenDocuments(out _).Count > 0;
        }

        /// <summary>
        /// Single source of truth for "All Open Files": every open document that has a path and is not junk.
        /// </summary>
        private static List<OpenDocumentsPromptBuilder.DocumentItem> CollectOpenDocuments(out int skippedCount)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            skippedCount = 0;
            var items = new List<OpenDocumentsPromptBuilder.DocumentItem>();

            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
                if (dte?.Documents == null)
                    return items;

                foreach (Document document in dte.Documents)
                {
                    if (document == null)
                        continue;

                    string filePath;
                    try
                    {
                        filePath = document.FullName;
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Warn($"SendAllOpenFiles: cannot read document path: {ex.Message}");
                        continue;
                    }

                    if (string.IsNullOrEmpty(filePath) || SourceFileFilter.ShouldExclude(filePath))
                    {
                        skippedCount++;
                        continue;
                    }

                    Document openDocument = document;
                    items.Add(new OpenDocumentsPromptBuilder.DocumentItem(
                        filePath,
                        () => CodeCommandHelper.ReadDocumentContent(openDocument)));
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"SendAllOpenFiles: failed to enumerate open documents: {ex.Message}");
            }

            return items;
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            List<OpenDocumentsPromptBuilder.DocumentItem> items = CollectOpenDocuments(out int skipped);

            InternalLogger.Info($"SendAllOpenFiles: {items.Count} open document(s) collected, {skipped} skipped.");

            string markdownText = OpenDocumentsPromptBuilder.Build(items);

            if (string.IsNullOrWhiteSpace(markdownText))
            {
                InternalLogger.Warn("SendAllOpenFiles: no content to inject — aborting.");
                return;
            }

            _ = _package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ChatInjectionService.Instance.InjectPromptAsync(markdownText);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error($"SendAllOpenFiles: injection failed: {ex.Message}");
                }
            });
        }
    }
}
