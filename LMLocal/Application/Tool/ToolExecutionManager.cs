using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling;
using Newtonsoft.Json.Linq;

namespace LMLocal.Services.Tool
{
    /// <summary>
    /// Manages tool execution support.
    /// </summary>
    internal interface IToolExecutionManager
    {
        /// <summary>
        /// Executes a tool call and returns the result.
        /// </summary>
        Task<ToolExecutionResult> ExecuteToolAsync(ToolCallRecord toolCall, CancellationToken ct);

        /// <summary>
        /// Gets processing message for a tool based on its tool call (parses parameters internally).
        /// </summary>
        string GetProcessingMessage(ToolCallRecord toolCall);
    }


    internal class ToolExecutionManager : IToolExecutionManager
    {
        private readonly ICompositeToolFactory _compositeToolFactory;

        public ToolExecutionManager(ICompositeToolFactory compositeToolFactory)
        {
            _compositeToolFactory = compositeToolFactory ?? throw new ArgumentNullException(nameof(compositeToolFactory));
        }

        public async Task<ToolExecutionResult> ExecuteToolAsync(ToolCallRecord toolCall, CancellationToken ct)
        {
            if (toolCall == null)
            {
                return new ToolExecutionResult
                {
                    Error = "Tool call is null"
                };
            }

            InternalLogger.Info($"ToolExecutionManager.ExecuteToolAsync: {toolCall.FunctionName} (id: {toolCall.CallId})");

            try
            {
                Dictionary<string, object> parameters = new Dictionary<string, object>();

                if (!string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
                {
                    var jsonObj = JObject.Parse(toolCall.ArgumentsJson);
                    parameters = jsonObj.ToObject<Dictionary<string, object>>();
                }

                if (!_compositeToolFactory.ToolExists(toolCall.FunctionName))
                {
                    var errorMsg = $"Tool '{toolCall.FunctionName}' not found";
                    InternalLogger.Warn($"ToolExecutionManager: {errorMsg}");
                    return new ToolExecutionResult
                    {
                        ToolId = toolCall.CallId,
                        ToolName = toolCall.FunctionName,
                        Error = errorMsg
                    };
                }

                InternalLogger.Info($"ToolExecutionManager: Executing {toolCall.FunctionName}");
                var result = await _compositeToolFactory.ExecuteAsync(
                    toolCall.FunctionName,
                    parameters,
                    ct).ConfigureAwait(false);

                var completionMessage = _compositeToolFactory.GetCompletionMessage(toolCall.FunctionName, result);

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

            Dictionary<string, object> parameters = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
            {
                try
                {
                    var jsonObj = JObject.Parse(toolCall.ArgumentsJson);
                    parameters = jsonObj.ToObject<Dictionary<string, object>>();
                }
                catch
                {
                    return "Processing...";
                }
            }

            return _compositeToolFactory.GetProcessingMessage(toolCall.FunctionName, parameters);
        }
    }
}
