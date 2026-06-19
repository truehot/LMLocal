using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.Abstractions;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions
{
    internal interface IBuiltInTool : ITool
    {
        /// <summary>
        /// Access level for this tool. ReadOnly tools can only read solution files;
        /// Execution tools can run external processes without modifying files;
        /// FullAccess tools can add, change, delete files.
        /// </summary>
        ToolAccessLevel AccessLevel { get; }

        string GetProcessingMessage(Dictionary<string, object> parameters);
        string GetCompletionMessage(object result);
        Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default);
    }
}