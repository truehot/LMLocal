using Microsoft.VisualStudio.Shell;

namespace LMLocal.Commands
{
    /// <summary>
    /// Context menu command: Add Unit Tests. Sends the selected code with a unit test generation prompt to LM Local.
    /// </summary>

    internal sealed class AddUnitTestsCommand : BaseCodeCommand
    {
        protected override int CommandId => 0x0203;
        protected override string ButtonText => "Add Unit Tests";
        protected override string InstructionDisplayName => "Tests";
        protected override string PromptInstruction =>
            "Generate unit tests for the following code. Use the testing framework that best matches this project. Cover edge cases and happy path.";

        private AddUnitTestsCommand(AsyncPackage package, OleMenuCommandService commandService) : base(package, commandService) { }
    }
}
