using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.LlmApi.Responses
{
    /// <summary>
    /// OpenAI-compatible models list response format.
    /// Used by OpenAI and OpenAI-compatible backends.
    /// </summary>
    internal class ListModelsResponse
    {
        [JsonProperty("object")]
        public string Object { get; set; }

        [JsonProperty("data")]
        public List<OpenAiModelInfo> Data { get; set; } 
    }

    /// <summary>
    /// Model info in OpenAI format.
    /// </summary>
    internal class OpenAiModelInfo
    {
        [JsonProperty("id")]
        public string Id { get; set; } 

        [JsonProperty("object")]
        public string Object { get; set; } 

        [JsonProperty("created")]
        public long Created { get; set; }

        [JsonProperty("owned_by")]
        public string OwnedBy { get; set; } 

        [JsonProperty("parent", NullValueHandling = NullValueHandling.Ignore)]
        public string Parent { get; set; }
    }
}


