using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace LMLocal.Infrastructure.Autocompletions.InlineCompletion
{
    /// <summary>
    /// Provides the <see cref="GhostLinesTagger"/> for a view. InterLineAdornmentTag may only come from
    /// an IViewTaggerProvider (SDK docs), and the instance is the per-view singleton the
    /// <see cref="SuggestionTagger"/> drives.
    /// </summary>
    [Export(typeof(IViewTaggerProvider))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [TagType(typeof(InterLineAdornmentTag))]
    internal sealed class GhostLinesTaggerProvider : IViewTaggerProvider
    {
        public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
        {
            if (textView == null || textView.TextBuffer != buffer)
                return null;

            return GhostLinesTagger.GetOrCreate(textView) as ITagger<T>;
        }
    }

    /// <summary>
    /// Lines 2..N of a multiline suggestion. 
    /// </summary>
    internal sealed class GhostLinesTagger : ITagger<InterLineAdornmentTag>
    {
        private readonly ITextView _view;
        private ITrackingPoint _anchor;          // start of the caret line
        private InterLineAdornmentTag _tag;

        private GhostLinesTagger(ITextView view)
        {
            _view = view;
        }

        internal static GhostLinesTagger GetOrCreate(ITextView view)
        {
            return view.Properties.GetOrCreateSingletonProperty(() => new GhostLinesTagger(view));
        }

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        internal bool IsShown => _tag != null;

        public IEnumerable<ITagSpan<InterLineAdornmentTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            var tag = _tag;
            var anchor = _anchor;
            if (tag == null || anchor == null || spans.Count == 0)
                yield break;

            var snapshot = spans[0].Snapshot;
            if (anchor.TextBuffer != snapshot.TextBuffer)
                yield break;

            var p = anchor.GetPoint(snapshot);
            if (spans.Any(s => s.Contains(p) || s.End == p))
                yield return new TagSpan<InterLineAdornmentTag>(new SnapshotSpan(p, 0), tag);
        }

        /// <summary>
        /// Shows <paramref name="element"/> (lines 2..N, <paramref name="lineCount"/> lines) under the
        /// line containing <paramref name="caretPoint"/>, replacing any block shown before.
        /// </summary>
        internal void Show(SnapshotPoint caretPoint, UIElement element, int lineCount, double lineHeight)
        {
            var oldLine = CurrentLineExtent();
            var line = caretPoint.GetContainingLine();

            _anchor = caretPoint.Snapshot.CreateTrackingPoint(line.Start, PointTrackingMode.Negative);
            _tag = new InterLineAdornmentTag(
                adornmentFactory: (tag, view, position) => element,
                isAboveLine: false,
                initialHeight: Math.Max(1, lineCount) * lineHeight,
                horizontalPositioningMode: HorizontalPositioningMode.TextRelative,   // x of the line's first character
                horizontalOffset: 0,
                removalCallback: null);

            Raise(oldLine);
            Raise(line.ExtentIncludingLineBreak);
        }

        internal void Hide()
        {
            if (_tag == null)
                return;

            var oldLine = CurrentLineExtent();
            _tag = null;
            _anchor = null;
            Raise(oldLine);
        }

        private SnapshotSpan? CurrentLineExtent()
        {
            var anchor = _anchor;
            if (anchor == null || anchor.TextBuffer != _view.TextBuffer)
                return null;

            return anchor.GetPoint(_view.TextSnapshot).GetContainingLine().ExtentIncludingLineBreak;
        }

        private void Raise(SnapshotSpan? span)
        {
            if (span.HasValue)
                TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(span.Value));
        }
    }
}
