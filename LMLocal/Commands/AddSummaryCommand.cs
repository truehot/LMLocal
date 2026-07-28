using Microsoft.VisualStudio.Shell;

namespace LMLocal.Commands
{
    /// <summary>
    /// Context menu command: Add Summary. Sends the selected code with a documentation generation prompt to LM Local.
    /// </summary>
    internal sealed class AddSummaryCommand : BaseCodeCommand
    {
        protected override int CommandId => 0x0204;
        protected override string ButtonText => "Add Summary";

        protected override string PromptInstruction =>
            "Add XML documentation comments (///) for all public members and a summary for the file/class";

        private AddSummaryCommand(AsyncPackage package, OleMenuCommandService commandService) : base(package, commandService) { }
    }
}
