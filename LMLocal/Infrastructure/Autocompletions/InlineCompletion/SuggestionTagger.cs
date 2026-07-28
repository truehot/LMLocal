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
        internal const int MaxAdorments = 1;
        internal const int MaxSuggestionLines = 1;

        private const int MaxPrefixLength = 400;
        private const int MaxSuffixLength = 200;
        private const int DefaultMaxTokens = 350;
        private const int DefaultDebounceDelayMs = 300;

        private readonly ITextView _textView;
        private readonly IWpfTextView _wpfTextView;
        private readonly CompletionCache _cache;
        private readonly ICompletionBroker _completionBroker;

        private int? _debounceDelayMs;
        private SuggestionState _state;
        private IAutocompletionsService _autocompletionsService;


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

        internal static bool TryGet(ITextView textView, out SuggestionTagger tagger)
        {
            return textView.Properties.TryGetProperty(typeof(SuggestionTagger), out tagger);
        }

        private int GetDebounceDelayMs()
        {
            if (_debounceDelayMs.HasValue)
                return _debounceDelayMs.Value;

            _ = FetchDebounceDelayAsync();

            return DefaultDebounceDelayMs;
        }

        private async Task FetchDebounceDelayAsync()
        {
            try
            {
                var configManager = ServiceConfiguration.GetService<IAutocompletionsConfigManager>();
                if (configManager != null)
                {
                    var config = await configManager.GetAsync().ConfigureAwait(false);
                    _debounceDelayMs = config?.DebounceDelayMs ?? DefaultDebounceDelayMs;
                }
                else
                {
                    _debounceDelayMs = DefaultDebounceDelayMs;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("FetchDebounceDelayAsync failed: " + ex.Message);
                _debounceDelayMs = DefaultDebounceDelayMs;
            }
        }

        private async Task<IAutocompletionsService> GetAutocompletionsServiceAsync()
        {
            if (_autocompletionsService != null)
                return _autocompletionsService;

            await ServiceConfiguration.InitializeAsync().ConfigureAwait(false);
            _autocompletionsService = ServiceConfiguration.GetService<IAutocompletionsService>();
            return _autocompletionsService;
        }


        internal void ScheduleSuggestion()
        {
            if (_disposed || _suppressNextSuggestion) return;

            int delay = GetDebounceDelayMs();

            if (_debounceTimer == null)
            {
                _debounceTimer = new Timer(OnDebounceElapsed, null, delay, Timeout.Infinite);
            }
            else
            {
                _debounceTimer.Change(delay, Timeout.Infinite);
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
                {
                    ad.Hide();
                }

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

        /// <summary>
        /// Called on each text change.
        /// </summary>
        internal void HandleTextChanged(TextContentChangedEventArgs e)
        {
            if (_disposed || _suppressNextSuggestion) return;
            if (e.Changes.Count == 0) return;
            ThreadHelper.ThrowIfNotOnUIThread();
            var state = _state;
            if (state != null && state.HasValue && !string.IsNullOrEmpty(state.Text))
            {
                if (e.Changes.Count == 1)
                {
                    var change = e.Changes[0];
                    if (change.OldLength == 0 && change.NewLength > 0)
                    {
                        string insertedText = e.After.GetText(change.NewPosition, change.NewLength);
                        if (!string.IsNullOrEmpty(insertedText) && state.Text.StartsWith(insertedText, StringComparison.OrdinalIgnoreCase))
                        {
                            string trimmed = state.Text.Substring(insertedText.Length);

                            if (string.IsNullOrEmpty(trimmed))
                            {
                                ClearSuggestion();
                                return;
                            }

                            int newPos = change.NewPosition + change.NewLength;
                            var caretPoint = new SnapshotPoint(e.After, newPos);
                            _state = new SuggestionState(trimmed, caretPoint);

                            string prefix = GetPrefix(e.After, newPos);
                            string suffix = GetSuffix(e.After, newPos);
                            var filePath = GetFilePath(e.After);
                            int lineNumber = e.After.GetLineNumberFromPosition(newPos);
                            int column = newPos - e.After.GetLineFromPosition(newPos).Start.Position;

                            var cacheKey = CompletionCache.BuildKey(filePath, lineNumber, column, prefix, suffix);
                            _cache.Set(cacheKey, trimmed);
                        }
                    }
                }
            }

            if (_completionBroker != null && _completionBroker.IsCompletionActive(_textView))
                return;

            ClearSuggestion();
            ScheduleSuggestion();
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

                string prefix = GetPrefix(snapshot, caretPos);
                string suffix = GetSuffix(snapshot, caretPos);

                if (!string.IsNullOrEmpty(suffix))
                {
                    var line = snapshot.GetLineFromPosition(caretPos);
                    int lineEndPos = line.End.Position;
                    int remainingLen = lineEndPos - caretPos;
                    if (remainingLen > 0)
                    {
                        string textAfterCaret = snapshot.GetText(caretPos, remainingLen);
                        if (!string.IsNullOrWhiteSpace(textAfterCaret))
                        {
                            InternalLogger.Info("Caret NOT at end of line — skipping suggestion");
                            return;
                        }
                    }
                }

                var filePath = GetFilePath(snapshot);
                var cacheKey = CompletionCache.BuildKey(
                    filePath,
                    _textView.Caret.Position.BufferPosition.GetContainingLineNumber(),
                    _textView.Caret.Position.BufferPosition.Position - _textView.Caret.Position.BufferPosition.GetContainingLine().Start.Position,
                    prefix,
                    suffix);

                if (!_cache.TryGet(cacheKey, out string suggestion))
                {
                    var autocompletionsService = await GetAutocompletionsServiceAsync().ConfigureAwait(false);

                    if (autocompletionsService == null) return;
                    var parameters = new CompletionParameters
                    {
                        Prompt = prefix,
                        Suffix = suffix,
                        MaxTokens = DefaultMaxTokens,
                        Temperature = 0,
                        Stop = new[] { "\n\n\n" }
                    };
                    var raw = await autocompletionsService.GetCompletionAsync(parameters, ct).ConfigureAwait(false);

                    suggestion = SuggestionPostProcessor.Process(raw, MaxSuggestionLines);

                    if (!string.IsNullOrEmpty(suggestion))
                        _cache.Set(cacheKey, suggestion);
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

                if (!string.IsNullOrEmpty(suggestion) && _wpfTextView != null)
                {
                    var caretPoint = new SnapshotPoint(currentSnapshot, currentCaretPos);

                    string clipped = ShowGhostText(suggestion, caretPoint);

                    _state = new SuggestionState(clipped, caretPoint);

                    InternalLogger.Info($"Suggestion set: text='{clipped}', caret={caretPoint}");
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

        /// <summary>
        /// Cuts a fragment of text before the caret with a maximum length of MaxPrefixLength.
        /// </summary>
        private string GetPrefix(ITextSnapshot snapshot, int caretPos)
        {
            int start = Math.Max(0, caretPos - MaxPrefixLength);
            int length = caretPos - start;
            return length > 0 ? snapshot.GetText(start, length) : string.Empty;
        }

        /// <summary>
        /// Cuts a fragment of text after the caret with a maximum length of MaxSuffixLength.
        /// </summary>
        private string GetSuffix(ITextSnapshot snapshot, int caretPos)
        {
            int length = Math.Min(MaxSuffixLength, snapshot.Length - caretPos);
            return length > 0 ? snapshot.GetText(caretPos, length) : string.Empty;
        }

        private string ShowGhostText(string text, SnapshotPoint caretPoint)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            EnsureAdornmentsCreated(1);
            if (_adornments.Count > 0)
                _adornments[0].Show(text, caretPoint);

            return text;
        }

        private void EnsureAdornmentsCreated(int count)
        {
            if (_wpfTextView == null) return;

            if (count > MaxAdorments)
                count = MaxAdorments;

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
