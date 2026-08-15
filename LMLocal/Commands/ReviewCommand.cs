using Microsoft.VisualStudio.Shell;

namespace LMLocal.Commands
{
    /// <summary>
    /// Context menu command: Review Code. Sends the selected code with a review prompt to LM Local.
    /// </summary>
    internal sealed class ReviewCommand : BaseCodeCommand
    {
        protected override int CommandId => 0x0201;

        protected override string InstructionDisplayName => "Review";

        protected override string PromptInstruction =>
            "Review the following code. Identify potential bugs, security issues, performance problems, and code smells. Provide specific, actionable recommendations.";

        private ReviewCommand(AsyncPackage package, OleMenuCommandService commandService) : base(package, commandService) { }
    }
}
