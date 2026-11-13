using System.Text;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Tool;

namespace LMLocal.Application.ChatSessionStream
{
    /// <summary>
    /// Stops a tool call whose arguments grew past the tool's limit (<see cref="ToolArgumentLimits"/>).
    /// </summary>
    internal sealed class ArgumentsLengthGuard : IToolCallArgumentsGuard
    {
        public static readonly ArgumentsLengthGuard Instance = new ArgumentsLengthGuard();

        public string Check(string functionName, StringBuilder arguments, string fragment)
        {
            if (arguments == null)
                return null;

            int? max = ToolArgumentLimits.GetMaxLength(functionName);
            if (max == null || arguments.Length <= max.Value)
                return null;

            return $"Arguments of {functionName} exceed {max.Value} chars";
        }
    }
}
