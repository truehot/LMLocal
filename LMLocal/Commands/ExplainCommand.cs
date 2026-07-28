using Microsoft.VisualStudio.Shell;

namespace LMLocal.Commands
{
    /// <summary>
    /// Context menu command: Explain Code. Sends the selected code with an explanation prompt to LM Local.
    /// </summary>
    internal sealed class ExplainCommand : BaseCodeCommand
    {
        protected override int CommandId => 0x0207;

        protected override string ButtonText => "Explain Code";

        protected override string InstructionDisplayName => "Explain";

        protected override string PromptInstruction =>
            "Explain the following code in detail. Describe what it does, how it works, the purpose of key methods and classes, and any important design decisions.";

        private ExplainCommand(AsyncPackage package, OleMenuCommandService commandService) : base(package, commandService) { }
    }
}
