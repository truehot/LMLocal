using System;
using System.ComponentModel.Composition;
using LMLocal.Core.Common;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace LMLocal.Infrastructure.Autocompletions.InlineCompletion
{
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [ContentType("code")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class SuggestionTextViewCreationListener : IWpfTextViewCreationListener
    {
        [Import]
        internal ICompletionBroker CompletionBroker { get; set; } = null;

        internal static readonly CompletionCache SharedCache = new CompletionCache();

        public void TextViewCreated(IWpfTextView textView)
        {
            if (textView == null)
                return;

            if (textView.Roles.Contains(PredefinedTextViewRoles.PreviewTextView))
                return;

            var tagger = textView.Properties.GetOrCreateSingletonProperty(
                typeof(SuggestionTagger),
                () => new SuggestionTagger(
                    textView,
                    SharedCache,
                    CompletionBroker));

            EventHandler<CaretPositionChangedEventArgs> caretHandler = (s, e) =>
            {
                try { tagger.ClearSuggestion(); }
                catch (Exception ex) { InternalLogger.Warn("caretHandler: " + ex.Message); }
            };

            EventHandler<TextContentChangedEventArgs> bufferHandler = (s, e) =>
            {
                try
                {
                    if (e.Changes.Count > 0)
                    {
                        tagger.ClearSuggestion();
                        tagger.ScheduleSuggestion();
                    }
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn("bufferHandler: " + ex.ToString());
                }
            };

            textView.Caret.PositionChanged += caretHandler;
            textView.TextBuffer.Changed += bufferHandler;

            textView.Closed += (s, e) =>
            {
                textView.Caret.PositionChanged -= caretHandler;
                textView.TextBuffer.Changed -= bufferHandler;

                if (textView.Properties.TryGetProperty(
                        typeof(SuggestionTagger),
                        out SuggestionTagger t))
                {
                    t.Dispose();
                    textView.Properties.RemoveProperty(typeof(SuggestionTagger));
                }

                if (textView.TextBuffer.Properties.TryGetProperty(
                        typeof(ITextDocument),
                        out ITextDocument doc)
                    && !string.IsNullOrEmpty(doc.FilePath))
                {
                    SharedCache.InvalidateFile(doc.FilePath);
                }
            };
        }
    }
}
