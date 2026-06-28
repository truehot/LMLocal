using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.LlmApi.Responses
{
    /// <summary>
    /// llama.cpp-specific /v1/models response.
    /// </summary>
    internal class LlamaCppListModelsResponse
    {
        [JsonProperty("data")]
        public List<LlamaCppModelData> Data { get; set; }
    }

    /// <summary>
    /// Model entry in llama.cpp /v1/models data array.
    /// </summary>
    internal class LlamaCppModelData
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("object")]
        public string Object { get; set; }

        [JsonProperty("created")]
        public long Created { get; set; }

        [JsonProperty("owned_by")]
        public string OwnedBy { get; set; }

        [JsonProperty("meta")]
        public LlamaCppModelMeta Meta { get; set; }
    }

    /// <summary>
    /// Metadata for a llama.cpp model (context length, parameter count, size on disk).
    /// </summary>
    internal class LlamaCppModelMeta
    {
        [JsonProperty("n_ctx")]
        public int NContext { get; set; }

        [JsonProperty("n_ctx_train")]
        public int NContextTrain { get; set; }

        [JsonProperty("n_embd")]
        public int NEmbed { get; set; }

        [JsonProperty("n_params")]
        public long NParams { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }
    }
}
