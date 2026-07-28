using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;

namespace LMLocal.Infrastructure.Autocompletions.InlineCompletion
{
    /// <summary>
    /// Intercepts the Escape key. If a ghost-text suggestion is visible,clears it and consumes the command; otherwise passes through.
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [Name(nameof(EscapeCommandHandler))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class EscapeCommandHandler : IChainedCommandHandler<EscapeKeyCommandArgs>
    {
        public string DisplayName => nameof(EscapeCommandHandler);

        public CommandState GetCommandState(EscapeKeyCommandArgs args, Func<CommandState> nextCommandHandler)
        {
            return nextCommandHandler();
        }

        public void ExecuteCommand(EscapeKeyCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
        {
            if (SuggestionTagger.TryGet(args.TextView, out var tagger) && tagger.HasSuggestion)
            {
                tagger.ClearSuggestion();
                return; // consumed
            }

            nextCommandHandler();
        }
    }
}
