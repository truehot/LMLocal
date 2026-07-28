using Microsoft.VisualStudio.Shell;

namespace LMLocal.Commands
{
    /// <summary>
    /// Context menu command: BugFix Code. Sends the selected code with a bug fix prompt to LM Local.
    /// </summary>
    internal sealed class BugFixCommand : BaseCodeCommand
    {
        protected override int CommandId => 0x0202;

        protected override string ButtonText => "BugFix Code";

        protected override string InstructionDisplayName => "Bugfix";

        protected override string PromptInstruction => "Fix any bugs in the following code. Provide the corrected version and explain what bugs were found and how they were resolved.";

        private BugFixCommand(AsyncPackage package, OleMenuCommandService commandService) : base(package, commandService) { }
    }
}
