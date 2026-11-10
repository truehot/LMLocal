using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Proposals;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace LMLocal.Infrastructure.Autocompletions.InlineCompletion
{
    /// <summary>
    /// MEF provider for the native inline suggestion manager.
    /// </summary>
    [Export(typeof(ProposalManagerProviderBase))]
    [Name("LMLocal Inline Suggestion Manager Provider")]
    [Order(Before = "default")]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class InlineSuggestionManagerProvider : ProposalManagerProviderBase
    {
        public override Task<ProposalManagerBase> GetProposalManagerAsync(
            ITextView view, CancellationToken cancel)
        {
            return Task.FromResult<ProposalManagerBase>(new InlineSuggestionManager(view));
        }

        private sealed class InlineSuggestionManager : ProposalManagerBase
        {
            private readonly ITextView _view;

            public InlineSuggestionManager(ITextView view)
            {
                _view = view;
            }

            public override bool TryGetIsProposalPosition(
                VirtualSnapshotPoint caret,
                ProposalScenario scenario,
                char triggeringCharacter,
                ref bool value)
            {
                _ = InlineCompletionBackend.RefreshAsync();
                if (!InlineCompletionBackend.IsNative)
                    return false;

                if (_view != null &&
                    (_view.Roles.Contains(PredefinedTextViewRoles.PreviewTextView) || _view.Roles.Contains("COMMANDVIEW")))
                    return false;

                value = true;
                return true;
            }
        }
    }
}
