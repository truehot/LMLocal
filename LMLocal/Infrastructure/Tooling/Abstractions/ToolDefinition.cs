using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.Tooling
{
    /// <summary>
    /// Defines the access level of a built-in tool.
    /// </summary>
    public enum ToolAccessLevel
    {
        /// <summary>Can read solution files, but cannot modify them.</summary>
        ReadOnly,
        /// <summary>Can run external processes (build, test) but does not modify solution files.</summary>
        Execution,
        /// <summary>Can add, change, delete solution files.</summary>
        FullAccess
    }

    /// <summary>
    /// Represents a tool description that can be exposed to an LLM. Contains the
    /// tool's name, optional description and parameter schema used when calling the tool.
    /// </summary>
    public class ToolDefinition
    {
        /// <summary>
        /// The tool name as it will be referenced by the LLM/system.
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// Optional human-readable description of the tool's purpose.
        /// </summary>
        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }

        /// <summary>
        /// Optional parameters schema describing the arguments that the tool accepts.
        /// </summary>
        [JsonProperty("parameters", NullValueHandling = NullValueHandling.Ignore)]
        public ToolParameters Parameters { get; set; }
    }

    /// <summary>
    /// Describes the parameters object for a tool. Mirrors a simple JSON schema
    /// containing a type, named properties and an optional list of required properties.
    /// </summary>
    public class ToolParameters
    {
        /// <summary>
        /// The JSON Schema type for the parameters object (default: "object").
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; } = "object";

        /// <summary>
        /// A map of parameter names to their details (type/description).
        /// </summary>
        [JsonProperty("properties")]
        public Dictionary<string, ToolDetails> Properties { get; set; }

        /// <summary>
        /// Optional list of property names that are required when invoking the tool.
        /// </summary>
        [JsonProperty("required", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Required { get; set; }
    }

    /// <summary>
    /// Describes a single parameter/property: its JSON type and an optional description.
    /// </summary>
    public class ToolDetails
    {
        /// <summary>
        /// The JSON Schema type for the parameter (e.g. "string", "integer").
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>
        /// Human-readable description of the parameter.
        /// </summary>
        [JsonProperty("description")]
        public string Description { get; set; }
    }
}