using System.Collections.Generic;
using LMLocal.Infrastructure.Tooling.Abstractions;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions
{
    internal interface IBuiltInTool : ITool
    {
        string GetProcessingMessage(Dictionary<string, object> parameters);
        string GetCompletionMessage(object result);
    }
}
