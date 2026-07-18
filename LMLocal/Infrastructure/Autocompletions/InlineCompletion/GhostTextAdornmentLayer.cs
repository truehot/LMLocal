using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace LMLocal.Infrastructure.Autocompletions.InlineCompletion
{
    internal static class GhostTextAdornmentLayer
    {
        [Export]
        [Name("GhostTextLayer")]
        [Order(After = PredefinedAdornmentLayers.Text)]
        public static readonly AdornmentLayerDefinition Definition = new AdornmentLayerDefinition();
    }
}
