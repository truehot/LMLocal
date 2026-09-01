using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling;
using Newtonsoft.Json.Linq;

namespace LMLocal.Application.Tool
{
    /// <summary>
    /// Manages tool execution support.
    /// </summary>
    internal interface IToolExecutionManager
    {
        /// <summary>
        /// Executes a tool call in the main chat context.
        /// </summary>
        Task<ToolExecutionResult> ExecuteToolAsync(ToolCallRecord toolCall, CancellationToken ct);

        /// <summary>
        /// Executes a tool call against an explicit queue.
        /// </summary>
        Task<ToolExecutionResult> ExecuteToolAsync(ToolCallRecord toolCall, CancellationToken ct, ToolQueue queue);

        /// <summary>
        /// Gets processing message for a tool based on its tool call.
        /// </summary>
        string GetProcessingMessage(ToolCallRecord toolCall);

        /// <summary>
        /// Returns the maximum execution time for a tool.
        /// </summary>
        TimeSpan? GetToolTimeout(string toolName);
    }


    internal class ToolExecutionManager : IToolExecutionManager
    {
        private readonly IToolRouter _toolRouter;
        private readonly IToolQueueProvider _toolQueueProvider;

        public ToolExecutionManager(
            IToolRouter toolRouter,
            IToolQueueProvider toolQueueProvider)
        {
            _toolRouter = toolRouter ?? throw new ArgumentNullException(nameof(toolRouter));
            _toolQueueProvider = toolQueueProvider ?? throw new ArgumentNullException(nameof(toolQueueProvider));
        }

        public Task<ToolExecutionResult> ExecuteToolAsync(ToolCallRecord toolCall, CancellationToken ct)
        {
            var queue = _toolQueueProvider.GetMainQueue();
            return ExecuteToolAsync(toolCall, ct, queue);
        }

        public async Task<ToolExecutionResult> ExecuteToolAsync(ToolCallRecord toolCall, CancellationToken ct, ToolQueue queue)
        {
            if (toolCall == null)
            {
                return new ToolExecutionResult
                {
                    Error = "Tool call is null"
                };
            }

            InternalLogger.Info($"ToolExecutionManager.ExecuteToolAsync: {toolCall.FunctionName} (id: {toolCall.CallId})");

            if (toolCall.IsInvalid)
            {
                var userMsg = $"Tool '{toolCall.FunctionName}' call '{toolCall.CallId}': arguments are not valid JSON, tool was not executed.";
                var errorMsg = userMsg + " Do not repeat the same full request; split large changes into smaller patches/chunks.";
                InternalLogger.Warn($"[ToolExecutionManager] Skipping {toolCall.FunctionName} (id: {toolCall.CallId}): invalid tool arguments.");
                return new ToolExecutionResult
                {
                    ToolId = toolCall.CallId,
                    ToolName = toolCall.FunctionName,
                    Error = errorMsg,
                    UserMessage = userMsg
                };
            }

            try
            {
                Dictionary<string, object> parameters = new Dictionary<string, object>();

                if (!string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
                {
                    var jsonObj = JObject.Parse(toolCall.ArgumentsJson);
                    parameters = jsonObj.ToObject<Dictionary<string, object>>();
                }

                if (queue == null || !queue.Allows(toolCall.FunctionName))
                {
                    var errorMsg = $"Tool '{toolCall.FunctionName}' not found or not allowed in the current context";
                    InternalLogger.Warn($"ToolExecutionManager: {errorMsg}");
                    return new ToolExecutionResult
                    {
                        ToolId = toolCall.CallId,
                        ToolName = toolCall.FunctionName,
                        Error = errorMsg
                    };
                }

                InternalLogger.Info($"ToolExecutionManager: Executing {toolCall.FunctionName}");
                var result = await _toolRouter.ExecuteAsync(
                    toolCall.FunctionName,
                    parameters,
                    ct).ConfigureAwait(false);

                var completionMessage = _toolRouter.GetCompletionMessage(toolCall.FunctionName, result);

                InternalLogger.Info($"ToolExecutionManager: {toolCall.FunctionName} completed successfully");
                return new ToolExecutionResult
                {
                    ToolId = toolCall.CallId,
                    ToolName = toolCall.FunctionName,
                    Result = result,
                    CompletionMessage = completionMessage
                };
            }
            catch (OperationCanceledException)
            {
                var errorMsg = $"Tool execution cancelled: {toolCall.FunctionName}";
                InternalLogger.Warn($"ToolExecutionManager: {errorMsg}");
                return new ToolExecutionResult
                {
                    ToolId = toolCall.CallId,
                    ToolName = toolCall.FunctionName,
                    Error = errorMsg
                };
            }
            catch (ArgumentException ex)
            {
                InternalLogger.Warn($"ToolExecutionManager: Argument error in {toolCall.FunctionName}: {ex.Message}");
                return new ToolExecutionResult
                {
                    ToolId = toolCall.CallId,
                    ToolName = toolCall.FunctionName,
                    Error = $"Invalid parameters: {ex.Message}"
                };
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"ToolExecutionManager: Error executing {toolCall.FunctionName}", ex);
                return new ToolExecutionResult
                {
                    ToolId = toolCall.CallId,
                    ToolName = toolCall.FunctionName,
                    Error = $"Execution error: {ex.Message}"
                };
            }
        }

        public string GetProcessingMessage(ToolCallRecord toolCall)
        {
            if (toolCall == null)
                return "Processing...";

            if (toolCall.IsInvalid)
                return "Invalid tool arguments";

            if (string.IsNullOrWhiteSpace(toolCall.FunctionName))
                return "Processing...";

            Dictionary<string, object> parameters = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
            {
                try
                {
                    var jsonObj = JObject.Parse(toolCall.ArgumentsJson);
                    parameters = jsonObj.ToObject<Dictionary<string, object>>();
                }
                catch (Exception)
                {
                    return "Processing...";
                }
            }

            return _toolRouter.GetProcessingMessage(toolCall.FunctionName, parameters) ?? "Processing...";
        }

        public TimeSpan? GetToolTimeout(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return null;

            return _toolRouter.GetToolTimeout(toolName);
        }
    }
}
