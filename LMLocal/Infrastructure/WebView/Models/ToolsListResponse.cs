using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.WebView.Models
{
    /// <summary>
    /// Tool information response for UI.
    /// </summary>
    public class ToolResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("enabled")]
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// Tools list response wrapper.
    /// </summary>
    public class ToolsListResponse
    {
        [JsonProperty("tools")]
        public List<ToolResponse> Tools { get; set; } = new List<ToolResponse>();
    }
}
