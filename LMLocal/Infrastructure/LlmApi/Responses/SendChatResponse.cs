using System.Collections.Generic;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.LlmApi.Responses
{
    internal class SendChatResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("object")]
        public string Object { get; set; } // Always "chat.completion"

        [JsonProperty("created")]
        public long Created { get; set; } // Unix timestamp

        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("service_tier")]
        public string ServiceTier { get; set; }

        [JsonProperty("choices")]
        public List<ChatChoice> Choices { get; set; }

        [JsonProperty("usage")]
        public CompletionUsage Usage { get; set; }

        [JsonProperty("system_fingerprint")]
        public string SystemFingerprint { get; set; }
    }

    internal class ChatChoice
    {
        [JsonProperty("index")]
        public int Index { get; set; }

        [JsonProperty("message")]
        public AssistantMessage Message { get; set; }

        [JsonProperty("finish_reason")]
        public string FinishReason { get; set; } // "stop", "length", "tool_calls", "content_filter"

        [JsonProperty("logprobs")]
        public LogprobInfo Logprobs { get; set; }
    }

    internal class AssistantMessage
    {
        [JsonProperty("role")]
        public string Role { get; set; } // "assistant"

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("refusal")]
        public string Refusal { get; set; }

        [JsonProperty("tool_calls")]
        public List<ToolCall> ToolCalls { get; set; }

        [JsonProperty("audio")]
        public AudioResponse Audio { get; set; }
    }

    internal class ToolCall
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; } //function or custom

        [JsonProperty("function")]
        public FunctionCallInfo Function { get; set; }
    }

    internal class FunctionCallInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("arguments")]
        public string Arguments { get; set; } // JSON string
    }
    internal class CompletionUsage
    {
        [JsonProperty("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonProperty("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonProperty("total_tokens")]
        public int TotalTokens { get; set; }

        [JsonProperty("prompt_tokens_details")]
        public PromptDetails PromptTokensDetails { get; set; }

        [JsonProperty("completion_tokens_details")]
        public CompletionDetails CompletionTokensDetails { get; set; }
    }

    internal class PromptDetails
    {
        [JsonProperty("cached_tokens")]
        public int CachedTokens { get; set; }

        [JsonProperty("audio_tokens")]
        public int AudioTokens { get; set; }
    }

    internal class CompletionDetails
    {
        [JsonProperty("reasoning_tokens")]
        public int ReasoningTokens { get; set; }

        [JsonProperty("audio_tokens")]
        public int AudioTokens { get; set; }

        [JsonProperty("accepted_prediction_tokens")]
        public int AcceptedPredictionTokens { get; set; }
    }
    internal class LogprobInfo
    {
        [JsonProperty("content")]
        public List<TokenLogprob> Content { get; set; }
    }

    internal class TokenLogprob
    {
        [JsonProperty("token")]
        public string Token { get; set; }

        [JsonProperty("logprob")]
        public double Logprob { get; set; }

        [JsonProperty("bytes")]
        public List<int> Bytes { get; set; }

        [JsonProperty("top_logprobs")]
        public List<TokenLogprob> TopLogprobs { get; set; }
    }

    internal class AudioResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("data")]
        public string Data { get; set; } // Base64 audio data

        [JsonProperty("transcript")]
        public string Transcript { get; set; }

        [JsonProperty("expires_at")]
        public long ExpiresAt { get; set; }
    }
}
