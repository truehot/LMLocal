using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Autocompletions;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.DependencyInjection;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;



namespace LMLocal.Infrastructure.Autocompletions.InlineCompletion
{
    /// <summary>
    /// Provides inline ghost-text suggestions using IAdornmentLayer for pixel-perfect positioning relative to text view lines.
    /// </summary>
    internal class SuggestionTagger : IDisposable
    {
        internal const int MaxSuggestionLines = 1;

        private const int DebounceDelayMs = 300;
        private const int MaxPrefixLength = 800;
        private const int MaxSuffixLength = 50;
        private const int DefaultMaxTokens = 80;

        private readonly ITextView _textView;
        private readonly IWpfTextView _wpfTextView;
        private readonly CompletionCache _cache;
        private readonly ICompletionBroker _completionBroker;

        private SuggestionState _state;

        private Timer _debounceTimer;
        private volatile CancellationTokenSource _operationCts;

        private readonly List<GhostTextAdornment> _adornments = new List<GhostTextAdornment>();

        private bool _suppressNextSuggestion;
        private bool _disposed;

        internal SuggestionTagger(
            ITextView textView,
            CompletionCache cache,
            ICompletionBroker completionBroker)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            _wpfTextView = textView as IWpfTextView;
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _completionBroker = completionBroker;
        }

        internal bool HasSuggestion
        {
            get
            {
                var s = _state;
                return s != null && s.HasValue;
            }
        }

        internal void ScheduleSuggestion()
        {
            if (_disposed || _suppressNextSuggestion) return;

            if (_debounceTimer == null)
            {
                _debounceTimer = new Timer(OnDebounceElapsed, null, DebounceDelayMs, Timeout.Infinite);
            }
            else
            {
                _debounceTimer.Change(DebounceDelayMs, Timeout.Infinite);
            }
        }

        internal virtual void ClearSuggestion()
        {
            CancelDebounce();
            CancelOperation();

            var oldState = _state;
            if (oldState == null || !oldState.HasValue) return;

            _state = null;

            foreach (var ad in _adornments)
            {
                ad.Hide();
            }
        }

        internal virtual void AcceptSuggestion()
        {
            var state = _state;
            if (_disposed || state == null || !state.HasValue)
                return;

            var suggestion = state.Text;
            var caret = state.CaretPoint;
            var snapshot = caret.Snapshot;

            var currentCaret = _textView.Caret.Position.BufferPosition;
            if (currentCaret.Snapshot != snapshot || currentCaret.Position != caret.Position)
                return;

            _suppressNextSuggestion = true;
            try
            {
                CancelDebounce();
                _state = null;

                foreach (var ad in _adornments)
                    ad.Hide();

                var edit = _textView.TextBuffer.CreateEdit();
                edit.Insert(currentCaret.Position, suggestion);
                edit.Apply();
            }
            finally
            {
                _suppressNextSuggestion = false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            CancelDebounce();
            CancelOperation();

            _debounceTimer?.Dispose();
            _debounceTimer = null;

            _state = null;

            foreach (var ad in _adornments)
                ad.Remove();

            _adornments.Clear();
        }

        private void CancelDebounce()
        {
            _debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void CancelOperation()
        {
            var cts = Interlocked.Exchange(ref _operationCts, null);
            if (cts != null)
            {
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { }
                cts.Dispose();
            }
        }

        private void OnDebounceElapsed(object state)
        {
            try
            {
                _ = GenerateSuggestionAsync();
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("OnDebounceElapsed: " + ex.Message);
            }
        }

        private async Task GenerateSuggestionAsync()
        {
            try
            {
                var ctSource = new CancellationTokenSource();
                var old = Interlocked.Exchange(ref _operationCts, ctSource);
                if (old != null)
                {
                    try { old.Cancel(); } catch (ObjectDisposedException) { }
                    old.Dispose();
                }

                var ct = ctSource.Token;
                ct.ThrowIfCancellationRequested();

                var snapshot = _textView.TextSnapshot;
                int caretPos = _textView.Caret.Position.BufferPosition.Position;

                if (caretPos > snapshot.Length)
                    caretPos = snapshot.Length;

                int prefixStart = Math.Max(0, caretPos - MaxPrefixLength);
                int prefixLen = caretPos - prefixStart;
                string prefix = prefixLen > 0
                    ? snapshot.GetText(prefixStart, prefixLen)
                    : string.Empty;

                int suffixLen = Math.Min(MaxSuffixLength, snapshot.Length - caretPos);
                string suffix = suffixLen > 0
                    ? snapshot.GetText(caretPos, suffixLen)
                    : string.Empty;

                var filePath = GetFilePath(snapshot);
                var cacheKey = CompletionCache.BuildKey(
                    filePath,
                    _textView.Caret.Position.BufferPosition.GetContainingLineNumber(),
                    _textView.Caret.Position.BufferPosition.Position - _textView.Caret.Position.BufferPosition.GetContainingLine().Start.Position,
                    prefix,
                    suffix);

                string[] displayLines;
                if (!_cache.TryGet(cacheKey, out string suggestion))
                {
                    await ServiceConfiguration.InitializeAsync().ConfigureAwait(false);

                    if (ct.IsCancellationRequested) return;

                    var autocompletionsService = ServiceConfiguration.GetService<IAutocompletionsService>();
                    var parameters = new CompletionParameters
                    {
                        Prompt = prefix,
                        Suffix = suffix,
                        MaxTokens = DefaultMaxTokens,
                        Temperature = 0,
                        Stop = new[] { "\n\n" }
                    };
                    var raw = await autocompletionsService.GetCompletionAsync(parameters, ct).ConfigureAwait(false);

                    displayLines = SuggestionPostProcessor.Process(raw, prefix, suffix, MaxSuggestionLines);

                    if (displayLines != null)
                    {
                        suggestion = string.Join("\n", displayLines);
                        _cache.Set(cacheKey, suggestion);
                    }
                }
                else
                {
                    displayLines = suggestion.Split('\n');
                }

                if (ct.IsCancellationRequested || _disposed) return;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

                if (ct.IsCancellationRequested || _disposed) return;

                var currentSnapshot = _textView.TextSnapshot;
                var currentCaretPos = _textView.Caret.Position.BufferPosition.Position;

                if (currentCaretPos > currentSnapshot.Length)
                    currentCaretPos = currentSnapshot.Length;

                if (currentCaretPos != caretPos)
                {
                    InternalLogger.Info($"Caret moved: {caretPos} -> {currentCaretPos}");
                    return;
                }

                if (_completionBroker != null && _completionBroker.IsCompletionActive(_textView))
                {
                    InternalLogger.Info("Completion is active, skipping suggestion");
                    return;
                }

                if (!string.IsNullOrEmpty(suggestion) && _wpfTextView != null)
                {
                    var caretPoint = new SnapshotPoint(currentSnapshot, currentCaretPos);

                    var caretViewLine = _wpfTextView.GetTextViewLineContainingBufferPosition(caretPoint);
                    if (caretViewLine == null) return;

                    EnsureAdornmentsCreated(displayLines.Length);

                    int displayCount = _adornments.Count;

                    if (displayCount < displayLines.Length)
                    {
                        var displayed = new string[displayCount];
                        Array.Copy(displayLines, displayed, displayCount);
                        suggestion = string.Join("\n", displayed);
                        displayLines = displayed;
                    }

                    PrepareAdornments(displayLines, caretPoint, _adornments.ToArray());

                    _state = new SuggestionState(
                        suggestion,
                        caretPoint);

                    InternalLogger.Info($"Suggestion set: text='{suggestion}', caret={caretPoint}");
                }
                else
                {
                    InternalLogger.Info("Suggestion is null or empty");
                }
            }
            catch (OperationCanceledException)
            {
                InternalLogger.Info("OperationCanceledException");
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("SuggestionTagger: GenerateSuggestionAsync failed: " + ex.Message);
            }
        }

        private void PrepareAdornments(
            string[] lines,
            SnapshotPoint caretPoint,
            GhostTextAdornment[] adornments)
        {
            if (_wpfTextView == null) return;

            for (int i = 0; i < lines.Length && i < adornments.Length; i++)
            {
                var adornment = adornments[i];
                if (adornment == null) continue;

                adornment.Show(lines[i], caretPoint);
            }
        }

        private void EnsureAdornmentsCreated(int count)
        {
            if (_wpfTextView == null) return;

            if (count > MaxSuggestionLines)
                count = MaxSuggestionLines;

            while (_adornments.Count > count)
            {
                var last = _adornments.Count - 1;
                _adornments[last].Remove();
                _adornments.RemoveAt(last);
            }

            while (_adornments.Count < count)
            {
                _adornments.Add(new GhostTextAdornment(_wpfTextView));
            }
        }

        private static string GetFilePath(ITextSnapshot snapshot)
        {
            if (snapshot.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument doc))
            {
                return doc.FilePath ?? string.Empty;
            }
            return string.Empty;
        }

        internal sealed class SuggestionState
        {
            internal readonly string Text;
            internal readonly SnapshotPoint CaretPoint;

            internal SuggestionState(string text, SnapshotPoint caretPoint)
            {
                Text = text;
                CaretPoint = caretPoint;
            }

            internal bool HasValue => Text != null;
        }
    }
}




