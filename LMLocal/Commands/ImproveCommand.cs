using Microsoft.VisualStudio.Shell;

namespace LMLocal.Commands
{
    /// <summary>
    /// Context menu command: Improve Code. Sends the selected code with an improvement prompt to LM Local.
    /// </summary>
    internal sealed class ImproveCommand : BaseCodeCommand
    {
        protected override int CommandId => 0x0206;

        protected override string ButtonText => "Improve Code";

        protected override string InstructionDisplayName => "Improve";

        protected override string PromptInstruction =>
            "Suggest improvements for the following code. Focus on performance, readability, maintainability, and adherence to C# best practices. Provide specific code examples for each improvement.";

        private ImproveCommand(AsyncPackage package, OleMenuCommandService commandService) : base(package, commandService) { }
    }
}
