using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        /// <summary>Maximum number of jsonl files scanned when loading or listing sessions.</summary>
        internal const int MaxJsonlFilesToScan = 100;

        /// <summary>
        /// Builds the consistent hourly file name: yyyyMMdd_HH_label.jsonl.
        /// </summary>
        internal static string BuildFileName(DateTime utcNow, string label)
        {
            return $"{utcNow:yyyyMMdd_HH}_{label}.jsonl";
        }

        /// <summary>
        /// Serializes a ChatMessage into a JSON line with type, session_id, timestamp and created_at.
        /// </summary>
        internal static string BuildMessageLine(ChatMessage message, string sessionId, DateTime utcNow)
        {
            object content = message.Content;

            if (content is List<ContentPart> parts)
            {
                int imageCount = parts.Count(p => p.Type == "image_url");
                if (imageCount > 0)
                    content = ChatMessagePlaceholders.FormatImagePlaceholder(imageCount);
            }

            var entry = new Dictionary<string, object>
            {
                { "type", "message" },
                { "session_id", sessionId },
                { "timestamp", utcNow.ToString("o") },
                { "created_at", (message.Timestamp ?? utcNow).ToString("o") },
                { "role", message.Role },
                { "content", content },
                { "tool_call_id", message.ToolCallId },
                { "tool_calls", message.ToolCalls },
                { "model_id", message.ModelId }
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
                DateTime created = ParseTimestamp(obj, "created_at");
                if (created == DateTime.MinValue)
                    created = ParseTimestamp(obj);

                List<ToolCall> toolCalls = null;
                var toolCallsToken = obj["tool_calls"];
                if (toolCallsToken != null && toolCallsToken.Type != JTokenType.Null)
                {
                    toolCalls = toolCallsToken.ToObject<List<ToolCall>>();
                }

                return new ChatMessage(role ?? "unknown", content, toolCallId)
                {
                    ToolCalls = toolCalls,
                    ModelId = obj.Value<string>("model_id"),
                    Timestamp = created == DateTime.MinValue ? (DateTime?)null : created
                };
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Failed to parse chat message from jsonl line", ex);
                return null;
            }
        }

        /// <summary>
        /// Parses one JSON Lines entry.
        /// </summary>
        internal static JObject ParseLine(string line)
        {
            using (var reader = new JsonTextReader(new StringReader(line)) { DateParseHandling = DateParseHandling.None })
            {
                return JObject.Load(reader);
            }
        }

        /// <summary>
        /// Reads a time field of an entry as UTC, or <see cref="DateTime.MinValue"/> when it is absent or unparsable.
        /// </summary>
        internal static DateTime ParseTimestamp(JObject obj, string field = "timestamp")
        {
            string timestamp = obj.Value<string>(field);
            if (!string.IsNullOrEmpty(timestamp)
                && DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
            {
                return parsed.UtcDateTime;
            }

            return DateTime.MinValue;
        }
    }
}
