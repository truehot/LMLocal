using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LMLocal.Application.Autocompletions;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.DependencyInjection;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Language.Suggestions;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.OptionsExtensionMethods;
using Microsoft.VisualStudio.Text.Operations;


namespace LMLocal.Infrastructure.Autocompletions.InlineCompletion
{
    /// <summary>
    /// Drives the inline (ghost text) autocomplete feature for a single text view.
    /// </summary>
    internal class SuggestionTagger : IDisposable
    {
        internal const int MaxAdornments = 1;

        // Multiline mode. The FIM request parameters below are shared with the native source.
        internal const int MaxSuggestionLines = 10;
        internal const bool MultilineEnabled = true;

        // true  - line 1 plus a "  …+N" marker; Tab inserts the whole block.
        // false - lines 2..N rendered below the caret line via InterLineAdornmentTag (GhostLinesTagger).
        internal const bool CollapsedPreview = false;

        internal const int MaxPrefixLength = 300;
        internal const int MaxSuffixLength = 400;
        internal const int DefaultMaxTokens = 256;
        internal const int DefaultDebounceDelayMs = 300;
        internal static readonly string[] StopSequences = { "\n\n\n" };

        // Characters that may follow the caret without blocking a suggestion (auto-inserted closers).
        private const string ClosingChars = ")]}>\"';";

        private readonly ITextView _textView;
        private readonly IWpfTextView _wpfTextView;
        private readonly CompletionCache _cache;
        private readonly ICompletionBroker _completionBroker;

        private int? _debounceDelayMs;
        private SuggestionState _state;
        private IAutocompletionsService _autocompletionsService;
        private readonly IEditorOperations _editorOperations;
        private readonly SuggestionServiceBase _suggestionService;
        private readonly IAsyncCompletionBroker _asyncCompletionBroker;

        // Line number + text of the lines 2..N block currently shown by GhostLinesTagger (null = none).
        private string _shownLinesKey;

        // True while some part of the current suggestion is actually drawn.
        private bool _isVisible;


        private Timer _debounceTimer;
        private volatile CancellationTokenSource _operationCts;

        private readonly List<GhostTextAdornment> _adornments = new List<GhostTextAdornment>();

        private bool _suppressNextSuggestion;
        private bool _disposed;

        internal SuggestionTagger(
            ITextView textView,
            CompletionCache cache,
            ICompletionBroker completionBroker,
            IEditorOperations editorOperations = null,
            SuggestionServiceBase suggestionService = null,
            IAsyncCompletionBroker asyncCompletionBroker = null)
        {
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            _wpfTextView = textView as IWpfTextView;
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _completionBroker = completionBroker;
            _editorOperations = editorOperations;
            _suggestionService = suggestionService;
            _asyncCompletionBroker = asyncCompletionBroker;

            if (_suggestionService != null)
                _suggestionService.ProposalDisplayed += OnVsProposalDisplayed;

            if (_asyncCompletionBroker != null)
                _asyncCompletionBroker.CompletionTriggered += OnCompletionTriggered;
        }

        /// <summary>True while an IntelliSense completion list (legacy or async) is open in this view.</summary>
        private bool IsCompletionActive()
        {
            return (_completionBroker != null && _completionBroker.IsCompletionActive(_textView)) ||
                   (_asyncCompletionBroker != null && _asyncCompletionBroker.IsCompletionActive(_textView));
        }

        /// <summary>
        /// An IntelliSense completion list opened in this view.
        /// </summary>
        private void OnCompletionTriggered(object sender, CompletionTriggeredEventArgs e)
        {
            try
            {
                if (_disposed || e == null || e.TextView != _textView)
                    return;

                ClearSuggestion();

                var session = e.CompletionSession;
                if (session == null)
                    return;

                EventHandler onDismissed = null;
                EventHandler<CompletionItemEventArgs> onCommitted = null;
                void Detach()
                {
                    session.Dismissed -= onDismissed;
                    session.ItemCommitted -= onCommitted;
                }
                onDismissed = (s, a) =>
                {
                    Detach();
                    if (!_disposed) ScheduleSuggestion();
                };
                onCommitted = (s, a) =>
                {
                    Detach();
                    if (!_disposed) ScheduleSuggestion();
                };
                session.Dismissed += onDismissed;
                session.ItemCommitted += onCommitted;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("OnCompletionTriggered: " + ex.Message);
            }
        }

        /// <summary>
        /// A VS inline suggestion (Copilot, IntelliCode) appeared while ours is shown: ours was first, so it
        /// stays and the VS one is dismissed instead of drawing both on top of each other.
        /// </summary>
        private void OnVsProposalDisplayed(object sender, ProposalDisplayedEventArgs e)
        {
            try
            {
                if (_disposed || e == null || e.View != _textView)
                    return;

                if (!HasSuggestion || !_isVisible)
                    return;

                DismissVsProposal();
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("OnVsProposalDisplayed: " + ex.Message);
            }
        }

        /// <summary>Dismisses the VS inline suggestion displayed in this view, if any.</summary>
        private void DismissVsProposal()
        {
            var service = _suggestionService;
            if (service == null || service.GetDisplayedProposal(_textView) == null)
                return;

            _ = DismissVsProposalAsync(service);
        }

        private async Task DismissVsProposalAsync(SuggestionServiceBase service)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(alwaysYield: true);
                if (_disposed || !HasSuggestion || !_isVisible || service.GetDisplayedProposal(_textView) == null)
                    return;

                await service.DismissProposalAsync(_textView, ReasonForDismiss.DismissedBySession, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("DismissVsProposalAsync: " + ex.Message);
            }
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
                if (!ServiceConfiguration.IsInitialized)
                    return;

                var configManager = ServiceConfiguration.GetService<IAutocompletionsConfigManager>();
                if (configManager != null)
                {
                    var config = await configManager.GetAsync().ConfigureAwait(false);
                    _debounceDelayMs = config?.DebounceDelayMs ?? DefaultDebounceDelayMs;
                    InlineCompletionBackend.Apply(config);
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

        private IAutocompletionsService GetAutocompletionsService()
        {
            if (_autocompletionsService != null)
                return _autocompletionsService;

            if (!ServiceConfiguration.IsInitialized)
                return null;

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
            _isVisible = false;

            foreach (var ad in _adornments)
            {
                ad.Hide();
            }

            HideGhostLines();
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

            if (_textView.TextBuffer.IsReadOnly(currentCaret.Position))
                return;

            string text = PrepareInsertText(suggestion, GetLineEnding(snapshot, caret.Position, _textView.Options));

            _suppressNextSuggestion = true;
            try
            {
                InsertAtCaret(_textView, _editorOperations, text);
            }
            finally
            {
                ClearSuggestion();
                _suppressNextSuggestion = false;
                ScheduleSuggestion();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_suggestionService != null)
                _suggestionService.ProposalDisplayed -= OnVsProposalDisplayed;

            if (_asyncCompletionBroker != null)
                _asyncCompletionBroker.CompletionTriggered -= OnCompletionTriggered;

            CancelDebounce();
            CancelOperation();

            _debounceTimer?.Dispose();
            _debounceTimer = null;

            _state = null;

            HideGhostLines();

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

            _ = InlineCompletionBackend.RefreshAsync();
            if (InlineCompletionBackend.IsNative)
            {
                ClearSuggestion();
                return;
            }

            if (e.Changes.Count == 0) return;

            ThreadHelper.ThrowIfNotOnUIThread();

            if (IsCompletionActive())
            {
                ClearSuggestion();
                return;
            }

            var state = _state;
            if (state != null && state.HasValue && !string.IsNullOrEmpty(state.Text))
            {
                if (e.Changes.Count == 1)
                {
                    var change = e.Changes[0];
                    if (change.OldLength == 0 && change.NewLength > 0)
                    {
                        string inserted = NormalizeNewLines(e.After.GetText(change.NewPosition, change.NewLength));

                        bool atIndentation = IsLineIndentationBefore(e.After, change.NewPosition);
                        if (TryConsume(state.Text, inserted, atIndentation, out string remainder))
                        {
                            if (remainder.Length == 0)
                            {
                                ClearSuggestion();
                                return;
                            }

                            int newPos = change.NewPosition + change.NewLength;
                            var caretPoint = new SnapshotPoint(e.After, newPos);
                            _state = new SuggestionState(remainder, caretPoint);
                            _cache.Set(BuildCacheKey(e.After, newPos), remainder);

                            return;
                        }
                    }
                }
            }

            ClearSuggestion();
            ScheduleSuggestion();
        }

        /// <summary>
        /// Called after each view layout.
        /// </summary>
        internal void HandleLayoutChanged(TextViewLayoutChangedEventArgs e)
        {
            if (_disposed || _suppressNextSuggestion) return;

            var s = _state;
            if (s == null || !s.HasValue) return;

            var p = s.CaretPoint;
            if (p.Snapshot != e.NewSnapshot) return;

            foreach (var line in e.NewOrReformattedLines)
            {
                if (line.Start.Position <= p.Position && p.Position <= line.End.Position)
                {
                    ShowGhostText(s.Text, p);
                    return;
                }
            }
        }

        /// <summary>
        /// Called when the caret moves. Hides an active ghost-text suggestion when the caret leaves its anchor position.
        /// </summary>
        internal void HandleCaretMoved(CaretPositionChangedEventArgs e)
        {
            if (_disposed || _suppressNextSuggestion) return;

            var s = _state;
            if (s == null || !s.HasValue) return;

            if (e.NewPosition.BufferPosition == s.CaretPoint)
            {
                if (e.OldPosition.VirtualBufferPosition.VirtualSpaces != e.NewPosition.VirtualBufferPosition.VirtualSpaces)
                {
                    ShowGhostText(s.Text, s.CaretPoint);
                }
                return;
            }

            ClearSuggestion();
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

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

                var snapshot = _textView.TextSnapshot;
                int caretPos = _textView.Caret.Position.BufferPosition.Position;
                if (caretPos > snapshot.Length) caretPos = snapshot.Length;

                if (InlineCompletionBackend.IsNative) return;
                if (!_textView.Selection.IsEmpty) return;
                if (_textView.TextBuffer.IsReadOnly(caretPos))
                    return;

                string prefix = GetPrefix(snapshot, caretPos);
                string suffix = GetSuffix(snapshot, caretPos);

                var caretLine = snapshot.GetLineFromPosition(caretPos);
                string textAfterCaret = GetTextAfterCaret(snapshot, caretPos);
                if (!string.IsNullOrWhiteSpace(textAfterCaret))
                {
                    InternalLogger.Info("Caret NOT at end of line — skipping suggestion");
                    return;
                }

                bool caretLineIsBlank = string.IsNullOrWhiteSpace(caretLine.GetText());

                var cacheKey = BuildCacheKey(snapshot, caretPos);

                if (!_cache.TryGet(cacheKey, out string suggestion))
                {
                    var autocompletionsService = GetAutocompletionsService();
                    if (autocompletionsService == null) return;

                    var parameters = CreateCompletionParameters(prefix, suffix);
                    var raw = await autocompletionsService.GetCompletionAsync(parameters, ct).ConfigureAwait(false);

                    suggestion = PostProcessSuggestion(raw, suffix, caretLineIsBlank, textAfterCaret);

                    if (!string.IsNullOrEmpty(suggestion))
                        _cache.Set(cacheKey, suggestion);
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

                if (ct.IsCancellationRequested || _disposed) return;

                var currentSnapshot = _textView.TextSnapshot;
                var currentCaretPos = _textView.Caret.Position.BufferPosition.Position;
                if (currentCaretPos > currentSnapshot.Length) currentCaretPos = currentSnapshot.Length;

                if (currentCaretPos != caretPos || currentSnapshot.Version.VersionNumber != snapshot.Version.VersionNumber)
                {
                    InternalLogger.Info($"Context changed: caret {caretPos}->{currentCaretPos}, " +
                                        $"snapshot {snapshot.Version.VersionNumber}->{currentSnapshot.Version.VersionNumber}");
                    return;
                }

                if (!string.IsNullOrEmpty(suggestion) && _suggestionService?.GetDisplayedProposal(_textView) != null)
                {
                    return;
                }

                if (!string.IsNullOrEmpty(suggestion) && IsCompletionActive())
                {
                    return;
                }

                if (!string.IsNullOrEmpty(suggestion) && _wpfTextView != null)
                {
                    var caretPoint = new SnapshotPoint(currentSnapshot, currentCaretPos);

                    ShowGhostText(suggestion, caretPoint);

                    _state = new SuggestionState(suggestion, caretPoint);

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

        /// <summary>
        /// Cuts a fragment of text before the caret with a maximum length of MaxPrefixLength.
        /// </summary>
        internal static string GetPrefix(ITextSnapshot snapshot, int caretPos)
        {
            int start = Math.Max(0, caretPos - MaxPrefixLength);
            int length = caretPos - start;
            return length > 0 ? snapshot.GetText(start, length) : string.Empty;
        }

        /// <summary>
        /// Cuts a fragment of text after the caret with a maximum length of MaxSuffixLength.
        /// </summary>
        internal static string GetSuffix(ITextSnapshot snapshot, int caretPos)
        {
            int length = Math.Min(MaxSuffixLength, snapshot.Length - caretPos);
            return length > 0 ? snapshot.GetText(caretPos, length) : string.Empty;
        }

        /// <summary>Text between the caret and the end of its line (without the line break).</summary>
        internal static string GetTextAfterCaret(ITextSnapshot snapshot, int caretPos)
        {
            int lineEnd = snapshot.GetLineFromPosition(caretPos).End.Position;
            return caretPos < lineEnd ? snapshot.GetText(caretPos, lineEnd - caretPos) : string.Empty;
        }

        /// <summary>FIM request parameters used by both backends.</summary>
        internal static CompletionParameters CreateCompletionParameters(string prefix, string suffix)
        {
            return new CompletionParameters
            {
                Prompt = prefix,
                Suffix = suffix,
                MaxTokens = DefaultMaxTokens,
                Temperature = 0,
                Stop = StopSequences
            };
        }

        /// <summary>
        /// Turns a raw LLM answer into the suggestion text ('\n' form):
        /// multiline post-processing, then - when text follows the caret - only the first line
        /// (a multiline insert would split the line) with the repeated closers removed.
        /// </summary>
        internal static string PostProcessSuggestion(string raw, string suffix, bool caretLineIsBlank, string textAfterCaret)
        {
            string suggestion = MultilineEnabled
                ? SuggestionPostProcessor.ProcessMultiline(raw, MaxSuggestionLines, suffix, caretLineIsBlank)
                : SuggestionPostProcessor.Process(raw, 1);

            if (!string.IsNullOrWhiteSpace(textAfterCaret))
            {
                suggestion = TrimClosingOverlap(FirstLine(suggestion), textAfterCaret);
                if (string.IsNullOrWhiteSpace(suggestion))
                    return null;
            }

            return suggestion;
        }

        /// <summary>First line of the text (without '\n').</summary>
        internal static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            int nl = text.IndexOf('\n');
            return nl < 0 ? text : text.Substring(0, nl);
        }

        /// <summary>
        /// D7: only whitespace and closing characters may follow the caret.
        /// </summary>
        internal static bool IsOnlyClosingChars(string textAfterCaret)
        {
            if (string.IsNullOrEmpty(textAfterCaret)) return true;
            foreach (char c in textAfterCaret)
                if (!char.IsWhiteSpace(c) && ClosingChars.IndexOf(c) < 0)
                    return false;
            return true;
        }

        /// <summary>
        /// Removes from the end of a single-line suggestion the longest prefix of the text that already
        /// follows the caret, so accepting "bar)" before an existing ")" does not produce "bar))".
        /// </summary>
        internal static string TrimClosingOverlap(string suggestion, string textAfterCaret)
        {
            if (string.IsNullOrEmpty(suggestion) || string.IsNullOrEmpty(textAfterCaret))
                return suggestion;

            string after = textAfterCaret.Trim();
            string body = suggestion.TrimEnd(' ', '\t');
            for (int k = Math.Min(after.Length, body.Length); k > 0; k--)
            {
                if (body.EndsWith(after.Substring(0, k), StringComparison.Ordinal))
                    return body.Substring(0, body.Length - k);
            }
            return suggestion;
        }

        /// <summary>True when only spaces/tabs precede <paramref name="position"/> on its line.</summary>
        internal static bool IsLineIndentationBefore(ITextSnapshot snapshot, int position)
        {
            var line = snapshot.GetLineFromPosition(position);
            int length = position - line.Start.Position;
            return length <= 0 || snapshot.GetText(line.Start.Position, length).Trim(' ', '\t').Length == 0;
        }

        /// <summary>'\r\n' and '\r' to '\n'.</summary>
        internal static string NormalizeNewLines(string text)
        {
            return text?.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        /// <summary>
        /// Matches inserted text against the start of the suggestion ('\n' form).
        /// </summary>
        internal static bool TryConsume(string suggestion, string inserted, out string remainder)
        {
            return TryConsume(suggestion, inserted, atLineIndentation: false, out remainder);
        }

        /// <param name="atLineIndentation">
        /// True when only whitespace precedes the insertion on its line.
        /// </param>
        internal static bool TryConsume(string suggestion, string inserted, bool atLineIndentation, out string remainder)
        {
            if (atLineIndentation && !string.IsNullOrEmpty(inserted) && !string.IsNullOrEmpty(suggestion) &&
                (inserted[0] == ' ' || inserted[0] == '\t') && inserted.Trim(' ', '\t').Length > 0 &&
                inserted.IndexOf('\n') < 0)
            {
                string typed = inserted.TrimStart(' ', '\t');
                string rest = suggestion.TrimStart(' ', '\t');
                if (rest.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                {
                    remainder = rest.Substring(typed.Length);
                    return true;
                }
            }

            return TryConsumeCore(suggestion, inserted, out remainder);
        }

        private static bool TryConsumeCore(string suggestion, string inserted, out string remainder)
        {
            remainder = null;

            if (string.IsNullOrEmpty(inserted) || string.IsNullOrEmpty(suggestion)) return false;

            if (inserted.Trim(' ', '\t').Length == 0)
            {
                string trimmed = suggestion.TrimStart(' ', '\t');
                if (trimmed.Length == suggestion.Length) return false;
                remainder = trimmed;
                return true;
            }

            if (inserted.StartsWith("\n"))
            {
                if (!suggestion.StartsWith("\n")) return false;
                if (inserted.Substring(1).Trim(' ', '\t').Length != 0) return false;   // more than indentation after \n
                remainder = suggestion.Substring(1).TrimStart(' ', '\t');
                return true;
            }

            if (!suggestion.StartsWith(inserted, StringComparison.OrdinalIgnoreCase)) return false;
            remainder = suggestion.Substring(inserted.Length);
            return true;
        }

        /// <summary>Cache key for a snapshot and caret position.</summary>
        internal static string BuildCacheKey(ITextSnapshot snapshot, int caretPos)
        {
            var filePath = GetFilePath(snapshot);
            int lineNumber = snapshot.GetLineNumberFromPosition(caretPos);
            int column = caretPos - snapshot.GetLineFromPosition(caretPos).Start.Position;
            return CompletionCache.BuildKey(filePath, lineNumber, column, GetPrefix(snapshot, caretPos), GetSuffix(snapshot, caretPos));
        }

        /// <summary>
        /// Buffer line ending at the insertion point (GetLineBreakText is empty for the last line).
        /// </summary>
        internal static string GetLineEnding(ITextSnapshot snapshot, int position, IEditorOptions options)
        {
            string lb = snapshot.GetLineFromPosition(position).GetLineBreakText();
            string firstLb = snapshot.LineCount > 1 ? snapshot.GetLineFromLineNumber(0).GetLineBreakText() : null;
            string optionNewLine = options?.GetOptionValue(DefaultOptions.NewLineCharacterOptionId) ?? Environment.NewLine;
            return ResolveLineEnding(lb, firstLb, optionNewLine);
        }

        /// <summary>
        /// Picks the line ending: break at the insertion point, then the first line's break, then the editor option.
        /// </summary>
        internal static string ResolveLineEnding(string lineBreakText, string firstLineBreakText, string optionNewLine)
        {
            if (!string.IsNullOrEmpty(lineBreakText)) return lineBreakText;
            if (!string.IsNullOrEmpty(firstLineBreakText)) return firstLineBreakText;
            return optionNewLine;
        }

        /// <summary>Text to insert: the suggestion's '\n' converted to the buffer's line endings.</summary>
        internal static string PrepareInsertText(string suggestion, string newLine)
        {
            return suggestion == null ? null : suggestion.Replace("\n", newLine);
        }

        /// <summary>
        /// Inserts text at the caret.
        /// </summary>
        internal static void InsertAtCaret(ITextView textView, IEditorOperations editorOperations, string text)
        {
            if (editorOperations != null && !textView.Options.IsOverwriteModeEnabled())
            {
                editorOperations.InsertText(text);
                return;
            }

            var caret = textView.Caret.Position.VirtualBufferPosition;
            string virtualIndent = caret.IsInVirtualSpace && editorOperations != null
                ? editorOperations.GetWhitespaceForVirtualSpace(caret)
                : string.Empty;

            using (var edit = textView.TextBuffer.CreateEdit())
            {
                edit.Insert(caret.Position.Position, virtualIndent + text);
                edit.Apply();
            }
        }

        /// <summary>
        /// First line plus a "  …+N" marker for lines 2..N;
        /// only the marker when the first line is empty; no marker for a single-line suggestion.
        /// </summary>
        internal static string BuildCollapsedPreview(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            int nl = text.IndexOf('\n');
            if (nl < 0) return text;
            string first = text.Substring(0, nl);
            int restLines = text.Substring(nl + 1).Split('\n').Length;
            return first.Length > 0 ? first + "  …+" + restLines : "  …+" + restLines;
        }

        private string ShowGhostText(string text, SnapshotPoint caretPoint)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            string first = CollapsedPreview ? BuildCollapsedPreview(text) : FirstLine(text);

            EnsureAdornmentsCreated(1);
            bool firstVisible = false;
            if (_adornments.Count > 0)
            {
                if (first.Length > 0)
                    firstVisible = _adornments[0].Show(first, caretPoint) && !string.IsNullOrWhiteSpace(first);
                else
                    _adornments[0].Hide();
            }

            ShowGhostLines(text, caretPoint);

            _isVisible = firstVisible || _shownLinesKey != null;

            if (_isVisible)
                DismissVsProposal();

            return text;
        }

        /// <summary>
        /// Lines 2..N under the caret line (GhostLinesTagger).
        /// </summary>
        private void ShowGhostLines(string text, SnapshotPoint caretPoint)
        {
            int nl = text.IndexOf('\n');
            if (!MultilineEnabled || CollapsedPreview || nl < 0 || _wpfTextView == null)
            {
                HideGhostLines();
                return;
            }

            string rest = text.Substring(nl + 1);
            int lineNumber = caretPoint.GetContainingLine().LineNumber;
            string key = lineNumber + "\0" + rest;
            if (key == _shownLinesKey)
                return;

            _shownLinesKey = key;
            GhostLinesTagger.GetOrCreate(_textView).Show(
                caretPoint,
                CreateRestLinesElement(rest),
                rest.Split('\n').Length,
                _wpfTextView.LineHeight);
        }

        private void HideGhostLines()
        {
            if (_shownLinesKey == null)
                return;

            _shownLinesKey = null;
            GhostLinesTagger.GetOrCreate(_textView).Hide();
        }

        /// <summary>
        /// Ghost text block for lines 2..N. A new element per tag: InterLineAdornmentTag adornments
        /// must not be shared between tags (SDK docs).
        /// </summary>
        private UIElement CreateRestLinesElement(string rest)
        {
            var props = _wpfTextView.FormattedLineSource?.DefaultTextProperties;
            var block = new TextBlock
            {
                Text = rest.Replace("\t", new string(' ', _textView.Options.GetTabSize())),
                LineHeight = _wpfTextView.LineHeight,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Opacity = 0.65,
                Foreground = GhostTextAdornment.GetGhostBrush(_wpfTextView),
                IsHitTestVisible = false,
                FontStyle = FontStyles.Normal,
                FontWeight = FontWeights.Normal
            };

            if (props != null)
            {
                block.FontFamily = props.Typeface.FontFamily;
                block.FontSize = props.FontRenderingEmSize;
            }

            return block;
        }

        private void EnsureAdornmentsCreated(int count)
        {
            if (_wpfTextView == null) return;

            if (count > MaxAdornments)
                count = MaxAdornments;

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

        internal static string GetFilePath(ITextSnapshot snapshot)
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
