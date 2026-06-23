using System.Collections.Generic;
using System.Linq;
using LMLocal.Infrastructure.LlmApi.Requests;
using LMLocal.Infrastructure.Tooling;

namespace LMLocal.Infrastructure.LlmApi.Converter
{
    /// <summary>
    /// Converts tool definitions from internal MCP format to OpenAI Chat Completions API format.
    /// </summary>
    internal static class ToolDefinitionConverter
    {
        /// <summary>
        /// Converts a list of internal tool definitions to OpenAI tool definitions format.
        /// </summary>
        public static List<OpenAiToolDefinition> ConvertToOpenAiFormat(
            IReadOnlyList<ToolDefinition> internalTools)
        {
            if (internalTools == null || internalTools.Count == 0)
                return new List<OpenAiToolDefinition>();

            return internalTools
                .Select(ConvertSingleTool)
                .ToList();
        }

        private static OpenAiToolDefinition ConvertSingleTool(ToolDefinition internalTool)
        {
            var functionDef = new FunctionDefinition
            {
                Name = internalTool.Name,
                Description = internalTool.Description,
                Parameters = ConvertParameters(internalTool.Parameters)
            };

            return new OpenAiToolDefinition
            {
                Type = "function",
                Function = functionDef
            };
        }

        private static FunctionParameters ConvertParameters(ToolParameters internalParams)
        {
            if (internalParams == null)
            {
                return new FunctionParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, object>(),
                    Required = new List<string>()
                };
            }

            var properties = ConvertProperties(internalParams.Properties);

            return new FunctionParameters
            {
                Type = internalParams.Type ?? "object",
                Properties = properties,
                Required = internalParams.Required
            };
        }
        private static Dictionary<string, object> ConvertProperties(
            Dictionary<string, ToolDetails> internalProperties)
        {
            if (internalProperties == null || internalProperties.Count == 0)
                return new Dictionary<string, object>();

            var result = new Dictionary<string, object>();

            foreach (var kvp in internalProperties)
            {
                result[kvp.Key] = ConvertToolDetails(kvp.Value);
            }

            return result;
        }

        private static object ConvertToolDetails(ToolDetails detail)
        {
            var obj = new Dictionary<string, object>
            {
                { "type", detail.Type },
            };

            if (detail.Description != null)
                obj["description"] = detail.Description;

            if (detail.Items != null)
                obj["items"] = ConvertToolDetails(detail.Items);

            if (detail.Properties != null && detail.Properties.Count > 0)
                obj["properties"] = ConvertProperties(detail.Properties);

            if (detail.Required != null && detail.Required.Count > 0)
                obj["required"] = detail.Required;

            return obj;
        }
    }
}
