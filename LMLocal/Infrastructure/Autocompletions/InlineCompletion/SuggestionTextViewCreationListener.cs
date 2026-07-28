using System;
using System.ComponentModel.Composition;
using LMLocal.Core.Common;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
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
                () => new SuggestionTagger(textView, SharedCache, CompletionBroker));

            void OnTextBufferChanged(object s, TextContentChangedEventArgs e)
            {
                try
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    tagger.HandleTextChanged(e);
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn("bufferHandler: " + ex.ToString());
                }
            }

            textView.TextBuffer.Changed += OnTextBufferChanged;

            textView.Closed += (s, e) =>
            {
                textView.TextBuffer.Changed -= OnTextBufferChanged;

                if (textView.Properties.TryGetProperty(typeof(SuggestionTagger), out SuggestionTagger t))
                {
                    t.Dispose();
                    textView.Properties.RemoveProperty(typeof(SuggestionTagger));
                }

                if (textView.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument doc) && !string.IsNullOrEmpty(doc.FilePath))
                {
                    SharedCache.InvalidateFile(doc.FilePath);
                }
            };
        }
    }
}
