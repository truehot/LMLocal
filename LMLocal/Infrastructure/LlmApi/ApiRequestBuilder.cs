using System;
using System.Collections.Generic;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi.Converter;
using LMLocal.Infrastructure.LlmApi.Requests;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Tooling;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.LlmApi
{
    internal interface IApiRequestBuilder
    {
        /// <summary>
        /// Builds objects from message/model contexts.
        /// </summary>
        SendChatRequest BuildRequest(MessageContext messageContext, ModelContext modelContext, bool stream, bool useTools = true);
    }


    internal class ApiRequestBuilder : IApiRequestBuilder
    {
        private readonly ISettingsManager _settingsManager;
        private readonly ICompositeToolFactory _toolFactory;

        public ApiRequestBuilder(ISettingsManager settingsManager, ICompositeToolFactory toolFactory)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _toolFactory = toolFactory ?? throw new ArgumentNullException(nameof(toolFactory));
        }

        public SendChatRequest BuildRequest(MessageContext messageContext, ModelContext modelContext, bool stream, bool useTools = true)
        {
            if (messageContext == null) throw new ArgumentNullException(nameof(messageContext));
            if (modelContext == null) throw new ArgumentNullException(nameof(modelContext));

            var messages = new List<Message>();

            foreach (var msg in messageContext.Input)
            {
                var apiMessage = new Message
                {
                    Role = msg.Role,
                    Content = msg.Content,
                    ToolCallId = msg.ToolCallId,
                    ToolCalls = ConvertToolCalls(msg.ToolCalls)
                };
                messages.Add(apiMessage);
            }

            var request = new SendChatRequest
            {
                Model = modelContext.ModelId,
                Messages = messages,
                Stream = stream,
                Temperature = modelContext.Temperature,
                TopP = modelContext.TopP,
                MaxCompletionTokens = modelContext.MaxOutputTokens,
                PresencePenalty = modelContext.PresencePenalty,
                FrequencyPenalty = modelContext.FrequencyPenalty,
                ReasoningEffort = modelContext.Reasoning,
                StreamOptions = stream ? new StreamOptions { IncludeUsage = stream } : null
            };

            if (_settingsManager.Current.EnableAiTools && useTools)
            {
                try
                {
                    var vsTools = _toolFactory.GetAllToolDefinitions();
                    if (vsTools.Count > 0)
                    {
                        var openAiTools = ToolDefinitionConverter.ConvertToOpenAiFormat(vsTools);
                        request.Tools = openAiTools;
                        request.ToolChoice = "auto";
                        request.ParallelToolCalls = true;
                    }
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"ApiRequestBuilder: failed to add tools to request: {ex.Message}");
                }
            }

            return request;
        }

        private static List<ToolCall> ConvertToolCalls(object toolCalls)
        {
            if (toolCalls == null)
                return null;

            if (toolCalls is List<ToolCall> typed)
                return typed;

            if (toolCalls is JArray jArray)
                return jArray.ToObject<List<ToolCall>>();

            return null;
        }
    }
}
