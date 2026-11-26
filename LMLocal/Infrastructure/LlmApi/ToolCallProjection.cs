using System.Collections.Generic;
using System.IO;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi.Requests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.LlmApi
{
    /// <summary>
    /// Maps Core <see cref="ChatToolCall"/> to and from the chat-completions wire DTO, keeping list order and null entries.
    /// </summary>
    internal static class ToolCallProjection
    {
        internal const string FunctionType = "function";

        internal static List<ToolCall> ToWire(IReadOnlyList<ChatToolCall> calls)
        {
            if (calls == null)
                return null;

            var result = new List<ToolCall>(calls.Count);
            foreach (var call in calls)
            {
                if (call == null)
                {
                    result.Add(null);
                    continue;
                }

                result.Add(new ToolCall
                {
                    Id = call.Id,
                    Type = FunctionType,
                    Function = new FunctionCallDetails
                    {
                        Name = call.Name,
                        Arguments = call.Arguments
                    },
                    ExtraContent = ParseExtraContent(call.ExtraContentJson)
                });
            }

            return result;
        }

        internal static List<ChatToolCall> FromWire(IEnumerable<ToolCall> calls)
        {
            if (calls == null)
                return null;

            var result = new List<ChatToolCall>();
            foreach (var call in calls)
            {
                if (call == null)
                {
                    result.Add(null);
                    continue;
                }

                result.Add(new ChatToolCall
                {
                    Id = call.Id,
                    Name = call.Function?.Name,
                    Arguments = call.Function?.Arguments,
                    ExtraContentJson = ToExtraContentJson(call.ExtraContent)
                });
            }

            return result;
        }

        /// <summary>
        /// Raw JSON to JToken without date parsing (the payload is opaque), or null when absent, null or unparsable.
        /// </summary>
        internal static JToken ParseExtraContent(string extraContentJson)
        {
            if (string.IsNullOrWhiteSpace(extraContentJson))
                return null;

            try
            {
                using (var reader = new JsonTextReader(new StringReader(extraContentJson)) { DateParseHandling = DateParseHandling.None })
                {
                    var token = JToken.ReadFrom(reader);
                    if (reader.Read())
                        return null;

                    return token.Type == JTokenType.Null ? null : token;
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        internal static string ToExtraContentJson(JToken extraContent)
        {
            if (extraContent == null || extraContent.Type == JTokenType.Null)
                return null;

            return extraContent.ToString(Formatting.None);
        }
    }
}
