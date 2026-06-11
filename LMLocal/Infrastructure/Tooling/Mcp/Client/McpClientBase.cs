using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Tooling.Mcp.Abstractions;
using LMLocal.Infrastructure.Tooling.Mcp.Models;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Tooling.Mcp.Client
{
    /// <summary>
    /// Base class for MCP client implementations.
    /// </summary>
    public abstract class McpClientBase : IMcpClient
    {
        private long _requestId = 0;

        protected virtual async Task<JsonRpcResponse> SendRequestAndWaitResponseAsync(
            JsonRpcRequest request,
            CancellationToken cancellationToken)
        {
            var json = request.ToJson();
            var responseJson = await SendJsonAndWaitResponseAsync(json, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(responseJson))
                throw new InvalidOperationException("Empty response from MCP server");

            var response = responseJson.FromJson<JsonRpcResponse>();
            if (response?.IsSuccess == false)
            {
                var errorMsg = response.Error?.Message ?? "Unknown error";
                throw new InvalidOperationException($"MCP error: {errorMsg}");
            }
            return response;
        }

        protected long GetNextRequestId() => Interlocked.Increment(ref _requestId);

        protected abstract Task<string> SendJsonAndWaitResponseAsync(string json, CancellationToken cancellationToken);
        protected abstract Task SendJsonAsync(string json, CancellationToken cancellationToken);
        public abstract Task InitializeAsync(CancellationToken cancellationToken);
        public abstract Task CloseAsync(CancellationToken cancellationToken);

        public async Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken)
        {
            var request = new JsonRpcRequest
            {
                Id = GetNextRequestId(),
                Method = "tools/list",
                Params = null
            };

            var response = await SendRequestAndWaitResponseAsync(request, cancellationToken).ConfigureAwait(false);

            var toolsList = (response.Result as JObject)?.ToObject<ToolsListResponse>();
            var rawTools = toolsList?.Tools ?? Array.Empty<ToolInfo>();

            var definitions = new McpToolDefinition[rawTools.Length];
            for (int i = 0; i < rawTools.Length; i++)
            {
                var t = rawTools[i];
                definitions[i] = new McpToolDefinition
                {
                    Name = t.Name,
                    Description = t.Description,
                    InputSchema = t.InputSchema
                };
            }
            return definitions;
        }

        public async Task<object> CallToolAsync(string toolName, Dictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(toolName))
                throw new ArgumentException("Tool name cannot be empty", nameof(toolName));

            var request = new JsonRpcRequest
            {
                Id = GetNextRequestId(),
                Method = "tools/call",
                Params = new ToolCallRequest
                {
                    ToolName = toolName,
                    Arguments = parameters ?? new Dictionary<string, object>()
                }
            };

            var response = await SendRequestAndWaitResponseAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.Result == null)
                throw new InvalidOperationException("Server returned empty result for tool call.");

            var toolResponse = (response.Result as JObject)?.ToObject<ToolCallResponse>();

            var textContent = toolResponse?.Content?.FirstOrDefault(c => c.Type == "text")?.Text;

            if (toolResponse?.IsError == true)
            {
                var errorMessage = !string.IsNullOrEmpty(textContent)
                    ? $"Tool execution failed: {textContent}"
                    : "Tool execution failed.";
                throw new InvalidOperationException(errorMessage);
            }

            return textContent ?? (object)toolResponse;
        }
    }
}
