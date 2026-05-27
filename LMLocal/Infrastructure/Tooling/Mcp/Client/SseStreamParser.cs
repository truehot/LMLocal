using System;
using LMLocal.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Mcp
{
    /// <summary>
    /// Parser for Server-Sent Events (SSE) format used in MCP streaming.
    /// </summary>
    internal static class SseStreamParser
    {
        private const string DataPrefix = "data: ";
        private const string EventPrefix = "event: ";
        private const string CommentPrefix = ":";
        private const string DoneMarker = "[DONE]";

        /// <summary>
        /// Attempts to parse a single SSE line into its components.
        /// </summary>
        public static SseMessage TryParseSseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            if (line.StartsWith(CommentPrefix) && !line.StartsWith(DataPrefix) && !line.StartsWith(EventPrefix))
            {
                return new SseMessage
                {
                    Type = SseMessageType.Comment,
                    RawData = line
                };
            }

            if (line.StartsWith(EventPrefix))
            {
                var eventType = line.Substring(EventPrefix.Length).Trim();
                return new SseMessage
                {
                    Type = SseMessageType.Event,
                    EventType = eventType
                };
            }

            if (line.StartsWith(DataPrefix))
            {
                var dataJson = line.Substring(DataPrefix.Length).Trim();

                if (dataJson == DoneMarker)
                {
                    return new SseMessage
                    {
                        Type = SseMessageType.Done,
                        RawData = dataJson
                    };
                }

                return new SseMessage
                {
                    Type = SseMessageType.Data,
                    RawData = dataJson,
                    ParsedData = TryParseJson(dataJson)
                };
            }

            return null;
        }

        /// <summary>
        /// Checks if a line is a valid SSE message (starts with data:, event:, or :).
        /// </summary>
        public static bool IsSseMessage(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            return line.StartsWith(DataPrefix) ||
                   line.StartsWith(EventPrefix) ||
                   line.StartsWith(CommentPrefix);
        }

        /// <summary>
        /// Extracts the JSON data from an SSE data field.
        /// </summary>
        public static string ExtractSseData(string line)
        {
            if (!line.StartsWith(DataPrefix))
                return null;

            return line.Substring(DataPrefix.Length).Trim();
        }

        /// <summary>
        /// Extracts the event type from an SSE event field.
        /// </summary>
        public static string ExtractSseEventType(string line)
        {
            if (!line.StartsWith(EventPrefix))
                return null;

            return line.Substring(EventPrefix.Length).Trim();
        }

        /// <summary>
        /// Safely parses JSON string to JObject.
        /// </summary>
        private static JObject TryParseJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JObject.Parse(json);
            }
            catch (JsonException ex)
            {
                InternalLogger.Debug($"SSE JSON parse error: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Unexpected error while parsing SSE JSON", ex);
                return null;
            }
        }
    }

    /// <summary>
    /// Represents a single SSE message with parsed components.
    /// </summary>
    internal class SseMessage
    {
        /// <summary>
        /// Type of SSE message.
        /// </summary>
        public SseMessageType Type { get; set; }

        /// <summary>
        /// Raw string data (for data and comment messages).
        /// </summary>
        public string RawData { get; set; }

        /// <summary>
        /// Event type if message is of type Event.
        /// </summary>
        public string EventType { get; set; }

        /// <summary>
        /// Parsed JSON object if message is of type Data and contains valid JSON.
        /// </summary>
        public JObject ParsedData { get; set; }
    }

    /// <summary>
    /// Types of SSE messages.
    /// </summary>
    internal enum SseMessageType
    {
        /// <summary>
        /// Comment line (starts with ':'), typically used for keep-alive.
        /// </summary>
        Comment,

        /// <summary>
        /// Event type marker (starts with 'event:').
        /// </summary>
        Event,

        /// <summary>
        /// Data field (starts with 'data:').
        /// </summary>
        Data,

        /// <summary>
        /// Special [DONE] marker indicating stream completion.
        /// </summary>
        Done
    }
}
