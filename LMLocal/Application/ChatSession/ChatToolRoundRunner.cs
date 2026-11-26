using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.SubAgents;
using LMLocal.Application.Tool;
using LMLocal.Application.Usage;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using LMLocal.Infrastructure.WebView;
using LMLocal.Infrastructure.WebView.Messaging;

namespace LMLocal.Application.ChatSession
{
    /// <summary>
    /// Runs the tool calls of one round of the main chat: emits StreamToolCall and StreamToolEnd per call, runs the
    /// parallel-eligible SubAgent calls as one group, and sends the snapshot of the changed files afterwards.
    /// </summary>
    internal sealed class ChatToolRoundRunner
    {
        /// <summary>Fallback of a tool that names no timeout of its own.</summary>
        private const int ToolExecutionTimeoutMs = 30000;

        private readonly IToolExecutionManager _toolManager;
        private readonly ISnapshotManager _snapshotManager;
        private readonly ISubAgentsParallelPolicy _parallelPolicy;
        private readonly ISubAgentsParallelRunner _parallelRunner;

        public ChatToolRoundRunner(
            IToolExecutionManager toolManager,
            ISnapshotManager snapshotManager,
            ISubAgentsParallelPolicy parallelPolicy,
            ISubAgentsParallelRunner parallelRunner)
        {
            _toolManager = toolManager ?? throw new ArgumentNullException(nameof(toolManager));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
            _parallelPolicy = parallelPolicy ?? throw new ArgumentNullException(nameof(parallelPolicy));
            _parallelRunner = parallelRunner ?? throw new ArgumentNullException(nameof(parallelRunner));
        }

        /// <summary>
        /// Executes <paramref name="calls"/> and returns their results in the same order, so the model gets the answer to
        /// every call it asked for. A round without calls runs nothing and returns nothing.
        /// </summary>
        public async Task<IReadOnlyList<ToolResultMessage>> RunAsync(
            IReadOnlyList<ToolCallRecord> calls,
            UsageCallContext usageContext,
            Func<WebView2ScriptMessage, Task> onMessage,
            CancellationToken ct)
        {
            var results = new List<ToolResultMessage>(calls?.Count ?? 0);
            if (calls == null || calls.Count == 0)
                return results;

            await _snapshotManager.BeginBatchAsync(ct).ConfigureAwait(false);
            try
            {
                var parallelIdx = new List<int>();
                var sequentialIdx = new List<int>();

                for (int i = 0; i < calls.Count; i++)
                {
                    var call = calls[i];
                    if (call != null && _parallelPolicy.IsParallelEnabled(call.FunctionName))
                        parallelIdx.Add(i);
                    else
                        sequentialIdx.Add(i);
                }

                var ordered = new ToolResultMessage[calls.Count];

                if (parallelIdx.Count > 1)
                {
                    var parallelCalls = new List<ToolCallRecord>(parallelIdx.Count);
                    foreach (var i in parallelIdx)
                        parallelCalls.Add(calls[i]);

                    var groupMaxParallel = parallelCalls
                        .Select(c => _parallelPolicy.ResolveMaxParallel(c.FunctionName))
                        .DefaultIfEmpty(SubAgentsConfig.DefaultMaxParallel)
                        .Min();
                    if (groupMaxParallel < 1)
                        groupMaxParallel = SubAgentsConfig.DefaultMaxParallel;

                    InternalLogger.Info($"ChatToolRoundRunner: Running {parallelCalls.Count} SubAgents in parallel (max {groupMaxParallel} concurrently).");

                    string groupId = Guid.NewGuid().ToString("N");
                    var groupCallIds = parallelCalls.Select(c => c.CallId).ToList();
                    int groupTotal = parallelCalls.Count;
                    int groupCompleted = 0;

                    await onMessage(new WebView2ToolGroupMessage
                    {
                        Type = WebView2MessageType.StreamToolGroupStart,
                        GroupId = groupId,
                        CallIds = groupCallIds,
                        Total = groupTotal,
                        Completed = 0
                    }).ConfigureAwait(false);

                    var groupResult = await _parallelRunner.RunAsync(
                        parallelCalls,
                        async (call, callCt) =>
                        {
                            var callResult = await RunToolCallAsync(call, onMessage, callCt, usageContext).ConfigureAwait(false);

                            int completed = Interlocked.Increment(ref groupCompleted);
                            await onMessage(new WebView2ToolGroupMessage
                            {
                                Type = WebView2MessageType.StreamToolGroupProgress,
                                GroupId = groupId,
                                CallIds = groupCallIds,
                                Total = groupTotal,
                                Completed = completed
                            }).ConfigureAwait(false);

                            return callResult;
                        },
                        groupMaxParallel,
                        ct).ConfigureAwait(false);

                    await onMessage(new WebView2ToolGroupMessage
                    {
                        Type = WebView2MessageType.StreamToolGroupEnd,
                        GroupId = groupId,
                        CallIds = groupCallIds,
                        Total = groupTotal,
                        Completed = groupTotal
                    }).ConfigureAwait(false);

                    for (int j = 0; j < parallelCalls.Count; j++)
                    {
                        ordered[parallelIdx[j]] = groupResult.Results[j].ToMessage(parallelCalls[j]);
                    }
                }
                else if (parallelIdx.Count == 1)
                {
                    // A single agent has nothing to overlap with; run it inline.
                    ct.ThrowIfCancellationRequested();
                    var call = calls[parallelIdx[0]];
                    var result = await RunToolCallAsync(call, onMessage, ct, usageContext).ConfigureAwait(false);
                    ordered[parallelIdx[0]] = result.ToMessage(call);
                }

                foreach (var i in sequentialIdx)
                {
                    ct.ThrowIfCancellationRequested();
                    var call = calls[i];
                    var result = await RunToolCallAsync(call, onMessage, ct, usageContext).ConfigureAwait(false);
                    ordered[i] = result.ToMessage(call);
                }

                foreach (var result in ordered)
                {
                    if (result != null)
                        results.Add(result);
                }
            }
            finally
            {
                try
                {
                    await _snapshotManager.EndBatchAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception batchEx)
                {
                    InternalLogger.Error("ChatToolRoundRunner: snapshot batch cleanup failed", batchEx);
                }
            }

            await SendSnapshotUpdateAsync(onMessage).ConfigureAwait(false);

            return results;
        }

        /// <summary>
        /// Runs one tool call: emits StreamToolCall, executes with a per-call timeout while forwarding progress steps, then emits StreamToolEnd.
        /// </summary>
        private async Task<ToolExecutionResult> RunToolCallAsync(
            ToolCallRecord toolCall,
            Func<WebView2ScriptMessage, Task> onMessage,
            CancellationToken ct,
            UsageCallContext usageContext)
        {
            var processingMessage = _toolManager.GetProcessingMessage(toolCall);

            var uiCallId = "ui_" + Guid.NewGuid().ToString("N");

            await onMessage(new WebView2ToolCallMessage
            {
                Type = WebView2MessageType.StreamToolCall,
                FunctionName = toolCall.FunctionName,
                CallId = uiCallId,
                ArgumentsJson = toolCall.ArgumentsJson,
                Message = processingMessage
            }).ConfigureAwait(false);

            ToolExecutionResult toolResult;
            using (var toolCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var toolTimeout = _toolManager.GetToolTimeout(toolCall.FunctionName) ?? TimeSpan.FromMilliseconds(ToolExecutionTimeoutMs);
                toolCts.CancelAfter(toolTimeout);

                var stepProgress = new ToolActivityForwarder(uiCallId, onMessage);
                toolResult = await _toolManager.ExecuteToolAsync(toolCall, toolCts.Token, stepProgress, null, usageContext).ConfigureAwait(false);

                if (toolCts.IsCancellationRequested && !ct.IsCancellationRequested && !toolResult.IsSuccess)
                {
                    var timeoutText = $"Tool '{toolCall.FunctionName}' timed out after {(int)toolTimeout.TotalSeconds} s.";
                    toolResult.Error = timeoutText;
                    toolResult.Result = null;
                    toolResult.UserMessage = timeoutText;
                }
            }

            ct.ThrowIfCancellationRequested();

            await onMessage(new WebView2ToolCallMessage
            {
                Type = WebView2MessageType.StreamToolEnd,
                FunctionName = toolCall.FunctionName,
                CallId = uiCallId,
                Message = string.IsNullOrEmpty(toolResult.Error) ? toolResult.CompletionMessage : toolResult.UserMessage,
                IsError = !string.IsNullOrEmpty(toolResult.Error),
                UiData = toolResult.UiData
            }).ConfigureAwait(false);

            return toolResult;
        }

        private async Task SendSnapshotUpdateAsync(Func<WebView2ScriptMessage, Task> onMessage)
        {
            try
            {
                var changedFiles = await _snapshotManager.GetChangedFilesWithStatusAsync().ConfigureAwait(false);

                var message = new WebView2SnapshotMessage
                {
                    ChangedFiles = changedFiles.ToList(),
                };

                await onMessage(message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("ChatToolRoundRunner: the snapshot update failed", ex);
            }
        }
    }
}
