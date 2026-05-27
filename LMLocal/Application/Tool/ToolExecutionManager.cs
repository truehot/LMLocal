using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling;
using Newtonsoft.Json.Linq;

namespace LMLocal.Services.Tool
{
    /// <summary>
    /// Manages tool execution with VS-specific tools support.
    /// Integrates with VsToolFactory for Visual Studio tool execution.
    /// Handles argument parsing, execution, error handling, and timeouts.
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
        private readonly ICompositeToolFactory _vsToolFactory;
        private readonly IServiceProvider _serviceProvider;

        public ToolExecutionManager(ICompositeToolFactory vsToolFactory, IServiceProvider serviceProvider)
        {
            _vsToolFactory = vsToolFactory ?? throw new ArgumentNullException(nameof(vsToolFactory));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
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

                if (!_vsToolFactory.ToolExists(toolCall.FunctionName))
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
                var result = await _vsToolFactory.ExecuteAsync(
                    toolCall.FunctionName,
                    _serviceProvider,
                    parameters,
                    ct).ConfigureAwait(false);

                var completionMessage = _vsToolFactory.GetCompletionMessage(toolCall.FunctionName, result);

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

            return _vsToolFactory.GetProcessingMessage(toolCall.FunctionName, parameters);
        }
    }
}
