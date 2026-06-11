using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling.Mcp.Models
{
    /// <summary>
    /// MCP JSON-RPC 2.0 request envelope.
    /// </summary>
    public class JsonRpcRequest
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("id")]
        public object Id { get; set; }

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params", NullValueHandling = NullValueHandling.Ignore)]
        public object Params { get; set; }
    }

    /// <summary>
    /// MCP JSON-RPC 2.0 response envelope.
    /// </summary>
    public class JsonRpcResponse
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; }

        [JsonProperty("id")]
        public object Id { get; set; }

        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
        public object Result { get; set; }

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public JsonRpcError Error { get; set; }

        public bool IsSuccess => Error == null;
    }

    /// <summary>
    /// MCP JSON-RPC error information.
    /// </summary>
    public class JsonRpcError
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public object Data { get; set; }
    }

    /// <summary>
    /// MCP initialize request parameters.
    /// </summary>
    public class InitializeRequest
    {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; } = "2025-11-25";

        [JsonProperty("capabilities")]
        public CapabilitiesObject Capabilities { get; set; } = new CapabilitiesObject();

        [JsonProperty("clientInfo")]
        public ClientInfo ClientInfo { get; set; } = new ClientInfo
        {
            Name = "LMLocal",
            Version = "1.0.0"
        };
    }

    /// <summary>
    /// Client capabilities declaration.
    /// </summary>
    public class CapabilitiesObject
    {
        [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)]
        public ToolCapabilities Tools { get; set; }
    }

    /// <summary>
    /// Tool capabilities.
    /// </summary>
    public class ToolCapabilities
    {
    }

    /// <summary>
    /// Client information sent to server.
    /// </summary>
    public class ClientInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    /// <summary>
    /// MCP initialize response result.
    /// </summary>
    public class InitializeResponse
    {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; }

        [JsonProperty("capabilities")]
        public CapabilitiesObject Capabilities { get; set; }

        [JsonProperty("serverInfo")]
        public ServerInfo ServerInfo { get; set; }
    }

    /// <summary>
    /// Server information from initialize response.
    /// </summary>
    public class ServerInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    /// <summary>
    /// MCP tools/list response.
    /// </summary>
    public class ToolsListResponse
    {
        [JsonProperty("tools")]
        public ToolInfo[] Tools { get; set; } = System.Array.Empty<ToolInfo>();

        [JsonProperty("nextCursor", NullValueHandling = NullValueHandling.Ignore)]
        public string NextCursor { get; set; }
    }

    /// <summary>
    /// Individual tool information from server.
    /// </summary>
    public class ToolInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("inputSchema")]
        public object InputSchema { get; set; }
    }

    /// <summary>
    /// MCP tools/call request parameters.
    /// </summary>
    public class ToolCallRequest
    {
        [JsonProperty("name")]
        public string ToolName { get; set; }

        [JsonProperty("arguments")]
        public object Arguments { get; set; }
    }

    /// <summary>
    /// MCP tools/call response result.
    /// </summary>
    public class ToolCallResponse
    {
        [JsonProperty("content")]
        public ContentBlock[] Content { get; set; }

        [JsonProperty("isError")]
        public bool IsError { get; set; }
    }

    /// <summary>
    /// Content block in tool response.
    /// </summary>
    public class ContentBlock
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }
    }
}
