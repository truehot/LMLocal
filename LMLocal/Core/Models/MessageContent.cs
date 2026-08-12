using Newtonsoft.Json;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// A single content part of a multimodal message (text or image_url).
    /// </summary>
    public class ContentPart
    {
        [JsonProperty("type")]
        public string Type { get; set; } // "text", "image_url", "input_audio"

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }

        [JsonProperty("image_url", NullValueHandling = NullValueHandling.Ignore)]
        public ImageUrlInfo ImageUrl { get; set; }
    }

    /// <summary>
    /// image_url payload for multimodal content parts.
    /// </summary>
    public class ImageUrlInfo
    {
        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("detail", NullValueHandling = NullValueHandling.Ignore)]
        public string Detail { get; set; } // "auto", "low", "high"
    }
}
