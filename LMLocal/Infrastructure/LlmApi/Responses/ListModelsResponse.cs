using LMLocal.Core.Common;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LMLocal.Infrastructure.LlmApi.Responses
{
    /// <summary>
    /// OpenAI-compatible models list response format.
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

        /// <summary>
        /// Human-readable model name (OpenRouter: "name").
        /// </summary>
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        /// <summary>
        /// Human-readable model name (Gemini OpenAI-compatible: "displayName").
        /// </summary>
        [JsonProperty("displayName", NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayName { get; set; }

        /// <summary>
        /// Snake_case alias for providers that emit "display_name" instead of "displayName".
        /// </summary>
        [JsonProperty("display_name", NullValueHandling = NullValueHandling.Ignore)]
        public string DisplayNameSnakeCase { get; set; }

        /// <summary>
        /// Maximum context length in tokens (OpenRouter: "context_length").
        /// </summary>
        [JsonProperty("context_length", NullValueHandling = NullValueHandling.Ignore)]
        public long? ContextLength { get; set; }

        /// <summary>
        /// Alias for maximum context length in tokens ("context_window").
        /// </summary>
        [JsonProperty("context_window", NullValueHandling = NullValueHandling.Ignore)]
        public long? ContextWindow { get; set; }

        /// <summary>
        /// Alias for maximum context length in tokens ("max_context_length").
        /// </summary>
        [JsonProperty("max_context_length", NullValueHandling = NullValueHandling.Ignore)]
        public long? MaxContextLength { get; set; }

        /// <summary>
        /// Alias for maximum context length in tokens ("max_model_len", used by vLLM-style endpoints).
        /// </summary>
        [JsonProperty("max_model_len", NullValueHandling = NullValueHandling.Ignore)]
        public long? MaxModelLen { get; set; }

        /// <summary>
        /// Per-token pricing.
        /// </summary>
        [JsonProperty("pricing", NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(OpenAiPricingConverter))]
        public OpenAiPricing Pricing { get; set; }

        /// <summary>
        /// Model architecture / modality info .
        /// </summary>
        [JsonProperty("architecture", NullValueHandling = NullValueHandling.Ignore)]
        public OpenAiArchitecture Architecture { get; set; }

        /// <summary>
        /// List of parameters the model supports.
        /// </summary>
        [JsonProperty("supported_parameters", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> SupportedParameters { get; set; }
    }

    /// <summary>
    /// Per-token pricing reported by OpenAI-compatible providers.
    /// </summary>
    internal class OpenAiPricing
    {
        /// <summary>Price per input token.</summary>
        [JsonProperty("prompt")]
        public string Prompt { get; set; }

        /// <summary>Price per output token.</summary>
        [JsonProperty("completion")]
        public string Completion { get; set; }

        /// <summary>Price per token read from provider cache.</summary>
        [JsonProperty("input_cache_read")]
        public string InputCacheRead { get; set; }

        /// <summary>Price per token written to provider cache.</summary>
        [JsonProperty("input_cache_write")]
        public string InputCacheWrite { get; set; }

        /// <summary>Flat input price per token ("input_price"), LiteLLM-style providers.</summary>
        [JsonProperty("input_price", NullValueHandling = NullValueHandling.Ignore)]
        public string InputPrice { get; set; }

        /// <summary>Flat cached-input price per token ("cached_price"), LiteLLM-style providers.</summary>
        [JsonProperty("cached_price", NullValueHandling = NullValueHandling.Ignore)]
        public string CachedPrice { get; set; }

        /// <summary>Flat output price per token ("output_price"), LiteLLM-style providers.</summary>
        [JsonProperty("output_price", NullValueHandling = NullValueHandling.Ignore)]
        public string OutputPrice { get; set; }
    }

    /// <summary>
    /// Tolerantly converts the "pricing" field, which appears in several shapes across OpenAI-compatible providers.
    /// </summary>
    internal class OpenAiPricingConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(OpenAiPricing);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var pricing = new OpenAiPricing();

            if (reader.TokenType == JsonToken.Null)
                return pricing;

            if (reader.TokenType == JsonToken.StartObject)
            {
                while (reader.Read() && reader.TokenType != JsonToken.EndObject)
                {
                    if (reader.TokenType != JsonToken.PropertyName)
                        continue;

                    var property = (string)reader.Value;

                    if (!reader.Read())
                        return pricing;

                    var value = ReadPricingValue(reader);

                    switch (property)
                    {
                        case "prompt":
                            pricing.Prompt = value;
                            break;
                        case "completion":
                            pricing.Completion = value;
                            break;
                        case "input_cache_read":
                            pricing.InputCacheRead = value;
                            break;
                        case "input_cache_write":
                            pricing.InputCacheWrite = value;
                            break;
                        case "input_price":
                            pricing.InputPrice = value;
                            break;
                        case "cached_price":
                            pricing.CachedPrice = value;
                            break;
                        case "output_price":
                            pricing.OutputPrice = value;
                            break;
                        default:
                            break;
                    }
                }

                return pricing;
            }

            if (reader.TokenType == JsonToken.StartArray)
            {
                List<PricingTier> tiers;
                try
                {
                    tiers = serializer.Deserialize<List<PricingTier>>(reader);
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"OpenAiPricingConverter: failed to parse pricing tiers: {ex.Message}");
                    return pricing;
                }

                if (tiers == null || tiers.Count == 0)
                    return pricing;

                var tier = tiers.FirstOrDefault(t => t.PromptTokensThreshold.HasValue && t.PromptTokensThreshold.Value == 0) ?? tiers[0];

                if (tier.InputPrice.HasValue)
                    pricing.InputPrice = tier.InputPrice.Value.ToString(CultureInfo.InvariantCulture);
                if (tier.CachedPrice.HasValue)
                    pricing.CachedPrice = tier.CachedPrice.Value.ToString(CultureInfo.InvariantCulture);
                if (tier.OutputPrice.HasValue)
                    pricing.OutputPrice = tier.OutputPrice.Value.ToString(CultureInfo.InvariantCulture);

                return pricing;
            }

            return pricing;
        }

        /// <summary>
        /// Reads a single pricing value as a per-token string.
        /// </summary>
        private static string ReadPricingValue(JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonToken.String:
                    return (string)reader.Value;
                case JsonToken.Integer:
                case JsonToken.Float:
                    return Convert.ToString(reader.Value, CultureInfo.InvariantCulture);
                case JsonToken.StartObject:
                    return ReadNestedPricePerToken(reader);
                case JsonToken.StartArray:
                    reader.Skip();
                    return null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Reads a nested price object, e.g. {"price_per_m":1500,"price_per_m_decimal":"0.15",...}, whose price is
        /// expressed in USD per 1M tokens, and returns it as a per-token string (divided by 1_000_000) so the rest
        /// of the pipeline keeps its per-token contract.
        /// </summary>
        private static string ReadNestedPricePerToken(JsonReader reader)
        {
            decimal? perMillionFromDecimal = null;
            decimal? perMillionFromFixedPoint = null;

            while (reader.Read() && reader.TokenType != JsonToken.EndObject)
            {
                if (reader.TokenType != JsonToken.PropertyName)
                    continue;

                var property = (string)reader.Value;

                if (!reader.Read())
                    break;

                if (property == "price_per_m_decimal")
                {
                    if (reader.TokenType == JsonToken.String &&
                        decimal.TryParse((string)reader.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                        perMillionFromDecimal = parsed;
                    else if (TryReadNumber(reader, out var decimalAsNumber))
                        perMillionFromDecimal = decimalAsNumber;
                    else
                        SkipContainer(reader);
                }
                else if (property == "price_per_m")
                {
                    if (TryReadNumber(reader, out var fixedPoint))
                        perMillionFromFixedPoint = fixedPoint;
                    else
                        SkipContainer(reader);
                }
                else
                {
                    SkipContainer(reader);
                }
            }

            var perMillion = perMillionFromDecimal
                ?? (perMillionFromFixedPoint.HasValue ? perMillionFromFixedPoint.Value / 10_000m : (decimal?)null);

            if (!perMillion.HasValue)
                return null;

            return (perMillion.Value / 1_000_000m).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Attempts to read a numeric JSON token as a decimal.
        /// </summary>
        private static bool TryReadNumber(JsonReader reader, out decimal value)
        {
            value = 0m;

            if (reader.TokenType != JsonToken.Integer && reader.TokenType != JsonToken.Float)
                return false;

            try
            {
                value = Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Consumes a nested object/array value so the reader stays balanced. Scalars need no action (the token was already consumed).
        /// </summary>
        private static void SkipContainer(JsonReader reader)
        {
            if (reader.TokenType == JsonToken.StartObject || reader.TokenType == JsonToken.StartArray)
                reader.Skip();
        }
    }

    /// <summary>
    /// A single pricing tier as reported by LiteLLM-style providers: per-token numeric prices plus a token threshold.
    /// </summary>
    internal class PricingTier
    {
        [JsonProperty("prompt_tokens_threshold", NullValueHandling = NullValueHandling.Ignore)]
        public long? PromptTokensThreshold { get; set; }

        [JsonProperty("input_price", NullValueHandling = NullValueHandling.Ignore)]
        public double? InputPrice { get; set; }

        [JsonProperty("cached_price", NullValueHandling = NullValueHandling.Ignore)]
        public double? CachedPrice { get; set; }

        [JsonProperty("output_price", NullValueHandling = NullValueHandling.Ignore)]
        public double? OutputPrice { get; set; }
    }

    /// <summary>
    /// Model architecture / modality info (OpenRouter: "architecture").
    /// </summary>
    internal class OpenAiArchitecture
    {
        /// <summary>Modality string, e.g. "text+image-&gt;text".</summary>
        [JsonProperty("modality")]
        public string Modality { get; set; }

        /// <summary>Accepted input modalities, e.g. ["text", "image"].</summary>
        [JsonProperty("input_modalities")]
        public List<string> InputModalities { get; set; }

        /// <summary>Produced output modalities, e.g. ["text"].</summary>
        [JsonProperty("output_modalities")]
        public List<string> OutputModalities { get; set; }
    }
}
