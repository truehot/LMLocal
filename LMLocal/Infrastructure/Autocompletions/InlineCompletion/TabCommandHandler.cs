using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;

namespace LMLocal.Infrastructure.Autocompletions.InlineCompletion
{
    /// <summary>
    /// Intercepts the Tab key. If a ghost-text suggestion is visible, accepts (inserts) it and consumes the command; otherwise passes through.
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [Name(nameof(TabCommandHandler))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class TabCommandHandler : IChainedCommandHandler<TabKeyCommandArgs>
    {
        public string DisplayName => nameof(TabCommandHandler);

        public CommandState GetCommandState(TabKeyCommandArgs args, Func<CommandState> nextCommandHandler)
        {
            return nextCommandHandler();
        }

        public void ExecuteCommand(TabKeyCommandArgs args, Action nextCommandHandler, CommandExecutionContext executionContext)
        {
            if (TryGetTagger(args.TextView, out var tagger) && tagger.HasSuggestion)
            {
                tagger.AcceptSuggestion();
                return; // consumed
            }

            nextCommandHandler();
        }

        private static bool TryGetTagger(ITextView textView, out SuggestionTagger tagger)
        {
            tagger = null;
            return textView.Properties.TryGetProperty(typeof(SuggestionTagger), out tagger);
        }
    }
}
