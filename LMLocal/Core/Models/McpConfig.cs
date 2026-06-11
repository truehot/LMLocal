using System;
using System.Collections.Generic;
using LMLocal.Core.Common;
using Newtonsoft.Json;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Root MCP configuration file wrapper with enable flag and servers.
    /// </summary>
    public class McpConfigFile : IEquatable<McpConfigFile>
    {
        /// <summary>
        /// Whether MCP (Model Context Protocol) is enabled.
        /// </summary>
        [JsonProperty("EnableMcp")]
        public bool EnableMcp { get; set; } = false;

        /// <summary>
        /// Serialized MCP servers configuration as JSON string.
        /// </summary>
        [JsonProperty("McpServersJson")]
        public string McpServersJson { get; set; }

        /// <summary>
        /// Gets the MCP servers configuration.
        /// Parses McpServersJson if it's provided.
        /// </summary>
        public McpClientConfig GetServersConfig()
        {
            if (string.IsNullOrWhiteSpace(McpServersJson))
                return new McpClientConfig();

            try
            {
                var config = McpServersJson.FromJson<McpClientConfig>();
                return config ?? new McpClientConfig();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("McpConfigFile.GetServersConfig: Failed to parse McpServersJson", ex);
                throw;
            }
        }

        public bool Equals(McpConfigFile other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;
            return EnableMcp == other.EnableMcp && Equals(McpServersJson, other.McpServersJson);
        }

        public override bool Equals(object obj) => Equals(obj as McpConfigFile);

        public override int GetHashCode()
        {
            unchecked
            {
                return (EnableMcp.GetHashCode() * 397) ^ (McpServersJson?.GetHashCode() ?? 0);
            }
        }
    }

    /// <summary>
    /// Root configuration for MCP (Model Context Protocol) servers.
    /// </summary>
    public class McpClientConfig : IEquatable<McpClientConfig>
    {
        [JsonProperty("mcpServers")]
        public Dictionary<string, McpServerConfig> Servers { get; set; } = new Dictionary<string, McpServerConfig>();

        // Accept alternative property name "servers" in incoming JSON
        [JsonProperty("servers")]
        private Dictionary<string, McpServerConfig> ServersAlias
        {
            set
            {
                if (value != null)
                {
                    Servers = value;
                }
            }
        }

        public bool Equals(McpClientConfig other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;

            if ((Servers == null && other.Servers != null) || (Servers != null && other.Servers == null))
                return false;

            if (Servers == null && other.Servers == null)
                return true;

            if (Servers.Count != other.Servers.Count)
                return false;

            foreach (var kvp in Servers)
            {
                if (!other.Servers.TryGetValue(kvp.Key, out var otherValue))
                    return false;
                if (!kvp.Value.Equals(otherValue))
                    return false;
            }

            return true;
        }

        public override bool Equals(object obj) => Equals(obj as McpClientConfig);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                if (Servers != null)
                {
                    foreach (var kvp in Servers)
                    {
                        hash = hash * 23 + kvp.Key.GetHashCode();
                        hash = hash * 23 + kvp.Value.GetHashCode();
                    }
                }
                return hash;
            }
        }

        private static bool DictionaryEquals<TKey, TValue>(Dictionary<TKey, TValue> a, Dictionary<TKey, TValue> b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;

            foreach (var kvp in a)
            {
                if (!b.TryGetValue(kvp.Key, out var bValue))
                    return false;
                if (!kvp.Value.Equals(bValue))
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Configuration for a single MCP server.
    /// </summary>
    public class McpServerConfig : IEquatable<McpServerConfig>
    {
        /// <summary>
        /// Transport type: "stdio", "http", or "streamable-http", or auto-detected based on other fields.
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// Command to execute (for stdio transport).
        /// </summary>
        [JsonProperty("command")]
        public string Command { get; set; }

        /// <summary>
        /// Arguments for the command (for stdio transport).
        /// </summary>
        [JsonProperty("args")]
        public List<string> Args { get; set; }

        /// <summary>
        /// Environment variables for the command (for stdio transport).
        /// </summary>
        [JsonProperty("env")]
        public Dictionary<string, string> Env { get; set; }

        /// <summary>
        /// Server URL (for http/streamable-http transport).
        /// </summary>
        [JsonProperty("url")]
        public string Url { get; set; }

        /// <summary>
        /// HTTP headers for requests (for http/streamable-http transport).
        /// </summary>
        [JsonProperty("headers")]
        public Dictionary<string, string> Headers { get; set; }

        /// <summary>
        /// Authentication method: "bearer", "basic", or custom.
        /// </summary>
        [JsonProperty("auth")]
        public string Auth { get; set; }

        /// <summary>
        /// Bearer token for authentication.
        /// </summary>
        [JsonProperty("token")]
        public string Token { get; set; }

        /// <summary>
        /// Whether this server is disabled. Disabled servers won't be initialized and their tools won't be available.
        /// </summary>
        [JsonProperty("disabled")]
        public bool Disabled { get; set; } = false;

        /// <summary>
        /// Optional tool permissions for this server. Key is tool name, value is permission (e.g. "disable").
        /// Disabled tools won't be available to the AI model.
        /// </summary>
        [JsonProperty("permissions", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> Permissions { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Resolves the transport type based on configuration.
        /// </summary>
        public string ResolveTransportType()
        {
            if (!string.IsNullOrEmpty(Type))
                return Type;
            if (!string.IsNullOrEmpty(Command))
                return "stdio";
            if (!string.IsNullOrEmpty(Url))
                return "http";
            return "stdio";
        }

        /// <summary>
        /// Validates the server configuration.
        /// </summary>
        public string Validate()
        {
            var transportType = ResolveTransportType();

            switch (transportType.ToLowerInvariant())
            {
                case "stdio":
                    if (string.IsNullOrWhiteSpace(Command))
                        return "stdio transport requires 'command' field";
                    break;

                case "http":
                case "streamable-http":
                    if (string.IsNullOrWhiteSpace(Url))
                        return $"{transportType} transport requires 'url' field";
                    if (!Uri.TryCreate(Url, UriKind.Absolute, out _))
                        return $"'{Url}' is not a valid URL";
                    break;

                default:
                    return $"Unknown transport type: {transportType}";
            }

            return null; // Valid
        }

        public bool Equals(McpServerConfig other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;

            return string.Equals(Type, other.Type, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Command, other.Command, StringComparison.Ordinal)
                && string.Equals(Url, other.Url, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Auth, other.Auth, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Token, other.Token, StringComparison.Ordinal)
                && Disabled == other.Disabled
                && DictionaryEquals(Args, other.Args)
                && DictionaryEquals(Env, other.Env)
                && DictionaryEquals(Headers, other.Headers)
                && DictionaryEquals(Permissions, other.Permissions);
        }

        public override bool Equals(object obj) => Equals(obj as McpServerConfig);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + (Type != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(Type) : 0);
                hash = hash * 23 + (Command != null ? Command.GetHashCode() : 0);
                hash = hash * 23 + (Url != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(Url) : 0);
                hash = hash * 23 + (Auth != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(Auth) : 0);
                hash = hash * 23 + (Token != null ? Token.GetHashCode() : 0);
                hash = hash * 23 + Disabled.GetHashCode();
                if (Permissions != null)
                {
                    foreach (var kvp in Permissions)
                    {
                        hash = hash * 23 + kvp.Key.GetHashCode();
                        hash = hash * 23 + kvp.Value.GetHashCode();
                    }
                }
                return hash;
            }
        }

        private static bool DictionaryEquals<T>(ICollection<T> a, ICollection<T> b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            return a.Count == b.Count; 
        }
    }
}
