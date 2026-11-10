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
    /// Command handler for "All Active Files" in the "Send to LM Local" submenu of the document tab context menu. 
    /// </summary>
    internal sealed class SendActiveFilesCommand
    {
        public const int CommandId = 0x020D;
        public static readonly Guid CommandSet = new Guid("c29700c4-7786-468f-bf99-0ecb9d69343f");

        private const string LogPrefix = "SendActiveFiles";

        private readonly AsyncPackage _package;
        private readonly ISessionManager _sessionManager;

        private SendActiveFilesCommand(AsyncPackage package, OleMenuCommandService commandService)
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
                new SendActiveFilesCommand(package, commandService);
            }
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is OleMenuCommand menuCommand)
            {
                bool isBusy = _sessionManager?.IsSessionRunning ?? false;
                menuCommand.Visible = true;
                menuCommand.Enabled = !isBusy && HasVisibleDocuments();
            }
        }

        /// <summary>
        /// True when at least one document is on screen, has a path and is not filtered out as junk.
        /// </summary>
        private static bool HasVisibleDocuments()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return CollectVisibleDocuments(out _).Count > 0;
        }

        /// <summary>
        /// Single source of truth for "All Active Files": open documents whose path is currently visible on screen, that have a path and that are not filtered out as junk.
        /// </summary>
        private static List<OpenDocumentsPromptBuilder.DocumentItem> CollectVisibleDocuments(out int skippedCount)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            skippedCount = 0;
            var items = new List<OpenDocumentsPromptBuilder.DocumentItem>();

            try
            {
                var dte = Package.GetGlobalService(typeof(DTE)) as DTE;
                if (dte?.Documents == null)
                    return items;

                HashSet<string> visiblePaths = VisibleDocumentsHelper.GetVisibleDocumentPathSet();
                if (visiblePaths.Count == 0)
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
                        InternalLogger.Warn($"{LogPrefix}: cannot read document path: {ex.Message}");
                        continue;
                    }

                    if (!visiblePaths.Contains(filePath))
                        continue;

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
                InternalLogger.Warn($"{LogPrefix}: failed to enumerate visible documents: {ex.Message}");
            }

            return items;
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            List<OpenDocumentsPromptBuilder.DocumentItem> items = CollectVisibleDocuments(out int skipped);

            InternalLogger.Info($"{LogPrefix}: {items.Count} visible document(s) collected, {skipped} skipped.");

            string markdownText = OpenDocumentsPromptBuilder.Build(items);

            if (string.IsNullOrWhiteSpace(markdownText))
            {
                InternalLogger.Warn($"{LogPrefix}: no content to inject — aborting.");
                return;
            }

            _ = _package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ChatInjectionService.Instance.InjectPromptAsync(markdownText);
                    InternalLogger.Info($"{LogPrefix}: prompt injected ({markdownText.Length} chars).");
                }
                catch (Exception ex)
                {
                    InternalLogger.Error($"{LogPrefix}: injection failed: {ex.Message}");
                }
            });
        }
    }
}
