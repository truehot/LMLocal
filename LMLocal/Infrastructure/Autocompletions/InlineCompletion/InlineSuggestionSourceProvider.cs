using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Proposals;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;

namespace LMLocal.Infrastructure.Autocompletions.InlineCompletion
{
    /// <summary>
    /// MEF provider for the native inline suggestion source.
    /// </summary>
    [Export(typeof(ProposalSourceProviderBase))]
    [Name("LMLocal Inline Suggestion Source Provider")]
    [Order(Before = "default")]
    [Order(Before = ProposalSourceProviders.IntelliCodeCSharpProposalSource)]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class InlineSuggestionSourceProvider : ProposalSourceProviderBase
    {
        [Import]
        internal IEditorOperationsFactoryService EditorOperationsFactory { get; set; } = null;

        public override Task<ProposalSourceBase> GetProposalSourceAsync(
            ITextView view, CancellationToken cancel)
        {
            var editorOperations = EditorOperationsFactory?.GetEditorOperations(view);
            return Task.FromResult<ProposalSourceBase>(new InlineSuggestionSource(view, editorOperations));
        }
    }
}
