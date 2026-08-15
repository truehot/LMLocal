using System;
using System.ComponentModel.Design;
using System.Reflection;
using EnvDTE;
using LMLocal.Application.ChatSession;
using LMLocal.Infrastructure.DependencyInjection;
using LMLocal.Infrastructure.Instructions;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace LMLocal.Commands
{
    /// <summary>
    /// Base class for context menu commands that inject code into LM Local chat and automatically send it with a predefined prompt instruction.
    /// </summary>
    internal abstract class BaseCodeCommand
    {
        public static readonly Guid CommandSet = new Guid("c29700c4-7786-468f-bf99-0ecb9d69343f");
        protected abstract int CommandId { get; }
        protected abstract string PromptInstruction { get; }

        /// <summary>
        /// Optional: the display name of the instruction tab to select from the dropdown before sending.
        /// </summary>
        protected virtual string InstructionDisplayName => null;

        private readonly AsyncPackage _package;
        private readonly ISessionManager _sessionManager;
        private readonly IInstructionsManager _instructionsManager;

        protected BaseCodeCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _sessionManager = ServiceConfiguration.GetService<ISessionManager>();
            _instructionsManager = ServiceConfiguration.GetService<IInstructionsManager>();

            var menuCommandID = new CommandID(CommandSet, CommandId);

            var menuItem = new OleMenuCommand(Execute, menuCommandID);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;

            commandService.AddCommand(menuItem);
        }

        public static async Task InitializeAsync<T>(AsyncPackage package)
            where T : BaseCodeCommand
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            if (await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService)
            {
                _ = (T)Activator.CreateInstance(typeof(T),
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.CreateInstance,
                    null,
                    new object[] { package, commandService },
                    null);
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
            if (filePath == null) return;

            string relativePath = CodeCommandHelper.GetRelativePath(filePath);
            string selectedText = CodeCommandHelper.GetSelectedText();

            string codeBlock = CodeCommandHelper.BuildMarkdownContent(
                selectedText,
                filePath,
                relativePath);

            if (string.IsNullOrWhiteSpace(codeBlock)) return;

            string fullPrompt = $"{PromptInstruction}\n\n{codeBlock}";

            _ = _package.JoinableTaskFactory.RunAsync(async () =>
            {
                string instructionTabId = null;
                if (!string.IsNullOrWhiteSpace(InstructionDisplayName))
                {
                    try
                    {
                        instructionTabId = await _instructionsManager
                            .GetInstructionTabIdByDisplayNameAsync(InstructionDisplayName)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Core.Common.InternalLogger.Warn(
                            $"Failed to resolve instruction '{InstructionDisplayName}': {ex.Message}");
                    }
                }

                await CodeCommandHelper.InjectIntoChatAsync(
                    _package,
                    fullPrompt,
                    autoSend: true,
                    instructionTabId: instructionTabId);
            });
        }
    }
}
