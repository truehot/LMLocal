using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.SubAgents.ReadRefs;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;

namespace LMLocal.Application.SubAgents
{
    /// <summary>
    /// Executes the tool calls of SubAgent rounds; one instance per run, it counts the used tools and the steps.
    /// </summary>
    internal sealed class SubAgentToolRoundExecutor
    {
        private readonly Func<IToolExecutionManager> _managerResolver;
        private readonly ToolQueue _queue;
        private readonly IProgress<ToolActivityEvent> _progress;
        private readonly SubAgentRunEvidence _evidence;
        private readonly HashSet<string> _usedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public SubAgentToolRoundExecutor(
            Func<IToolExecutionManager> managerResolver,
            ToolQueue queue,
            IProgress<ToolActivityEvent> progress,
            SubAgentRunEvidence evidence)
        {
            _managerResolver = managerResolver ?? throw new ArgumentNullException(nameof(managerResolver));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _progress = progress;
            _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        }

        /// <summary>Names of the allowed tools executed so far (case-insensitive set).</summary>
        public IReadOnlyCollection<string> UsedTools => _usedTools;

        /// <summary>Number of allowed tool calls executed so far.</summary>
        public int Steps { get; private set; }

        /// <summary>
        /// Executes the round's calls in order; a call not allowed in the queue gets the "not allowed" error and is not run.
        /// </summary>
        public async Task<List<ToolResultMessage>> ExecuteAsync(IReadOnlyList<ToolCallRecord> calls, CancellationToken cancellationToken)
        {
            var results = new List<ToolResultMessage>(calls?.Count ?? 0);
            if (calls == null)
                return results;

            foreach (var toolCall in calls)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_queue.Allows(toolCall.FunctionName))
                {
                    results.Add(new ToolResultMessage
                    {
                        ToolCallId = toolCall.CallId,
                        ToolName = toolCall.FunctionName,
                        Result = $"Error: tool '{toolCall.FunctionName}' is not allowed in SubAgent context."
                    });
                    continue;
                }

                _usedTools.Add(toolCall.FunctionName);

                _progress?.Report(new ToolActivityEvent
                {
                    ToolName = toolCall.FunctionName,
                    Step = ++Steps
                });

                var execResult = await _managerResolver()
                    .ExecuteToolAsync(toolCall, cancellationToken, _queue)
                    .ConfigureAwait(false);

                bool succeeded = string.IsNullOrEmpty(execResult.Error);
                if (succeeded)
                    _evidence.Record(toolCall.FunctionName, execResult.Result);

                results.Add(new ToolResultMessage
                {
                    ToolCallId = toolCall.CallId,
                    ToolName = toolCall.FunctionName,
                    Result = succeeded ? execResult.Result : execResult.Error,
                    Error = execResult.Error
                });
            }

            return results;
        }
    }
}
