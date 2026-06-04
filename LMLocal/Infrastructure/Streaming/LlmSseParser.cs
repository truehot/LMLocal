using LMLocal.Core.Models;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Streaming
{
    /// <summary>
    /// Stateless parser for Server-Sent Events (SSE) streams from various LLM providers.
    /// Supports multiple response formats:
    /// - Nemotron: XML tool_calls in reasoning_content
    /// - Qwen/Gemma/OpenAI: JSON tool_calls in delta.tool_calls
    /// 
    /// Returns derived StreamChunk types:
    /// - TextStreamChunk for content/reasoning/tool arguments
    /// - ToolCallMetadataChunk for tool metadata
    /// - CompletionStreamChunk for final data
    /// </summary>
    internal static class LlmSseParser
    {
        private const string DoneMarker = "data: [DONE]";
        private const string DataPrefix = "data: ";


        public static StreamChunk ExtractDelta(string line)
        {
            if (line == DoneMarker)
            {
                return new CompletionStreamChunk(finishReason: "stop");
            }

            if (!line.StartsWith(DataPrefix))
            {
                return null;
            }

            var dataJson = line.Substring(DataPrefix.Length).Trim();
            if (string.IsNullOrEmpty(dataJson))
            {
                return null;
            }

            JObject json;
            try
            {
                json = JObject.Parse(dataJson);
            }
            catch
            {
                return null;
            }

            // First try to extract content or tool call data, which is more common in streaming responses
            var contentChunk = ExtractStreamContent(json);
            if (contentChunk != null) {
                return contentChunk;
            }
            

            // If no content or tool call data, check for usage info or finish reason, which may come in a final chunk without choices
            if (json["usage"] != null && (!(json["choices"] is JArray choices) || choices.Count == 0))
            {
                return ExtractUsage(json);
            }

            // If there are choices, check for finish reason or refusal, which may indicate the end of the stream or a refusal to answer
            if (json["choices"] is JArray choicesToken && choicesToken.Count > 0)
            {
                var firstChoice = choicesToken[0] as JObject;
                var finishReason = firstChoice?["finish_reason"]?.ToString();
                if (!string.IsNullOrEmpty(finishReason))
                {
                    return new CompletionStreamChunk(finishReason: finishReason);
                }

                var refusal = firstChoice?["delta"]?["refusal"]?.ToString();
                if (!string.IsNullOrEmpty(refusal))
                {
                    return new CompletionStreamChunk(refusal: refusal);
                }
            }

            return null;
        }

        private static CompletionStreamChunk ExtractUsage(JObject json)
        {
            var usage = json["usage"];
            if (usage == null)
            {
                return null;
            }

            var totalTokens = usage["total_tokens"]?.Value<int?>();
            var promptTokens = usage["prompt_tokens"]?.Value<int?>();
            var completionTokens = usage["completion_tokens"]?.Value<int?>();
            var reasoningTokens = usage["completion_tokens_details"]?["reasoning_tokens"]?.Value<int?>();
            var systemFingerprint = json["system_fingerprint"]?.ToString();

            return new CompletionStreamChunk(
                totalTokens: totalTokens,
                promptTokens: promptTokens,
                completionTokens: completionTokens,
                reasoningTokens: reasoningTokens,
                systemFingerprint: systemFingerprint);
        }

        private static StreamChunk ExtractStreamContent(JObject json)
        {
            if (!(json["choices"] is JArray choices) || choices.Count == 0)
            {
                return null;
            }

            var delta = choices[0]?["delta"];
            if (delta == null || delta.Type == JTokenType.Null)
            {
                return null;
            }

            if (delta["tool_calls"] is JArray toolCallsArray && toolCallsArray.Count > 0)
            {
                var toolCall = toolCallsArray[0] as JObject;

                var index = toolCall["index"]?.Value<int?>();
                var callId = toolCall["id"]?.ToString();
                var functionName = toolCall["function"]?["name"]?.ToString();
                var arguments = toolCall["function"]?["arguments"]?.ToString();

                if (!string.IsNullOrEmpty(functionName) || !string.IsNullOrEmpty(callId))
                {
                    return new ToolCallMetadataChunk(index ?? 0, callId, functionName, initialArguments: arguments);
                }

                if (!string.IsNullOrEmpty(arguments))
                {
                    return new TextStreamChunk(arguments, ChunkKind.ToolCallArguments, index);
                }
            }

            var reasoning = delta["reasoning_content"]?.ToString();
            if (!string.IsNullOrEmpty(reasoning))
            {
                if (reasoning.Contains("<tool_call>") || reasoning.Contains("</tool_call>") || reasoning.Contains("<function="))
                {
                    return new TextStreamChunk(reasoning, ChunkKind.ToolCallArguments, toolCallIndex: 0);
                }

                return new TextStreamChunk(reasoning, ChunkKind.Reasoning);
            }

            var content = delta["content"]?.ToString();
            if (!string.IsNullOrEmpty(content))
            {
                return new TextStreamChunk(content, ChunkKind.Content);
            }

            return null;
        }
    }
}
