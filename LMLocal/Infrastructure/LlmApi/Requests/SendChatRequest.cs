using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.LlmApi.Requests
{
    internal class SendChatRequest
    {
        /// <summary>
        /// Model identifier to use for this request.
        /// </summary>
        [JsonProperty("model")]
        public string Model { get; set; }

        /// <summary>
        /// A list of messages comprising the conversation so far.
        /// </summary>
        [JsonProperty("messages")]
        public List<Message> Messages { get; set; }

        /// <summary>
        /// Whether to store this conversation in the API logs.
        /// </summary>
        [JsonProperty("store", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Store { get; set; }

        /// <summary>
        /// If set, partial message deltas will be sent as SSE (Server-Sent Events) stream.
        /// </summary>
        [JsonProperty("stream", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Stream { get; set; }

        /// <summary>
        /// What sampling temperature to use, between 0 and 2. Higher values like 0.8 will make the output more random,
        /// while lower values like 0.2 will make it more focused and deterministic.
        /// </summary>
        [JsonProperty("temperature", NullValueHandling = NullValueHandling.Ignore)]
        public double? Temperature { get; set; }

        /// <summary>
        /// An alternative to sampling with temperature, called nucleus sampling, where the model considers the results
        /// of the tokens with top_p probability mass. So 0.1 means only the tokens comprising the top 10% probability mass are considered.
        /// </summary>
        [JsonProperty("top_p", NullValueHandling = NullValueHandling.Ignore)]
        public double? TopP { get; set; }

        /// <summary>
        /// The maximum number of tokens that can be generated in the chat completion. This value can be used to control costs for using the API.
        /// </summary>
        [JsonProperty("max_completion_tokens", NullValueHandling = NullValueHandling.Ignore)]
        public int? MaxCompletionTokens { get; set; }

        /// <summary>
        /// Number between -2.0 and 2.0. Positive values penalize new tokens based on whether they appear in the prompt,
        /// encouraging the model to talk about new topics.
        /// </summary>
        [JsonProperty("presence_penalty", NullValueHandling = NullValueHandling.Ignore)]
        public double? PresencePenalty { get; set; }

        /// <summary>
        /// Number between -2.0 and 2.0. Positive values penalize new tokens based on their existing frequency in the text so far,
        /// encouraging the model to use diverse language.
        /// </summary>
        [JsonProperty("frequency_penalty", NullValueHandling = NullValueHandling.Ignore)]
        public double? FrequencyPenalty { get; set; }

        /// <summary>
        /// Controls the effort level for reasoning. Only used if the model supports extended thinking.
        /// Valid values: "none", "low", "medium", "high".
        /// </summary>
        [JsonProperty("reasoning_effort", NullValueHandling = NullValueHandling.Ignore)]
        public string ReasoningEffort { get; set; }

        /// <summary>
        /// An object specifying the format that the model must output.
        /// </summary>
        [JsonProperty("response_format", NullValueHandling = NullValueHandling.Ignore)]
        public object ResponseFormat { get; set; }

        /// <summary>
        /// Up to 4 sequences where the API will stop generating further tokens. Can be a string or array of strings.
        /// </summary>
        [JsonProperty("stop", NullValueHandling = NullValueHandling.Ignore)]
        public object Stop { get; set; }

        /// <summary>
        /// Options for streaming response. Only set this when you set stream: true.
        /// </summary>
        [JsonProperty("stream_options", NullValueHandling = NullValueHandling.Ignore)]
        public StreamOptions StreamOptions { get; set; }

        /// <summary>List of tools that the model can call.</summary>
        [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)]
        public List<OpenAiToolDefinition> Tools { get; set; }

        /// <summary>Controll if tool can call function, 
        /// none means the model will not call any tool and instead generates a message. 
        /// auto means the model can pick between generating a message or calling one or more tools. 
        /// required means the model must call one or more tools. </summary>
        [JsonProperty("tool_choice", NullValueHandling = NullValueHandling.Ignore)]
        public object ToolChoice { get; set; }

        /// <summary>Allow parallel tool calls.</summary>
        [JsonProperty("parallel_tool_calls", NullValueHandling = NullValueHandling.Ignore)]
        public bool? ParallelToolCalls { get; set; }
    }

    public class FunctionParameters
    {
        [JsonProperty("type")] public string Type { get; set; } = "object";
        [JsonProperty("properties")] public Dictionary<string, object> Properties { get; set; }
        [JsonProperty("required", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Required { get; set; }
    }

    public class FunctionDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
        public string Description { get; set; }
        [JsonProperty("parameters", NullValueHandling = NullValueHandling.Ignore)]
        public FunctionParameters Parameters { get; set; }
    }

    /// <summary>
    /// OpenAI Chat Completions API tool definition format.
    /// Wraps a function definition with type information.
    /// </summary>
    public class OpenAiToolDefinition
    {
        [JsonProperty("type")] public string Type { get; set; } = "function";
        [JsonProperty("function")] public FunctionDefinition Function { get; set; }
    }

    /// <summary>
    /// Options for streaming response. Set only when stream is true.
    /// </summary>
    public class StreamOptions
    {
        /// <summary>
        /// If true, an additional chunk will be streamed when the stream reaches [DONE] with token usage information.
        /// </summary>
        [JsonProperty("include_usage", NullValueHandling = NullValueHandling.Ignore)]
        public bool? IncludeUsage { get; set; }
    }

    /// <summary>
    /// A message in the conversation. Can contain text or multimodal content.
    /// </summary>
    public class Message
    {
        /// <summary>
        /// The role of the author of this message. One of: "developer", "system", "user", "assistant", "tool".
        /// </summary>
        [JsonProperty("role")]
        public string Role { get; set; }

        /// <summary>
        /// The content of the message. Can be a string or an array of content parts (for multimodal content).
        /// </summary>
        [JsonProperty("content")]
        public object Content { get; set; }

        /// <summary>
        /// An optional name for the participant. Provides a name to disambiguate participants with the same role.
        /// </summary>
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        /// <summary>
        /// Tool call that this message is responding to. Required when role is "tool".
        /// </summary>
        [JsonProperty("tool_call_id", NullValueHandling = NullValueHandling.Ignore)]
        public string ToolCallId { get; set; }

        /// <summary>
        /// 
        /// </summary>
        [JsonProperty("tool_calls", NullValueHandling = NullValueHandling.Ignore)]
        public List<ToolCall> ToolCalls { get; set; }
    }

    public class ToolCall
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; } = "function";

        [JsonProperty("function")]
        public FunctionCallDetails Function { get; set; }
    }

    public class FunctionCallDetails
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// Arguments comes as json string in the request, and it's the responsibility of the client to parse it according to the function definition. 
        /// </summary>
        [JsonProperty("arguments")]
        public string Arguments { get; set; }
    }

    public class ContentPart
    {
        [JsonProperty("type")]
        public string Type { get; set; } // "text", "image_url", "input_audio"

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }

        [JsonProperty("image_url", NullValueHandling = NullValueHandling.Ignore)]
        public ImageUrlInfo ImageUrl { get; set; }
    }

    public class ImageUrlInfo
    {
        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("detail", NullValueHandling = NullValueHandling.Ignore)]
        public string Detail { get; set; } // "auto", "low", "high"
    }
}
