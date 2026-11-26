using System;
using System.ComponentModel.Design;
using System.Reflection;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.ChatSession;
using LMLocal.Core.Common;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace LMLocal.Commands
{
    /// <summary>
    /// Base class for context menu commands that inject content into LM Local chat and automatically send it with a predefined prompt instruction.
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

        protected BaseCodeCommand(
            AsyncPackage package,
            OleMenuCommandService commandService,
            ISessionManager sessionManager,
            IInstructionsManager instructionsManager)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _sessionManager = sessionManager;
            _instructionsManager = instructionsManager;

            var menuCommandID = new CommandID(CommandSet, CommandId);

            var menuItem = new OleMenuCommand(Execute, menuCommandID);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;

            commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// True while a chat session is generating; commands are disabled then.
        /// </summary>
        protected bool IsSessionRunning => _sessionManager?.IsSessionRunning ?? false;

        public static async Task InitializeAsync<T>(
            AsyncPackage package,
            ISessionManager sessionManager,
            IInstructionsManager instructionsManager)
            where T : BaseCodeCommand
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            if (await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService)
            {
                _ = (T)Activator.CreateInstance(typeof(T),
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.CreateInstance,
                    null,
                    new object[] { package, commandService, sessionManager, instructionsManager },
                    null);
            }
        }

        protected virtual void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is OleMenuCommand menuCommand)
            {
                menuCommand.Visible = true;
                menuCommand.Enabled = !IsSessionRunning;
            }
        }

        /// <summary>
        /// Builds the prompt that is injected into the chat, or null/empty when there is nothing to send.
        /// </summary>
        protected virtual string BuildPrompt()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string filePath = CodeCommandHelper.GetActiveDocumentPath();
            if (filePath == null) return null;

            string relativePath = CodeCommandHelper.GetRelativePath(filePath);
            string selectedText = CodeCommandHelper.GetSelectedText();

            string codeBlock = CodeCommandHelper.BuildMarkdownContent(
                selectedText,
                filePath,
                relativePath);

            if (string.IsNullOrWhiteSpace(codeBlock)) return null;

            return $"{PromptInstruction}\n\n{codeBlock}";
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string fullPrompt;
            try
            {
                fullPrompt = BuildPrompt();
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"Code command 0x{CommandId:X4}: failed to build the prompt", ex);
                return;
            }

            if (string.IsNullOrWhiteSpace(fullPrompt)) return;

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
                        InternalLogger.Warn(
                            $"Failed to resolve instruction '{InstructionDisplayName}'", ex);
                    }
                }
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();

                await ChatInjectionService.Instance.InjectAndAutoSendAsync(fullPrompt, instructionTabId);
            });
        }
    }
}
