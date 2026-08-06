using System;
using System.Collections.Generic;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi.Requests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Persistence
{
    /// <summary>
    /// Pure serialization helpers for the JSON Lines chat log format. No I/O, no dependencies on services.
    /// </summary>
    internal static class ChatLogSerializer
    {
        /// <summary>Maximum length of a session prompt shown in the history dialog before truncation. The dialog clamps prompt text to two lines via CSS, so longer prompts are already clipped visually; this keeps the stored/transferred prompt compact.</summary>
        internal const int MaxPromptLength = 200;

        /// <summary>Maximum number of jsonl files scanned when loading or listing sessions.</summary>
        internal const int MaxJsonlFilesToScan = 50;

        /// <summary>Default limit for the session list returned by GetChatSessionsAsync.</summary>
        internal const int DefaultSessionListLimit = 200;

        /// <summary>
        /// Builds the consistent hourly file name: yyyyMMdd_HH_label.jsonl.
        /// </summary>
        internal static string BuildFileName(DateTime utcNow, string label)
        {
            return $"{utcNow:yyyyMMdd_HH}_{label}.jsonl";
        }

        /// <summary>
        /// Serializes a ChatMessage into a JSON line with type, session_id, and timestamp.
        /// </summary>
        internal static string BuildMessageLine(ChatMessage message, string sessionId, DateTime utcNow)
        {
            var entry = new Dictionary<string, object>
            {
                { "type", "message" },
                { "session_id", sessionId },
                { "timestamp", utcNow.ToString("o") },
                { "role", message.Role },
                { "content", message.Content },
                { "tool_call_id", message.ToolCallId },
                { "tool_calls", message.ToolCalls }
            };

            return JsonConvert.SerializeObject(entry) + Environment.NewLine;
        }

        /// <summary>
        /// Serializes a session_start marker line for a new session boundary.
        /// </summary>
        internal static string BuildSessionStartMarker(string sessionId, DateTime utcNow)
        {
            var marker = new Dictionary<string, object>
            {
                { "type", "session_start" },
                { "session_id", sessionId },
                { "timestamp", utcNow.ToString("o") }
            };

            return JsonConvert.SerializeObject(marker) + Environment.NewLine;
        }

        /// <summary>
        /// Deserializes a JObject (from jsonl) into a ChatMessage, or null if the line is malformed.
        /// </summary>
        internal static ChatMessage ParseChatMessage(JObject obj)
        {
            try
            {
                string role = obj.Value<string>("role");
                object content = obj["content"]?.ToObject<object>();
                string toolCallId = obj.Value<string>("tool_call_id");

                List<ToolCall> toolCalls = null;
                var toolCallsToken = obj["tool_calls"];
                if (toolCallsToken != null && toolCallsToken.Type != JTokenType.Null)
                {
                    toolCalls = toolCallsToken.ToObject<List<ToolCall>>();
                }

                return new ChatMessage(role ?? "unknown", content, toolCallId)
                {
                    ToolCalls = toolCalls
                };
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Failed to parse chat message from jsonl line", ex);
                return null;
            }
        }

        /// <summary>
        /// Truncates a session prompt to MaxPromptLength chars, appending an ellipsis when truncated.
        /// </summary>
        internal static string TruncatePrompt(string content)
        {
            if (string.IsNullOrEmpty(content)) return string.Empty;
            return content.Length > MaxPromptLength
                ? content.Substring(0, MaxPromptLength) + "..."
                : content;
        }
    }
}
