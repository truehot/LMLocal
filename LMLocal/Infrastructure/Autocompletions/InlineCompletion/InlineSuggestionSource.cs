using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Autocompletions;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.DependencyInjection;
using Microsoft.VisualStudio.Language.Proposals;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;

namespace LMLocal.Infrastructure.Autocompletions.InlineCompletion
{
    /// <summary>
    /// Native inline suggestion source (VS Inline Suggestions / Proposals API).
    /// </summary>
    internal sealed class InlineSuggestionSource : ProposalSourceBase
    {
        private const string ProviderName = "LMLocal";

        private readonly ITextView _view;
        private readonly IEditorOperations _editorOperations;
        private readonly CompletionCache _cache = SuggestionTextViewCreationListener.SharedCache;

        private IAutocompletionsService _autocompletionsService;
        private CancellationTokenSource _generationCts;

        // Current proposal (UI thread only).
        private string _currentText;
        private string _currentProposalId;
        private ITrackingPoint _currentAnchor;

        // AutocompletionsConfig.SingleLinePreview as of the last generation.
        private bool _singleLinePreview = true;

        internal InlineSuggestionSource(ITextView view, IEditorOperations editorOperations)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _editorOperations = editorOperations;
        }

        public override Task DisposeAsync()
        {
            CancelGeneration();
            return Task.CompletedTask;
        }

        public override async Task<ProposalCollectionBase> RequestProposalsAsync(
            VirtualSnapshotPoint caret,
            CompletionState completionState,
            ProposalScenario scenario,
            char triggeringCharacter,
            CancellationToken cancel)
        {
            try
            {
                await InlineCompletionBackend.RefreshAsync(cancel).ConfigureAwait(false);
                if (!InlineCompletionBackend.IsNative)
                    return null;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancel);

                switch (scenario)
                {
                    case ProposalScenario.TypeChar:
                    case ProposalScenario.Return:
                        return await HandleTypingAsync(caret, completionState, cancel);

                    case ProposalScenario.CaretMove:
                    case ProposalScenario.Completion:
                        return ReEmitCurrent(caret, completionState);

                    default:
                        CancelGeneration();
                        ClearCurrent();
                        return null;
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("InlineSuggestionSource: " + ex.Message);
                return null;
            }
        }

        private async Task<ProposalCollectionBase> HandleTypingAsync(
            VirtualSnapshotPoint caret, CompletionState completionState, CancellationToken cancel)
        {
            var snapshot = caret.Position.Snapshot;
            int caretPos = caret.Position.Position;

            if (!_view.Selection.IsEmpty || _view.TextBuffer.IsReadOnly(caretPos))
            {
                CancelGeneration();
                ClearCurrent();
                return null;
            }

            if (TryTypeThrough(caret, completionState, out ProposalCollectionBase typeThrough))
                return typeThrough;

            string textAfterCaret = SuggestionTagger.GetTextAfterCaret(snapshot, caretPos);
            if (!SuggestionTagger.IsOnlyClosingChars(textAfterCaret))
            {
                CancelGeneration();
                return null;
            }

            return await GenerateAsync(caret, completionState, textAfterCaret, cancel);
        }

        /// <summary>
        /// Applies the text typed since the current anchor to the current suggestion.
        /// </summary>
        private bool TryTypeThrough(VirtualSnapshotPoint caret, CompletionState completionState, out ProposalCollectionBase result)
        {
            result = null;
            if (_currentText == null || _currentAnchor == null)
                return false;

            var snapshot = caret.Position.Snapshot;
            if (_currentAnchor.TextBuffer != snapshot.TextBuffer)
            {
                ClearCurrent();
                return false;
            }

            int anchor = _currentAnchor.GetPosition(snapshot);
            int caretPos = caret.Position.Position;
            if (caretPos < anchor)
            {
                ClearCurrent();
                return false;
            }

            if (caretPos == anchor)
            {
                result = BuildProposal(_currentText, caret, completionState, reuseId: true);
                return true;
            }

            string inserted = SuggestionTagger.NormalizeNewLines(snapshot.GetText(anchor, caretPos - anchor));
            bool atIndentation = SuggestionTagger.IsLineIndentationBefore(snapshot, anchor);
            if (!SuggestionTagger.TryConsume(_currentText, inserted, atIndentation, out string remainder))
            {
                ClearCurrent();
                return false;
            }

            if (remainder.Length == 0)
            {
                ClearCurrent();
                return true;
            }

            _cache.Set(SuggestionTagger.BuildCacheKey(snapshot, caretPos), remainder);
            result = BuildProposal(remainder, caret, completionState, reuseId: true);
            return true;
        }

        private async Task<ProposalCollectionBase> GenerateAsync(
            VirtualSnapshotPoint caret, CompletionState completionState, string textAfterCaret, CancellationToken cancel)
        {
            ClearCurrent();

            var cts = new CancellationTokenSource();
            var old = Interlocked.Exchange(ref _generationCts, cts);
            if (old != null)
            {
                try { old.Cancel(); } catch (ObjectDisposedException) { }
                old.Dispose();
            }

            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, cts.Token))
                {
                    var ct = linked.Token;
                    var snapshot = caret.Position.Snapshot;
                    int caretPos = caret.Position.Position;

                    var config = await GetConfigAsync(ct).ConfigureAwait(false);
                    int debounceMs = config != null && config.DebounceDelayMs > 0
                        ? config.DebounceDelayMs
                        : SuggestionTagger.DefaultDebounceDelayMs;
                    bool singleLinePreview = config?.SingleLinePreview ?? true;

                    await Task.Delay(debounceMs, ct).ConfigureAwait(false);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
                    if (!IsCaretAt(snapshot, caretPos))
                        return null;

                    string prefix = SuggestionTagger.GetPrefix(snapshot, caretPos);
                    string suffix = SuggestionTagger.GetSuffix(snapshot, caretPos);
                    bool caretLineIsBlank = string.IsNullOrWhiteSpace(snapshot.GetLineFromPosition(caretPos).GetText());
                    string cacheKey = SuggestionTagger.BuildCacheKey(snapshot, caretPos);

                    if (!_cache.TryGet(cacheKey, out string suggestion))
                    {
                        var service = GetAutocompletionsService();
                        if (service == null)
                            return null;

                        var raw = await service.GetCompletionAsync(
                            SuggestionTagger.CreateCompletionParameters(prefix, suffix), ct).ConfigureAwait(false);

                        suggestion = SuggestionTagger.PostProcessSuggestion(raw, suffix, caretLineIsBlank, textAfterCaret);
                        if (string.IsNullOrEmpty(suggestion))
                            return null;

                        _cache.Set(cacheKey, suggestion);
                    }

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
                    if (!IsCaretAt(snapshot, caretPos))
                        return null;

                    _singleLinePreview = singleLinePreview;
                    return BuildProposal(suggestion, caret, completionState, reuseId: false);
                }
            }
            finally
            {
                Interlocked.CompareExchange(ref _generationCts, null, cts);
                cts.Dispose();
            }
        }

        /// <summary>True when the view still shows the snapshot and caret position a request was made for.</summary>
        private bool IsCaretAt(ITextSnapshot snapshot, int caretPos)
        {
            return _view.TextSnapshot == snapshot &&
                   _view.Caret.Position.BufferPosition.Position == caretPos;
        }

        private ProposalCollectionBase BuildProposal(string text, VirtualSnapshotPoint caret, CompletionState completionState, bool reuseId)
        {
            var snapshot = caret.Position.Snapshot;
            int caretPos = caret.Position.Position;

            string insertText = SuggestionTagger.PrepareInsertText(
                text, SuggestionTagger.GetLineEnding(snapshot, caretPos, _view.Options));

            string previewText = _singleLinePreview ? SuggestionTagger.BuildCollapsedPreview(text) : text;

            string proposalId = reuseId && _currentProposalId != null
                ? _currentProposalId
                : ProviderName + "." + Guid.NewGuid().ToString("N");

            var edits = new[] { new ProposedEdit(new SnapshotSpan(caret.Position, 0), insertText) };

            var proposal = Proposal.TryCreateProposal(
                description: "LMLocal inline suggestion",
                edits: edits,
                caret: caret,
                completionState: completionState,
                flags: ProposalFlags.SingleTabToAccept | ProposalFlags.PreserveOnBufferChange,
                commitAction: () => Commit(insertText, snapshot, caretPos),
                proposalId: proposalId,
                acceptText: null,
                previewText: previewText,
                nextText: null,
                scope: null);

            if (proposal == null)
            {
                ClearCurrent();
                return null;
            }

            _currentText = text;
            _currentProposalId = proposalId;
            _currentAnchor = snapshot.CreateTrackingPoint(caretPos, PointTrackingMode.Negative);

            return new ProposalCollection(ProviderName, new ProposalBase[] { proposal });
        }

        private ProposalCollectionBase ReEmitCurrent(VirtualSnapshotPoint caret, CompletionState completionState)
        {
            if (_currentText == null || _currentAnchor == null ||
                _currentAnchor.TextBuffer != caret.Position.Snapshot.TextBuffer ||
                _currentAnchor.GetPosition(caret.Position.Snapshot) != caret.Position.Position)
            {
                ClearCurrent();
                return null;
            }

            return BuildProposal(_currentText, caret, completionState, reuseId: true);
        }

        /// <summary>
        /// Custom commit action (VS does not apply the edits when one is given): insert at the caret
        /// with the anchor and read-only checks.
        /// </summary>
        private bool Commit(string insertText, ITextSnapshot snapshot, int caretPos)
        {
            try
            {
                if (!IsCaretAt(snapshot, caretPos) || _view.TextBuffer.IsReadOnly(caretPos))
                    return false;

                SuggestionTagger.InsertAtCaret(_view, _editorOperations, insertText);
                ClearCurrent();
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("InlineSuggestionSource.Commit: " + ex.Message);
                return false;
            }
        }

        private void ClearCurrent()
        {
            _currentText = null;
            _currentProposalId = null;
            _currentAnchor = null;
        }

        private void CancelGeneration()
        {
            var cts = Interlocked.Exchange(ref _generationCts, null);
            if (cts == null) return;
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        private static async Task<AutocompletionsConfig> GetConfigAsync(CancellationToken cancel)
        {
            if (!ServiceConfiguration.IsInitialized)
                return null;

            var configManager = ServiceConfiguration.GetService<IAutocompletionsConfigManager>();
            return configManager != null
                ? await configManager.GetAsync(cancel).ConfigureAwait(false)
                : null;
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
    }
}
