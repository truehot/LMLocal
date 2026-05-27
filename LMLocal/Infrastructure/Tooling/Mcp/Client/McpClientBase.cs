using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Common;
using LMLocal.Infrastructure.Tooling.Mcp.Models;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Mcp
{
    /// <summary>
    /// Base class for MCP client implementations.
    /// </summary>
    public abstract class McpClientBase : IMcpClient
    {
        private long _requestId = 0;
        private readonly object _requestIdLock = new object();

        protected virtual async Task SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
        {
            var json = JsonConvert.SerializeObject(request);
            await SendJsonAsync(json, cancellationToken).ConfigureAwait(false);
        }

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

        protected long GetNextRequestId()
        {
            lock (_requestIdLock)
            {
                return ++_requestId;
            }
        }

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

            var resultJson = response.Result.ToJson();
            var toolsList = resultJson.FromJson<ToolsListResponse>();

            var definitions = (toolsList?.Tools ?? Array.Empty<ToolInfo>())
                .Select(t => new McpToolDefinition
                {
                    Name = t.Name,
                    Description = t.Description,
                    InputSchema = t.InputSchema
                })
                .ToList();

            return definitions.AsReadOnly();
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

            var resultJson = response.Result.ToJson();
            var toolResponse = resultJson.FromJson<ToolCallResponse>();

            if (toolResponse?.IsError == true)
                throw new InvalidOperationException($"Tool execution failed");

            var textContent = toolResponse?.Content
                ?.FirstOrDefault(c => c.Type == "text")
                ?.Text;

            return textContent ?? (object)toolResponse;
        }
    }
}
