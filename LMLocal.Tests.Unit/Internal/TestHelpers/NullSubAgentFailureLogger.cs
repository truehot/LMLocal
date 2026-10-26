using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.SubAgents;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;

namespace LMLocal.Tests.Unit.Internal.TestHelpers
{
    /// <summary>
    /// No-op <see cref="ISubAgentFailureLogger"/> for tests that do not care about failure logging.
    /// </summary>
    internal sealed class NullSubAgentFailureLogger : ISubAgentFailureLogger
    {
        public static readonly NullSubAgentFailureLogger Instance = new NullSubAgentFailureLogger();

        public Task LogAsync(SubAgentFailureRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LogFromRequestAsync(
            SubAgentRunRequest request,
            string stage,
            string error,
            string runId = null,
            int? round = null,
            Guid? chatSessionId = null,
            Exception exception = null,
            IReadOnlyList<SubAgentRecentCall> recentCalls = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task LogFromToolCallAsync(
            ToolCallRecord call,
            string stage,
            string error,
            Exception exception = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
