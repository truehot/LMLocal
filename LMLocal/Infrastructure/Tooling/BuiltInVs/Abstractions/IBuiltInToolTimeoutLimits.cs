using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.Abstractions;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions
{
    /// <summary>
    /// Optional contract for built-in tools that declare an execution timeout budget.
    /// </summary>
    internal interface IBuiltInToolTimeoutLimits
    {
        ToolExecutionLimits TimeoutLimits { get; }
    }

    /// <summary>
    /// Long-running tools that must honour the time the caller actually gave them.
    /// </summary>
    internal interface IBuiltInToolAppliedTimeout : IBuiltInToolTimeoutLimits
    {
        Task<object> ExecuteAsync(Dictionary<string, object> parameters, TimeSpan appliedTimeout, CancellationToken cancellationToken);
    }
}
