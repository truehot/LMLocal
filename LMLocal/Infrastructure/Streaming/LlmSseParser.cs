using System.Collections.Generic;
using LMLocal.Core.Models;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Streaming
{
    /// <summary>
    /// Parser for Server-Sent Events (SSE) streams from OpenAI-compatible providers.
    /// Handles JSON tool_calls in delta.tool_calls.
    /// 
    /// Returns derived StreamChunk types:
    /// - TextStreamChunk for content/reasoning/tool arguments
    /// - ToolCallMetadataChunk for tool metadata
    /// - CompletionStreamChunk for final data
    /// </summary>
    internal class LlmSseParser
    {
        private const string DoneMarker = "data: [DONE]";
        private const string DataPrefix = "data: ";

        /// <summary>
        /// Extracts all completed chunks from an SSE line, including buffered XML tool calls.
        /// </summary>
        public List<StreamChunk> ExtractDeltas(string line)
        {
            var chunks = new List<StreamChunk>();

            if (line == DoneMarker)
            {
                chunks.Add(new CompletionStreamChunk(finishReason: "stop"));
                return chunks;
            }

            if (!line.StartsWith(DataPrefix))
            {
                return chunks;
            }

            var dataJson = line.Substring(DataPrefix.Length).Trim();
            if (string.IsNullOrEmpty(dataJson))
            {
                return chunks;
            }

            JObject json;
            try
            {
                json = JObject.Parse(dataJson);
            }
            catch
            {
                return chunks;
            }

            if (json["error"] is JObject err)
            {
                chunks.Add(new ErrorStreamChunk(
                    err["message"]?.ToString() ?? json["message"]?.ToString(),
                    err["type"]?.ToString(),
                    err["code"]?.ToString()));
                return chunks;
            }


            var contentChunks = ExtractStreamContents(json);
            if (contentChunks.Count > 0)
            {
                chunks.AddRange(contentChunks);
            }

            if (json["choices"] is JArray choicesToken && choicesToken.Count > 0)
            {
                var firstChoice = choicesToken[0] as JObject;
                var finishReason = firstChoice?["finish_reason"]?.ToString();
                if (!string.IsNullOrEmpty(finishReason))
                {
                    var usage = ExtractUsage(json);
                    chunks.Add(new CompletionStreamChunk(
                        finishReason: finishReason,
                        totalTokens: usage?.TotalTokens,
                        promptTokens: usage?.PromptTokens,
                        completionTokens: usage?.CompletionTokens,
                        reasoningTokens: usage?.ReasoningTokens,
                        cachedTokens: usage?.CachedTokens,
                        systemFingerprint: usage?.SystemFingerprint));
                    return chunks;
                }
            }

            if (chunks.Count == 0)
            {
                if (json["usage"] != null && (!(json["choices"] is JArray choices) || choices.Count == 0))
                {
                    var usageChunk = ExtractUsage(json);
                    if (usageChunk != null)
                    {
                        chunks.Add(usageChunk);
                    }
                    return chunks;
                }
            }

            return chunks;
        }

        private CompletionStreamChunk ExtractUsage(JObject json)
        {
            var usageToken = json["usage"];
            if (usageToken == null || usageToken.Type != JTokenType.Object)
                return null;

            var usage = (JObject)usageToken;

            int? totalTokens = usage["total_tokens"]?.Value<int?>();
            int? promptTokens = usage["prompt_tokens"]?.Value<int?>();
            int? completionTokens = usage["completion_tokens"]?.Value<int?>();

            int? reasoningTokens = null;
            var detailsToken = usage["completion_tokens_details"];
            if (detailsToken?.Type == JTokenType.Object)
            {
                reasoningTokens = detailsToken["reasoning_tokens"]?.Value<int?>();
            }

            int? cachedTokens = null;
            var promptDetails = usage["prompt_tokens_details"];
            if (promptDetails?.Type == JTokenType.Object)
            {
                cachedTokens = promptDetails["cached_tokens"]?.Value<int?>();
            }

            string systemFingerprint = json["system_fingerprint"]?.ToString();

            return new CompletionStreamChunk(
                totalTokens: totalTokens,
                promptTokens: promptTokens,
                completionTokens: completionTokens,
                reasoningTokens: reasoningTokens,
                cachedTokens: cachedTokens,
                systemFingerprint: systemFingerprint);
        }

        /// <summary>
        /// Extracts all completed chunks from JSON, including handling tool call blocks.
        /// </summary>
        private List<StreamChunk> ExtractStreamContents(JObject json)
        {
            var chunks = new List<StreamChunk>();

            if (!(json["choices"] is JArray choices) || choices.Count == 0)
            {
                return chunks;
            }

            var delta = choices[0]?["delta"];
            if (delta == null || delta.Type == JTokenType.Null)
            {
                return chunks;
            }

            if (delta["tool_calls"] is JArray toolCallsArray && toolCallsArray.Count > 0)
            {
                foreach (var toolCallToken in toolCallsArray)
                {
                    if (!(toolCallToken is JObject toolCall)) continue;

                    var index = toolCall["index"]?.Value<int?>();
                    var callId = toolCall["id"]?.ToString();
                    var functionName = toolCall["function"]?["name"]?.ToString();
                    var arguments = toolCall["function"]?["arguments"]?.ToString();

                    if (!string.IsNullOrEmpty(functionName) || !string.IsNullOrEmpty(callId))
                    {
                        chunks.Add(new ToolCallMetadataChunk(index ?? 0, callId, functionName, initialArguments: arguments));
                    }
                    else if (!string.IsNullOrEmpty(arguments))
                    {
                        chunks.Add(new TextStreamChunk(arguments, ChunkKind.ToolCallArguments, index));
                    }
                }

                return chunks;
            }

            var reasoning = delta["reasoning_content"]?.ToString();
            if (!string.IsNullOrEmpty(reasoning))
            {
                chunks.Add(new TextStreamChunk(reasoning, ChunkKind.Reasoning));
                return chunks;
            }

            var refusal = delta["refusal"]?.ToString();
            if (!string.IsNullOrEmpty(refusal))
            {
                chunks.Add(new TextStreamChunk(refusal, ChunkKind.Content));
            }

            var content = delta["content"]?.ToString();
            if (!string.IsNullOrEmpty(content))
            {
                chunks.Add(new TextStreamChunk(content, ChunkKind.Content));
            }

            return chunks;
        }
    }
}
