using System;
using System.ComponentModel.Composition;
using LMLocal.Core.Common;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Suggestions;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
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

        [Import]
        internal IEditorOperationsFactoryService EditorOperationsFactory { get; set; } = null;

        // VS inline suggestions (Copilot, IntelliCode). Optional: absent on VS builds without the service.
        [Import(AllowDefault = true)]
        internal SuggestionServiceBase SuggestionService { get; set; } = null;

        // Modern (async) IntelliSense completion list, used by C# and most languages in VS 2022.
        [Import(AllowDefault = true)]
        internal IAsyncCompletionBroker AsyncCompletionBroker { get; set; } = null;

        internal static readonly CompletionCache SharedCache = new CompletionCache();

        public void TextViewCreated(IWpfTextView textView)
        {
            if (textView == null)
                return;

            if (textView.Roles.Contains(PredefinedTextViewRoles.PreviewTextView) || textView.Roles.Contains("COMMANDVIEW"))
                return;

            if (!textView.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document))
                return;

            string path = document.FilePath;
            if (string.IsNullOrEmpty(path))
                return;


            var editorOperations = EditorOperationsFactory?.GetEditorOperations(textView);
            var tagger = textView.Properties.GetOrCreateSingletonProperty(
                typeof(SuggestionTagger),
                () => new SuggestionTagger(textView, SharedCache, CompletionBroker, editorOperations, SuggestionService, AsyncCompletionBroker));

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

            void OnCaretPositionChanged(object s, CaretPositionChangedEventArgs e)
            {
                try
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    tagger.HandleCaretMoved(e);
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn("caretHandler: " + ex.ToString());
                }
            }

            void OnLayoutChanged(object s, TextViewLayoutChangedEventArgs e)
            {
                try
                {
                    tagger.HandleLayoutChanged(e);
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn("layoutHandler: " + ex.ToString());
                }
            }

            textView.TextBuffer.Changed += OnTextBufferChanged;
            textView.Caret.PositionChanged += OnCaretPositionChanged;
            textView.LayoutChanged += OnLayoutChanged;

            textView.Closed += (s, e) =>
            {
                textView.TextBuffer.Changed -= OnTextBufferChanged;
                textView.Caret.PositionChanged -= OnCaretPositionChanged;
                textView.LayoutChanged -= OnLayoutChanged;

                if (textView.Properties.TryGetProperty(typeof(SuggestionTagger), out SuggestionTagger t))
                {
                    t.Dispose();
                    textView.Properties.RemoveProperty(typeof(SuggestionTagger));
                }
            };
        }
    }
}

