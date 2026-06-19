using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Root configuration file wrapper for built-in tools enabled/disabled states.
    /// </summary>
    public class ToolsConfigFile : IEquatable<ToolsConfigFile>
    {
        /// <summary>
        /// List of tool configurations with their enabled states.
        /// </summary>
        [JsonProperty("tools")]
        public List<ToolConfig> Tools { get; set; } = new List<ToolConfig>();

        public bool Equals(ToolsConfigFile other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;

            if ((Tools == null && other.Tools != null) || (Tools != null && other.Tools == null))
                return false;

            if (Tools == null && other.Tools == null)
                return true;

            if (Tools.Count != other.Tools.Count)
                return false;

            for (int i = 0; i < Tools.Count; i++)
            {
                if (!Equals(Tools[i], other.Tools[i]))
                    return false;
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ToolsConfigFile);
        }

        public override int GetHashCode()
        {
            return Tools?.GetHashCode() ?? 0;
        }
    }

    /// <summary>
    /// Configuration for a single tool.
    /// </summary>
    public class ToolConfig : IEquatable<ToolConfig>
    {
        /// <summary>
        /// Unique identifier for the tool (e.g., "list-directory-contents").
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>
        /// Whether the tool is enabled.
        /// </summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        public bool Equals(ToolConfig other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;

            return Id == other.Id && Enabled == other.Enabled;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ToolConfig);
        }

        public override int GetHashCode()
        {
            return (Id?.GetHashCode() ?? 0) ^ Enabled.GetHashCode();
        }
    }
}
