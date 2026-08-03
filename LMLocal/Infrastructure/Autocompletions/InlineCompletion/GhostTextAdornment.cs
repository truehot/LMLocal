using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace LMLocal.Infrastructure.Autocompletions.InlineCompletion
{
    /// <summary>
    /// Renders one line of ghost text right after the caret.
    /// </summary>
    internal sealed class GhostTextAdornment
    {
        private static readonly Brush LightBrush = new SolidColorBrush(Color.FromRgb(170, 170, 170));
        private static readonly Brush DarkBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90));
        private static readonly Brush FallbackBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128));

        private readonly IWpfTextView _view;
        private readonly IAdornmentLayer _layer;
        private TextBlock _textBlock;

        public GhostTextAdornment(IWpfTextView view)
        {
            _view = view;
            _layer = view.GetAdornmentLayer("GhostTextLayer");
        }

        public void Show(string text, SnapshotPoint caretPoint)
        {
            if (string.IsNullOrEmpty(text))
            {
                Hide();
                return;
            }

            RemoveCurrent();

            var line = _view.GetTextViewLineContainingBufferPosition(caretPoint);
            if (line == null) return;

            if (caretPoint.Position < line.End.Position)
            {
                string textAfter = caretPoint.Snapshot.GetText(caretPoint.Position, line.End.Position - caretPoint.Position);
                if (!string.IsNullOrWhiteSpace(textAfter))
                {
                    Hide();
                    return;
                }
            }

            if (_textBlock == null)
            {
                var props = _view.FormattedLineSource?.DefaultTextProperties;
                if (props == null) return;

                _textBlock = new TextBlock
                {
                    IsHitTestVisible = false,
                    Opacity = 0.65,
                    FontFamily = props.Typeface.FontFamily,
                    FontSize = props.FontRenderingEmSize,
                    FontStyle = FontStyles.Normal,
                    FontWeight = FontWeights.Normal,
                    Foreground = GetGhostBrush()
                };
            }

            _textBlock.Text = text;
            _textBlock.Visibility = Visibility.Visible;

            var charBounds = line.GetCharacterBounds(caretPoint);

            Canvas.SetLeft(_textBlock, charBounds.Left);
            Canvas.SetTop(_textBlock, charBounds.TextTop);


            _layer.AddAdornment(
                AdornmentPositioningBehavior.TextRelative,
                new SnapshotSpan(caretPoint, 0),
                null,
                _textBlock,
                null);
        }

        /// <summary>
        /// Collapses the adornment without removing it from the layer.
        /// </summary>
        public void Hide()
        {
            if (_textBlock != null)
                _textBlock.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Fully removes the adornment from the layer. Use in Dispose paths.
        /// </summary>
        public void Remove()
        {
            if (_textBlock != null)
            {
                _layer.RemoveAdornment(_textBlock);
                _textBlock = null;
            }
        }

        private void RemoveCurrent()
        {
            if (_textBlock != null)
            {
                _layer.RemoveAdornment(_textBlock);
            }
        }

        private Brush GetGhostBrush()
        {
            var bg = _view.Background;
            if (bg is SolidColorBrush solidBg)
            {
                var c = solidBg.Color;
                double brightness = c.R * 0.2126 + c.G * 0.7152 + c.B * 0.0722;
                return brightness < 128 ? LightBrush : DarkBrush;
            }
            return FallbackBrush;
        }
    }
}
